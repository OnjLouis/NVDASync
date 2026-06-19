using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace NvdaAddonSync
{
    internal sealed class AddonManifest
    {
        public string Name { get; set; }
        public string Summary { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public string SourcePath { get; set; }
        public bool IsArchive { get; set; }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Summary))
                {
                    return Summary;
                }
                if (!string.IsNullOrWhiteSpace(Name))
                {
                    return Name;
                }
                return Path.GetFileNameWithoutExtension(SourcePath);
            }
        }

        public string InstallFolderName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Name))
                {
                    return SanitizeFolderName(Name);
                }
                return SanitizeFolderName(Path.GetFileNameWithoutExtension(SourcePath));
            }
        }

        internal static string SanitizeFolderName(string value)
        {
            var builder = new StringBuilder();
            foreach (var ch in (value ?? string.Empty).Trim())
            {
                if (Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0)
                {
                    continue;
                }
                builder.Append(ch);
            }
            var result = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(result) ? "addon" : result;
        }
    }

    internal sealed class AddonInstallResult
    {
        public int AddonsFound { get; set; }
        public int AddonsInstalled { get; set; }
        public int AddonsSkipped { get; set; }
        public int TargetsUpdated { get; set; }
        public int UnavailableTargets { get; set; }
        public int FilesCopied { get; set; }
        public int ItemsIgnored { get; set; }
        public int Errors { get; set; }
    }

    internal static class AddonPackService
    {
        public static event Action<string> Message;

        public static List<AddonManifest> ReadInstalledAddons(string nvdaConfigFolder)
        {
            var addonsFolder = SyncEngine.ResolveAddonsDirectory(nvdaConfigFolder, "NVDA folder", true);
            var addons = new List<AddonManifest>();
            if (!Directory.Exists(addonsFolder))
            {
                return addons;
            }

            foreach (var directory in Directory.EnumerateDirectories(addonsFolder))
            {
                var manifestPath = Path.Combine(directory, "manifest.ini");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }
                var manifest = ReadManifestFile(manifestPath);
                manifest.SourcePath = directory;
                manifest.IsArchive = false;
                addons.Add(manifest);
            }
            addons.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            return addons;
        }

        public static void ExportPackFile(string nvdaConfigFolder, string outputFile)
        {
            var addons = ReadInstalledAddons(nvdaConfigFolder);
            var builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine("  \"format\": \"nvda-sync-addon-pack\",");
            builder.AppendLine("  \"formatVersion\": 1,");
            builder.AppendLine("  \"createdBy\": \"NVDA Sync\",");
            builder.AppendLine("  \"createdAtUtc\": \"" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\",");
            builder.AppendLine("  \"sourceFolder\": \"" + JsonEscape(SyncEngine.ResolveNvdaConfigDirectory(nvdaConfigFolder, "NVDA folder", true)) + "\",");
            builder.AppendLine("  \"addons\": [");
            for (var i = 0; i < addons.Count; i++)
            {
                var addon = addons[i];
                builder.AppendLine("    {");
                builder.AppendLine("      \"name\": \"" + JsonEscape(addon.Name) + "\",");
                builder.AppendLine("      \"summary\": \"" + JsonEscape(addon.Summary) + "\",");
                builder.AppendLine("      \"version\": \"" + JsonEscape(addon.Version) + "\",");
                builder.AppendLine("      \"author\": \"" + JsonEscape(addon.Author) + "\",");
                builder.AppendLine("      \"description\": \"" + JsonEscape(addon.Description) + "\",");
                builder.AppendLine("      \"folder\": \"" + JsonEscape(Path.GetFileName(addon.SourcePath)) + "\"");
                builder.Append("    }");
                if (i + 1 < addons.Count)
                {
                    builder.Append(",");
                }
                builder.AppendLine();
            }
            builder.AppendLine("  ]");
            builder.AppendLine("}");

            var parent = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }
            File.WriteAllText(outputFile, builder.ToString(), new UTF8Encoding(false));
            Log("Exported add-on pack with " + addons.Count + " add-ons to " + outputFile);
        }

        public static AddonInstallResult InstallFromDirectory(string sourceFolder, IEnumerable<string> secondaryFolders, bool excludePythonCache, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sourceFolder))
            {
                throw new InvalidOperationException("Choose a source folder containing add-ons.");
            }
            sourceFolder = Path.GetFullPath(Environment.ExpandEnvironmentVariables(sourceFolder.Trim()));
            if (!Directory.Exists(sourceFolder))
            {
                throw new DirectoryNotFoundException("Source folder does not exist: " + sourceFolder);
            }

            var addons = FindAddonSources(sourceFolder);
            var result = new AddonInstallResult();
            result.AddonsFound = addons.Count;
            if (addons.Count == 0)
            {
                Log("No valid add-ons were found in " + sourceFolder);
                return result;
            }

            foreach (var secondary in secondaryFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(secondary))
                {
                    continue;
                }

                string targetAddonsFolder;
                try
                {
                    targetAddonsFolder = SyncEngine.ResolveAddonsDirectory(secondary, "Secondary folder", false);
                    if (!IsTargetAvailable(targetAddonsFolder))
                    {
                        result.UnavailableTargets++;
                        Log("Skipped unavailable secondary " + targetAddonsFolder);
                        continue;
                    }
                    if (IsSameOrChild(sourceFolder, targetAddonsFolder) || IsSameOrChild(targetAddonsFolder, sourceFolder))
                    {
                        result.Errors++;
                        Log("Skipped unsafe target because it overlaps the source folder: " + targetAddonsFolder);
                        continue;
                    }
                    Directory.CreateDirectory(targetAddonsFolder);
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    Log("Could not prepare secondary folder: " + ex.Message);
                    continue;
                }

                Log("Installing add-ons to " + targetAddonsFolder);
                foreach (var addon in addons)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    InstallOneAddon(addon, targetAddonsFolder, excludePythonCache, result);
                }
                result.TargetsUpdated++;
            }

            return result;
        }

        public static List<AddonManifest> FindAddonSources(string sourceFolder)
        {
            var addons = new List<AddonManifest>();
            var rootManifest = Path.Combine(sourceFolder, "manifest.ini");
            if (File.Exists(rootManifest))
            {
                var manifest = ReadManifestFile(rootManifest);
                manifest.SourcePath = sourceFolder;
                manifest.IsArchive = false;
                addons.Add(manifest);
                return addons;
            }

            foreach (var directory in Directory.EnumerateDirectories(sourceFolder))
            {
                var manifestPath = Path.Combine(directory, "manifest.ini");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }
                var manifest = ReadManifestFile(manifestPath);
                manifest.SourcePath = directory;
                manifest.IsArchive = false;
                addons.Add(manifest);
            }

            foreach (var archivePath in Directory.EnumerateFiles(sourceFolder, "*.nvda-addon", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var manifest = ReadArchiveManifest(archivePath);
                    manifest.SourcePath = archivePath;
                    manifest.IsArchive = true;
                    addons.Add(manifest);
                }
                catch (Exception ex)
                {
                    Log("Skipped invalid add-on archive " + Path.GetFileName(archivePath) + ": " + ex.Message);
                }
            }

            addons.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            return addons;
        }

        private static void InstallOneAddon(AddonManifest addon, string targetAddonsFolder, bool excludePythonCache, AddonInstallResult result)
        {
            var installName = addon.InstallFolderName;
            var targetFolder = Path.Combine(targetAddonsFolder, installName);
            try
            {
                if (Directory.Exists(targetFolder))
                {
                    DeleteDirectoryRobust(targetFolder);
                }
                Directory.CreateDirectory(targetFolder);

                if (addon.IsArchive)
                {
                    ExtractArchive(addon.SourcePath, targetFolder, result);
                }
                else
                {
                    CopyDirectory(addon.SourcePath, targetFolder, excludePythonCache, result);
                }
                result.AddonsInstalled++;
                Log("Installed " + addon.DisplayName + " to " + targetFolder);
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.AddonsSkipped++;
                Log("Failed to install " + addon.DisplayName + ": " + ex.Message);
                try
                {
                    if (Directory.Exists(targetFolder))
                    {
                        DeleteDirectoryRobust(targetFolder);
                    }
                }
                catch
                {
                }
            }
        }

        private static void CopyDirectory(string sourceFolder, string targetFolder, bool excludePythonCache, AddonInstallResult result)
        {
            foreach (var directory in Directory.EnumerateDirectories(sourceFolder, "*", SearchOption.AllDirectories))
            {
                if (excludePythonCache && ContainsPathSegment(directory, "__pycache__"))
                {
                    result.ItemsIgnored++;
                    continue;
                }
                var relative = GetRelativePath(sourceFolder, directory);
                Directory.CreateDirectory(Path.Combine(targetFolder, relative));
            }

            foreach (var file in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
            {
                if (ShouldIgnore(file, excludePythonCache))
                {
                    result.ItemsIgnored++;
                    continue;
                }
                var relative = GetRelativePath(sourceFolder, file);
                var targetFile = Path.Combine(targetFolder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(targetFile));
                File.Copy(file, targetFile, true);
                File.SetLastWriteTimeUtc(targetFile, File.GetLastWriteTimeUtc(file));
                result.FilesCopied++;
            }
        }

        private static void ExtractArchive(string archivePath, string targetFolder, AddonInstallResult result)
        {
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        continue;
                    }
                    var targetPath = Path.GetFullPath(Path.Combine(targetFolder, entry.FullName));
                    if (!IsSameOrChild(targetFolder, targetPath))
                    {
                        throw new InvalidOperationException("Archive contains an unsafe path: " + entry.FullName);
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                    entry.ExtractToFile(targetPath, true);
                    result.FilesCopied++;
                }
            }
        }

        private static void DeleteDirectoryRobust(string folder)
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            foreach (var directory in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(directory, FileAttributes.Normal);
            }
            File.SetAttributes(folder, FileAttributes.Normal);
            Directory.Delete(folder, true);
        }

        private static AddonManifest ReadArchiveManifest(string archivePath)
        {
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.Equals(entry.FullName.Replace('/', '\\'), "manifest.ini", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var stream = entry.Open())
                        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                        {
                            return ParseManifest(reader.ReadToEnd());
                        }
                    }
                }
            }
            throw new InvalidOperationException("manifest.ini was not found at the archive root.");
        }

        private static AddonManifest ReadManifestFile(string manifestPath)
        {
            return ParseManifest(File.ReadAllText(manifestPath, Encoding.UTF8));
        }

        private static AddonManifest ParseManifest(string text)
        {
            var manifest = new AddonManifest();
            using (var reader = new StringReader(text ?? string.Empty))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("["))
                    {
                        continue;
                    }
                    var equals = line.IndexOf('=');
                    if (equals <= 0)
                    {
                        continue;
                    }
                    var key = line.Substring(0, equals).Trim();
                    var value = TrimManifestValue(line.Substring(equals + 1));
                    if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase)) manifest.Name = value;
                    else if (string.Equals(key, "summary", StringComparison.OrdinalIgnoreCase)) manifest.Summary = value;
                    else if (string.Equals(key, "version", StringComparison.OrdinalIgnoreCase)) manifest.Version = value;
                    else if (string.Equals(key, "author", StringComparison.OrdinalIgnoreCase)) manifest.Author = value;
                    else if (string.Equals(key, "description", StringComparison.OrdinalIgnoreCase)) manifest.Description = value;
                }
            }
            if (string.IsNullOrWhiteSpace(manifest.Name))
            {
                throw new InvalidOperationException("manifest.ini does not contain a name field.");
            }
            return manifest;
        }

        private static string TrimManifestValue(string value)
        {
            value = (value ?? string.Empty).Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[value.Length - 1] == '"') || (value[0] == '\'' && value[value.Length - 1] == '\'')))
            {
                value = value.Substring(1, value.Length - 2);
            }
            return value;
        }

        private static bool IsTargetAvailable(string target)
        {
            var root = Path.GetPathRoot(target);
            if (!string.IsNullOrWhiteSpace(root) && !Directory.Exists(root))
            {
                return false;
            }
            var existing = target;
            while (!string.IsNullOrWhiteSpace(existing) && !Directory.Exists(existing))
            {
                existing = Path.GetDirectoryName(existing);
            }
            return !string.IsNullOrWhiteSpace(existing) && Directory.Exists(existing);
        }

        private static bool ShouldIgnore(string path, bool excludePythonCache)
        {
            if (!excludePythonCache)
            {
                return false;
            }
            var extension = Path.GetExtension(path);
            return ContainsPathSegment(path, "__pycache__")
                || string.Equals(extension, ".pyc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".pyo", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsPathSegment(string path, string segment)
        {
            var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var part in parts)
            {
                if (string.Equals(part, segment, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsSameOrChild(string parent, string possibleChild)
        {
            parent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            possibleChild = Path.GetFullPath(possibleChild).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return possibleChild.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRelativePath(string root, string path)
        {
            var rootUri = new Uri(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var pathUri = new Uri(path);
            var relative = Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString());
            return relative.Replace('/', Path.DirectorySeparatorChar);
        }

        private static string JsonEscape(string value)
        {
            if (value == null)
            {
                return "";
            }
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private static void Log(string message)
        {
            var handler = Message;
            if (handler != null)
            {
                handler(DateTime.Now.ToString("HH:mm:ss") + " " + message);
            }
        }
    }
}
