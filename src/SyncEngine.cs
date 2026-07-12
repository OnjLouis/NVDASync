using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace NvdaAddonSync
{
    internal enum SyncComponentKind
    {
        Directory,
        File,
        OtherConfigFiles,
        OtherConfigFolders
    }

    internal sealed class SyncComponent
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string RelativePath { get; private set; }
        public SyncComponentKind Kind { get; private set; }

        private SyncComponent(string id, string name, string relativePath, SyncComponentKind kind)
        {
            Id = id;
            Name = name;
            RelativePath = relativePath;
            Kind = kind;
        }

        public static readonly SyncComponent Addons = new SyncComponent("addons", "Add-ons", "addons", SyncComponentKind.Directory);
        public static readonly SyncComponent InputGestures = new SyncComponent("inputGestures", "Input gestures", "gestures.ini", SyncComponentKind.File);
        public static readonly SyncComponent NvdaIni = new SyncComponent("nvdaIni", "NVDA.ini", "nvda.ini", SyncComponentKind.File);
        public static readonly SyncComponent SpeechDictionaries = new SyncComponent("speechDictionaries", "Speech dictionaries", "speechDicts", SyncComponentKind.Directory);
        public static readonly SyncComponent ConfigProfiles = new SyncComponent("configProfiles", "Configuration profiles", "profiles", SyncComponentKind.Directory);
        public static readonly SyncComponent OtherConfigFiles = new SyncComponent("otherConfigFiles", "Other root configuration files", "", SyncComponentKind.OtherConfigFiles);
        public static readonly SyncComponent OtherConfigFolders = new SyncComponent("otherConfigFolders", "Other root configuration folders", "", SyncComponentKind.OtherConfigFolders);

        public static List<SyncComponent> All
        {
            get
            {
                return new List<SyncComponent>
                {
                    Addons,
                    InputGestures,
                    NvdaIni,
                    SpeechDictionaries,
                    ConfigProfiles,
                    OtherConfigFiles,
                    OtherConfigFolders
                };
            }
        }

        public static List<SyncComponent> FromSettings(AppSettings settings)
        {
            var components = new List<SyncComponent>();
            if (settings.SyncAddons) components.Add(Addons);
            if (settings.SyncInputGestures) components.Add(InputGestures);
            if (settings.SyncNvdaIni) components.Add(NvdaIni);
            if (settings.SyncSpeechDictionaries) components.Add(SpeechDictionaries);
            if (settings.SyncConfigProfiles) components.Add(ConfigProfiles);
            if (settings.SyncOtherConfigFiles) components.Add(OtherConfigFiles);
            if (settings.SyncOtherConfigFolders) components.Add(OtherConfigFolders);
            if (components.Count == 0)
            {
                components.Add(Addons);
            }
            return components;
        }

        public static SyncComponent FromId(string id)
        {
            foreach (var component in All)
            {
                if (string.Equals(component.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return component;
                }
            }
            throw new InvalidOperationException("Unknown component: " + id);
        }

        public override string ToString()
        {
            return Name;
        }
    }

    internal sealed class SyncOptions
    {
        public bool DeleteStaleItems { get; set; }
        public bool ExcludePythonCache { get; set; }
        public bool ExcludeLogFiles { get; set; }
        public List<SyncComponent> Components { get; set; }
        public string AddonMode { get; set; }
        public CancellationToken CancellationToken { get; set; }
    }

    internal sealed class SyncResult
    {
        public int ComponentsSynced { get; set; }
        public int FilesCopied { get; set; }
        public int FilesSkipped { get; set; }
        public int ItemsDeleted { get; set; }
        public int ItemsIgnored { get; set; }
        public int UnavailableTargets { get; set; }
        public int Errors { get; set; }
    }

    internal sealed class SyncEngine
    {
        private static readonly HashSet<string> ReservedRootConfigFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "gestures.ini",
            "inputGestures.ini",
            "nvda.ini",
            "profileTriggers.ini"
        };

        private static readonly HashSet<string> ReservedRootConfigDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "addons",
            "profiles",
            "speechDicts"
        };

        public event Action<string> Message;

        public SyncResult Sync(string primaryFolder, IEnumerable<string> secondaryFolders, SyncOptions options)
        {
            if (options == null)
            {
                options = new SyncOptions();
            }
            if (options.Components == null || options.Components.Count == 0)
            {
                options.Components = new List<SyncComponent> { SyncComponent.Addons };
            }
            options.AddonMode = AddonSyncMode.Normalize(options.AddonMode);

            var result = new SyncResult();
            primaryFolder = ResolveNvdaConfigDirectory(primaryFolder, "Primary folder", true);

            foreach (var secondary in secondaryFolders)
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(secondary))
                {
                    continue;
                }
                try
                {
                    var target = ResolveNvdaConfigDirectory(secondary, "Secondary folder", false);
                    ValidateFolderPair(primaryFolder, target);
                    if (!IsTargetAvailable(target))
                    {
                        result.UnavailableTargets++;
                        Log("Skipped unavailable secondary folder " + target);
                        continue;
                    }
                    Directory.CreateDirectory(target);
                    Log("Syncing to " + target);
                    foreach (var component in options.Components)
                    {
                        options.CancellationToken.ThrowIfCancellationRequested();
                        SyncComponentToTarget(primaryFolder, target, component, result, options);
                    }
                }
                catch (DirectoryNotFoundException ex)
                {
                    result.UnavailableTargets++;
                    Log("Skipped unavailable secondary folder: " + ex.Message);
                }
                catch (OperationCanceledException)
                {
                    Log("Sync cancelled.");
                    break;
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    Log("Error: " + ex.Message);
                }
            }

            return result;
        }

        private void SyncComponentToTarget(string primaryFolder, string targetFolder, SyncComponent component, SyncResult result, SyncOptions options)
        {
            if (component.Kind == SyncComponentKind.OtherConfigFiles)
            {
                SyncOtherConfigFiles(primaryFolder, targetFolder, result, options);
                return;
            }
            if (component.Kind == SyncComponentKind.OtherConfigFolders)
            {
                SyncOtherConfigFolders(primaryFolder, targetFolder, result, options);
                return;
            }
            if (component == SyncComponent.Addons && AddonSyncMode.Normalize(options.AddonMode) == AddonSyncMode.ExistingOnly)
            {
                SyncExistingAddons(primaryFolder, targetFolder, result, options);
                return;
            }

            var source = Path.Combine(primaryFolder, component.RelativePath);
            var target = Path.Combine(targetFolder, component.RelativePath);
            if (component.Kind == SyncComponentKind.File)
            {
                if (!File.Exists(source))
                {
                    Log("Skipped missing " + component.Name + ".");
                    return;
                }
                CopyOneFile(primaryFolder, source, target, result, options.CancellationToken);
                result.ComponentsSynced++;
                return;
            }

            if (!Directory.Exists(source))
            {
                Log("Skipped missing " + component.Name + ".");
                return;
            }
            Directory.CreateDirectory(target);
            Log("Syncing " + component.Name + ".");
            CopyChangedFiles(source, target, result, options);
            if (options.DeleteStaleItems)
            {
                DeleteStaleEntries(source, target, result, options);
            }
            if (component == SyncComponent.ConfigProfiles)
            {
                SyncProfileTriggers(primaryFolder, targetFolder, result, options);
            }
            result.ComponentsSynced++;
        }

        private void SyncProfileTriggers(string primaryFolder, string targetFolder, SyncResult result, SyncOptions options)
        {
            var sourceFile = Path.Combine(primaryFolder, "profileTriggers.ini");
            var targetFile = Path.Combine(targetFolder, "profileTriggers.ini");
            if (File.Exists(sourceFile))
            {
                CopyOneFile(primaryFolder, sourceFile, targetFile, result, options.CancellationToken);
                return;
            }
            if (options.DeleteStaleItems && File.Exists(targetFile))
            {
                DeleteFile(targetFolder, targetFile, result);
            }
        }


        private void SyncExistingAddons(string primaryFolder, string targetFolder, SyncResult result, SyncOptions options)
        {
            var sourceAddons = Path.Combine(primaryFolder, SyncComponent.Addons.RelativePath);
            var targetAddons = Path.Combine(targetFolder, SyncComponent.Addons.RelativePath);
            if (!Directory.Exists(sourceAddons))
            {
                Log("Skipped missing Add-ons.");
                return;
            }
            if (!Directory.Exists(targetAddons))
            {
                Log("Skipped existing-only add-on sync because the target has no add-ons folder.");
                result.ComponentsSynced++;
                return;
            }

            Log("Updating existing add-ons only.");
            foreach (var targetAddon in Directory.EnumerateDirectories(targetAddons, "*", SearchOption.TopDirectoryOnly))
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                var addonName = Path.GetFileName(targetAddon);
                var sourceAddon = Path.Combine(sourceAddons, addonName);
                if (!Directory.Exists(sourceAddon))
                {
                    result.ItemsIgnored++;
                    Log("Left target-only add-on unchanged: " + addonName);
                    continue;
                }
                CopyChangedFiles(sourceAddon, targetAddon, result, options);
                if (options.DeleteStaleItems)
                {
                    DeleteStaleEntries(sourceAddon, targetAddon, result, options);
                }
            }
            result.ComponentsSynced++;
        }

        private void SyncOtherConfigFiles(string primaryFolder, string targetFolder, SyncResult result, SyncOptions options)
        {
            Log("Syncing other root configuration files.");
            foreach (var sourceFile in Directory.EnumerateFiles(primaryFolder, "*", SearchOption.TopDirectoryOnly))
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(sourceFile);
                if (ReservedRootConfigFiles.Contains(name))
                {
                    result.ItemsIgnored++;
                    continue;
                }
                CopyOneFile(primaryFolder, sourceFile, Path.Combine(targetFolder, name), result, options.CancellationToken);
            }
            if (options.DeleteStaleItems)
            {
                foreach (var targetFile in Directory.EnumerateFiles(targetFolder, "*", SearchOption.TopDirectoryOnly))
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(targetFile);
                    if (ReservedRootConfigFiles.Contains(name))
                    {
                        continue;
                    }
                    var sourceFile = Path.Combine(primaryFolder, name);
                    if (!File.Exists(sourceFile))
                    {
                        DeleteFile(targetFolder, targetFile, result);
                    }
                }
            }
            result.ComponentsSynced++;
        }

        private void SyncOtherConfigFolders(string primaryFolder, string targetFolder, SyncResult result, SyncOptions options)
        {
            Log("Syncing other root configuration folders.");
            foreach (var sourceDirectory in Directory.EnumerateDirectories(primaryFolder, "*", SearchOption.TopDirectoryOnly))
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(sourceDirectory);
                if (ReservedRootConfigDirectories.Contains(name))
                {
                    result.ItemsIgnored++;
                    continue;
                }
                var targetDirectory = Path.Combine(targetFolder, name);
                Directory.CreateDirectory(targetDirectory);
                CopyChangedFiles(sourceDirectory, targetDirectory, result, options);
                if (options.DeleteStaleItems)
                {
                    DeleteStaleEntries(sourceDirectory, targetDirectory, result, options);
                }
            }
            if (options.DeleteStaleItems)
            {
                foreach (var targetDirectory in Directory.EnumerateDirectories(targetFolder, "*", SearchOption.TopDirectoryOnly))
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(targetDirectory);
                    if (ReservedRootConfigDirectories.Contains(name))
                    {
                        continue;
                    }
                    var sourceDirectory = Path.Combine(primaryFolder, name);
                    if (!Directory.Exists(sourceDirectory))
                    {
                        DeleteDirectory(targetFolder, targetDirectory, result, true);
                    }
                }
            }
            result.ComponentsSynced++;
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

        public static string NormalizeDirectory(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException(label + " is not set.");
            }
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim())).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static string ResolveNvdaConfigDirectory(string path, string label, bool mustExist)
        {
            var normalized = NormalizeDirectory(path, label);
            if (IsKnownConfigChildDirectory(normalized))
            {
                var parent = Path.GetDirectoryName(normalized);
                if (mustExist && !Directory.Exists(parent))
                {
                    throw new DirectoryNotFoundException(label + " does not exist: " + parent);
                }
                return parent;
            }

            if (string.Equals(Path.GetFileName(normalized), "userConfig", StringComparison.OrdinalIgnoreCase)
                || LooksLikeConfigDirectory(normalized))
            {
                if (mustExist && !Directory.Exists(normalized))
                {
                    throw new DirectoryNotFoundException(label + " does not exist: " + normalized);
                }
                return normalized;
            }

            var userConfig = Path.Combine(normalized, "userConfig");
            if (Directory.Exists(userConfig) || File.Exists(Path.Combine(normalized, "nvda.exe")))
            {
                if (!mustExist || Directory.Exists(userConfig))
                {
                    return userConfig;
                }
            }

            var addons = Path.Combine(normalized, "addons");
            if (Directory.Exists(addons))
            {
                return normalized;
            }

            if (!Directory.Exists(normalized))
            {
                if (mustExist)
                {
                    throw new DirectoryNotFoundException(label + " does not exist: " + normalized);
                }
                return GuessConfigDirectoryForUnavailablePath(normalized);
            }

            var discovered = FindConfigDirectories(normalized);
            if (discovered.Count == 1)
            {
                return discovered[0];
            }
            if (discovered.Count > 1)
            {
                throw new InvalidOperationException(label + " contains more than one NVDA configuration folder. Choose the intended folder directly.");
            }

            if (mustExist)
            {
                throw new DirectoryNotFoundException(label + " is not an NVDA folder and no NVDA configuration folder was found under it: " + normalized);
            }
            return userConfig;
        }

        public static string ResolveAddonsDirectory(string path, string label, bool mustExist)
        {
            return Path.Combine(ResolveNvdaConfigDirectory(path, label, mustExist), "addons");
        }

        private static string GuessConfigDirectoryForUnavailablePath(string path)
        {
            if (IsKnownConfigChildDirectory(path))
            {
                return Path.GetDirectoryName(path);
            }
            if (string.Equals(Path.GetFileName(path), "userConfig", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
            return Path.Combine(path, "userConfig");
        }

        private static bool IsKnownConfigChildDirectory(string path)
        {
            var name = Path.GetFileName(path);
            return ReservedRootConfigDirectories.Contains(name);
        }

        private static bool LooksLikeConfigDirectory(string path)
        {
            return File.Exists(Path.Combine(path, "nvda.ini"))
                || File.Exists(Path.Combine(path, "gestures.ini"))
                || File.Exists(Path.Combine(path, "inputGestures.ini"))
                || Directory.Exists(Path.Combine(path, "addons"))
                || Directory.Exists(Path.Combine(path, "profiles"))
                || Directory.Exists(Path.Combine(path, "speechDicts"));
        }

        private static List<string> FindConfigDirectories(string root)
        {
            var matches = new List<string>();
            try
            {
                var userConfig = Path.Combine(root, "userConfig");
                if (LooksLikeConfigDirectory(userConfig))
                {
                    matches.Add(userConfig);
                }
                if (LooksLikeConfigDirectory(root))
                {
                    matches.Add(root);
                }
                foreach (var directory in Directory.EnumerateDirectories(root, "addons", SearchOption.AllDirectories))
                {
                    var parent = Path.GetDirectoryName(directory);
                    if (!string.IsNullOrWhiteSpace(parent) && !matches.Contains(parent))
                    {
                        matches.Add(parent);
                    }
                    if (matches.Count > 1)
                    {
                        break;
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
            return matches;
        }

        private static void ValidateFolderPair(string primaryFolder, string secondaryFolder)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(primaryFolder, secondaryFolder))
            {
                throw new InvalidOperationException("Primary and secondary folders cannot be the same folder.");
            }
            if (IsSameOrChild(primaryFolder, secondaryFolder) || IsSameOrChild(secondaryFolder, primaryFolder))
            {
                throw new InvalidOperationException("Primary and secondary folders cannot contain each other.");
            }
        }

        private static bool IsSameOrChild(string parent, string possibleChild)
        {
            parent = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            possibleChild = possibleChild.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return possibleChild.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
        }

        private void CopyChangedFiles(string primaryFolder, string targetFolder, SyncResult result, SyncOptions options)
        {
            foreach (var sourceFile in Directory.EnumerateFiles(primaryFolder, "*", SearchOption.AllDirectories))
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                if (ShouldIgnore(sourceFile, options))
                {
                    result.ItemsIgnored++;
                    continue;
                }
                var relativePath = GetRelativePath(primaryFolder, sourceFile);
                var targetFile = Path.Combine(targetFolder, relativePath);
                CopyOneFile(primaryFolder, sourceFile, targetFile, result, options.CancellationToken);
            }
        }

        private void CopyOneFile(string primaryFolder, string sourceFile, string targetFile, SyncResult result, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = GetRelativePath(primaryFolder, sourceFile);
            try
            {
                if (!NeedsCopy(sourceFile, targetFile))
                {
                    result.FilesSkipped++;
                    return;
                }
                var targetDirectory = Path.GetDirectoryName(targetFile);
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }
                CopyFileAtomically(sourceFile, targetFile);
                result.FilesCopied++;
                Log("Copied " + relativePath);
            }
            catch (Exception ex)
            {
                result.Errors++;
                Log("Failed to copy " + relativePath + ": " + ex.Message);
            }
        }

        private void DeleteStaleEntries(string primaryFolder, string targetFolder, SyncResult result, SyncOptions options)
        {
            foreach (var targetFile in Directory.EnumerateFiles(targetFolder, "*", SearchOption.AllDirectories))
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                if (ShouldIgnore(targetFile, options))
                {
                    DeleteFile(targetFolder, targetFile, result);
                    continue;
                }
                var relativePath = GetRelativePath(targetFolder, targetFile);
                var sourceFile = Path.Combine(primaryFolder, relativePath);
                if (File.Exists(sourceFile))
                {
                    continue;
                }
                DeleteFile(targetFolder, targetFile, result);
            }

            var directories = new List<string>(Directory.EnumerateDirectories(targetFolder, "*", SearchOption.AllDirectories));
            directories.Sort((a, b) => b.Length.CompareTo(a.Length));
            foreach (var targetDirectory in directories)
            {
                options.CancellationToken.ThrowIfCancellationRequested();
                if (ShouldIgnoreDirectory(targetDirectory, options))
                {
                    DeleteDirectory(targetFolder, targetDirectory, result, true);
                    continue;
                }
                var relativePath = GetRelativePath(targetFolder, targetDirectory);
                var sourceDirectory = Path.Combine(primaryFolder, relativePath);
                if (Directory.Exists(sourceDirectory))
                {
                    continue;
                }
                try
                {
                    if (Directory.GetFileSystemEntries(targetDirectory).Length == 0)
                    {
                        DeleteDirectory(targetFolder, targetDirectory, result, false);
                    }
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    Log("Failed to delete folder " + relativePath + ": " + ex.Message);
                }
            }
        }

        private static bool ShouldIgnore(string path, SyncOptions options)
        {
            var name = Path.GetFileName(path);
            if (IsTransientBrowserProfileFile(path))
            {
                return true;
            }
            if (options.ExcludeLogFiles && string.Equals(Path.GetExtension(path), ".log", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (options.ExcludePythonCache)
            {
                if (string.Equals(name, "__pycache__", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                if (string.Equals(Path.GetExtension(path), ".pyc", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetExtension(path), ".pyo", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                return ContainsPathSegment(path, "__pycache__");
            }
            return false;
        }

        private static bool ShouldIgnoreDirectory(string path, SyncOptions options)
        {
            return (options.ExcludePythonCache && ContainsPathSegment(path, "__pycache__"))
                || IsTransientBrowserProfileDirectory(path);
        }

        private static bool IsTransientBrowserProfileFile(string path)
        {
            if (!LooksLikeBrowserProfilePath(path))
            {
                return false;
            }

            if (ContainsAnyPathSegment(path, new[]
            {
                "Cache",
                "Cache_Data",
                "Code Cache",
                "DawnGraphiteCache",
                "DawnWebGPUCache",
                "GPUCache",
                "GrShaderCache",
                "ShaderCache",
                "Session Storage",
                "Sessions",
                "segmentation_platform",
                "Service Worker",
                "Site Characteristics Database"
            }))
            {
                return true;
            }

            var name = Path.GetFileName(path);
            if (string.Equals(name, "History", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Top Sites", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Cookies", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Safe Browsing Cookies", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Reporting and NEL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "QuotaManager", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("-journal", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetExtension(name), ".tmp", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static bool IsTransientBrowserProfileDirectory(string path)
        {
            if (!LooksLikeBrowserProfilePath(path))
            {
                return false;
            }
            return ContainsAnyPathSegment(path, new[]
            {
                "Cache",
                "Cache_Data",
                "Code Cache",
                "DawnGraphiteCache",
                "DawnWebGPUCache",
                "GPUCache",
                "GrShaderCache",
                "ShaderCache",
                "Session Storage",
                "Sessions",
                "segmentation_platform",
                "Service Worker",
                "Site Characteristics Database"
            });
        }

        private static bool LooksLikeBrowserProfilePath(string path)
        {
            return ContainsAnyPathSegment(path, new[]
            {
                "chromeProfiles",
                "ChromeProfile",
                "Default",
                "Network",
                "WebStorage",
                "Safe Browsing Network",
                "GPUPersistentCache",
                "Safe Browsing Cookies"
            });
        }

        private static bool ContainsAnyPathSegment(string path, IEnumerable<string> segments)
        {
            foreach (var segment in segments)
            {
                if (ContainsPathSegment(path, segment))
                {
                    return true;
                }
            }
            return false;
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

        private void DeleteFile(string targetFolder, string targetFile, SyncResult result)
        {
            var relativePath = GetRelativePath(targetFolder, targetFile);
            try
            {
                File.SetAttributes(targetFile, FileAttributes.Normal);
                File.Delete(targetFile);
                result.ItemsDeleted++;
                Log("Deleted stale file " + relativePath);
            }
            catch (Exception ex)
            {
                result.Errors++;
                Log("Failed to delete " + relativePath + ": " + ex.Message);
            }
        }

        private void DeleteDirectory(string targetFolder, string targetDirectory, SyncResult result, bool recursive)
        {
            var relativePath = GetRelativePath(targetFolder, targetDirectory);
            try
            {
                Directory.Delete(targetDirectory, recursive);
                result.ItemsDeleted++;
                Log("Deleted stale folder " + relativePath);
            }
            catch (Exception ex)
            {
                result.Errors++;
                Log("Failed to delete folder " + relativePath + ": " + ex.Message);
            }
        }

        private static bool NeedsCopy(string sourceFile, string targetFile)
        {
            if (!File.Exists(targetFile))
            {
                return true;
            }
            var sourceInfo = new FileInfo(sourceFile);
            var targetInfo = new FileInfo(targetFile);
            if (sourceInfo.Length != targetInfo.Length)
            {
                return true;
            }
            var difference = sourceInfo.LastWriteTimeUtc - targetInfo.LastWriteTimeUtc;
            if (Math.Abs(difference.TotalSeconds) <= 2)
            {
                return false;
            }
            if (!FilesHaveSameContent(sourceFile, targetFile))
            {
                return true;
            }
            TryNormalizeTargetMetadata(sourceFile, targetFile);
            return false;
        }

        private static void CopyFileAtomically(string sourceFile, string targetFile)
        {
            var tempFile = targetFile + ".nvdaSync.tmp";
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
            File.Copy(sourceFile, tempFile, true);
            File.SetLastWriteTimeUtc(tempFile, File.GetLastWriteTimeUtc(sourceFile));
            File.SetAttributes(tempFile, File.GetAttributes(sourceFile));
            if (File.Exists(targetFile))
            {
                File.SetAttributes(targetFile, FileAttributes.Normal);
                File.Replace(tempFile, targetFile, null);
            }
            else
            {
                File.Move(tempFile, targetFile);
            }
            TryNormalizeTargetMetadata(sourceFile, targetFile);
        }

        private static bool FilesHaveSameContent(string sourceFile, string targetFile)
        {
            const int BufferSize = 1024 * 64;
            var sourceBuffer = new byte[BufferSize];
            var targetBuffer = new byte[BufferSize];
            using (var source = File.OpenRead(sourceFile))
            using (var target = File.OpenRead(targetFile))
            {
                while (true)
                {
                    var sourceRead = source.Read(sourceBuffer, 0, sourceBuffer.Length);
                    var targetRead = target.Read(targetBuffer, 0, targetBuffer.Length);
                    if (sourceRead != targetRead)
                    {
                        return false;
                    }
                    if (sourceRead == 0)
                    {
                        return true;
                    }
                    for (var index = 0; index < sourceRead; index++)
                    {
                        if (sourceBuffer[index] != targetBuffer[index])
                        {
                            return false;
                        }
                    }
                }
            }
        }

        private static void TryNormalizeTargetMetadata(string sourceFile, string targetFile)
        {
            try
            {
                File.SetLastWriteTimeUtc(targetFile, File.GetLastWriteTimeUtc(sourceFile));
                File.SetAttributes(targetFile, File.GetAttributes(sourceFile));
            }
            catch
            {
            }
        }

        private static string GetRelativePath(string root, string path)
        {
            var rootUri = new Uri(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var pathUri = new Uri(path);
            var relative = Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString());
            return relative.Replace('/', Path.DirectorySeparatorChar);
        }

        private void Log(string message)
        {
            var handler = Message;
            if (handler != null)
            {
                handler(DateTime.Now.ToString("HH:mm:ss") + " " + message);
            }
        }
    }
}
