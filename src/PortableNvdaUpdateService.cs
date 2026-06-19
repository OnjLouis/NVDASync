using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace NvdaAddonSync
{
    internal sealed class PortableNvdaUpdateResult
    {
        public int TargetsUpdated { get; set; }
        public int BackupsCreated { get; set; }
        public int FilesAdded { get; set; }
        public int FilesReplaced { get; set; }
        public int FilesSkipped { get; set; }
        public int FilesDeleted { get; set; }
        public int DirectoriesDeleted { get; set; }
        public int UnavailableTargets { get; set; }
        public int Errors { get; set; }
    }

    internal static class PortableNvdaUpdateService
    {
        private static readonly HashSet<string> ProtectedRootFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "userConfig",
            "Settings",
            "Logs",
            "Backups"
        };

        public static event Action<string> Message;

        public static string DetectInstalledNvdaProgramFolder()
        {
            var candidates = new List<string>();
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFiles)) candidates.Add(Path.Combine(programFiles, "NVDA"));
            if (!string.IsNullOrWhiteSpace(programFilesX86)) candidates.Add(Path.Combine(programFilesX86, "NVDA"));

            foreach (var candidate in candidates)
            {
                if (LooksLikeNvdaProgramFolder(candidate))
                {
                    return candidate;
                }
            }
            throw new DirectoryNotFoundException("Could not find an installed NVDA program folder under Program Files.");
        }

        public static PortableNvdaUpdateResult UpdateInstalledToTarget(string secondaryFolder, CancellationToken cancellationToken)
        {
            return UpdateTarget(DetectInstalledNvdaProgramFolder(), secondaryFolder, true, cancellationToken);
        }

        public static PortableNvdaUpdateResult UpdateTarget(string sourceProgramFolder, string secondaryFolder, CancellationToken cancellationToken)
        {
            return UpdateTarget(sourceProgramFolder, secondaryFolder, true, cancellationToken);
        }

        public static PortableNvdaUpdateResult UpdateTarget(string sourceProgramFolder, string secondaryFolder, bool createBackup, CancellationToken cancellationToken)
        {
            var result = new PortableNvdaUpdateResult();
            try
            {
                sourceProgramFolder = NormalizeProgramSource(sourceProgramFolder);
                var targetRoot = ResolvePortableRoot(secondaryFolder, false);
                if (!IsTargetAvailable(targetRoot))
                {
                    result.UnavailableTargets++;
                    Log("Skipped unavailable portable NVDA target " + targetRoot);
                    return result;
                }
                Directory.CreateDirectory(targetRoot);
                if (!LooksLikePortableNvdaRoot(targetRoot))
                {
                    throw new InvalidOperationException("Portable NVDA target must contain nvda.exe and userConfig: " + targetRoot);
                }
                if (IsProgramFilesPath(targetRoot))
                {
                    throw new InvalidOperationException("Program-file sync only updates portable copies, not installed NVDA under Program Files.");
                }
                if (IsSameOrChild(sourceProgramFolder, targetRoot) || IsSameOrChild(targetRoot, sourceProgramFolder))
                {
                    throw new InvalidOperationException("NVDA program source and portable target cannot contain each other.");
                }
                if (IsNvdaRunningFrom(targetRoot))
                {
                    throw new InvalidOperationException("Close the portable NVDA copy before updating its program files: " + targetRoot);
                }

                if (!HasProgramChanges(sourceProgramFolder, targetRoot, cancellationToken))
                {
                    Log("Portable NVDA program files already up to date in " + targetRoot);
                    return result;
                }

                Log("Updating portable NVDA program files in " + targetRoot);
                if (createBackup)
                {
                    var backupPath = CreateProgramBackup(targetRoot, cancellationToken);
                    result.BackupsCreated++;
                    Log("Created program-file backup " + backupPath);
                }
                else
                {
                    Log("Program-file backup skipped by setting.");
                }

                CopyProgramFiles(sourceProgramFolder, targetRoot, result, cancellationToken);
                DeleteStaleProgramFiles(sourceProgramFolder, targetRoot, result, cancellationToken);
                DeleteEmptyProgramDirectories(sourceProgramFolder, targetRoot, result, cancellationToken);
                result.TargetsUpdated++;
            }
            catch (DirectoryNotFoundException ex)
            {
                result.UnavailableTargets++;
                Log("Skipped unavailable portable NVDA target: " + ex.Message);
            }
            catch (OperationCanceledException)
            {
                Log("Portable NVDA program update cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                result.Errors++;
                Log("Portable NVDA program update failed: " + ex.Message);
            }
            return result;
        }

        public static string ResolvePortableRoot(string path, bool mustExist)
        {
            var normalized = SyncEngine.NormalizeDirectory(path, "Portable NVDA target");
            if (LooksLikePortableNvdaRoot(normalized))
            {
                return normalized;
            }

            var config = SyncEngine.ResolveNvdaConfigDirectory(normalized, "Portable NVDA target", mustExist);
            if (string.Equals(Path.GetFileName(config), "userConfig", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Path.GetDirectoryName(config);
                if (!string.IsNullOrWhiteSpace(parent) && (LooksLikePortableNvdaRoot(parent) || !mustExist))
                {
                    return parent;
                }
            }

            if (mustExist)
            {
                throw new DirectoryNotFoundException("Could not resolve a portable NVDA root from: " + path);
            }
            return normalized;
        }

        private static string NormalizeProgramSource(string sourceProgramFolder)
        {
            sourceProgramFolder = SyncEngine.NormalizeDirectory(sourceProgramFolder, "Installed NVDA program folder");
            if (!LooksLikeNvdaProgramFolder(sourceProgramFolder))
            {
                throw new DirectoryNotFoundException("Installed NVDA program folder does not contain nvda.exe: " + sourceProgramFolder);
            }
            return sourceProgramFolder;
        }

        private static bool LooksLikeNvdaProgramFolder(string folder)
        {
            return !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, "nvda.exe"));
        }

        private static bool LooksLikePortableNvdaRoot(string folder)
        {
            return LooksLikeNvdaProgramFolder(folder) && Directory.Exists(Path.Combine(folder, "userConfig"));
        }


        private static bool HasProgramChanges(string sourceRoot, string targetRoot, CancellationToken cancellationToken)
        {
            foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsProtectedRootPath(sourceRoot, sourceFile))
                {
                    continue;
                }
                var relative = GetRelativePath(sourceRoot, sourceFile);
                if (NeedsCopy(sourceFile, Path.Combine(targetRoot, relative)))
                {
                    return true;
                }
            }

            foreach (var targetFile in Directory.EnumerateFiles(targetRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsProtectedRootPath(targetRoot, targetFile))
                {
                    continue;
                }
                var relative = GetRelativePath(targetRoot, targetFile);
                if (!File.Exists(Path.Combine(sourceRoot, relative)))
                {
                    return true;
                }
            }
            return false;
        }
        private static string CreateProgramBackup(string targetRoot, CancellationToken cancellationToken)
        {
            var parent = Path.GetDirectoryName(targetRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(parent))
            {
                parent = targetRoot;
            }
            var folderName = Path.GetFileName(targetRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(folderName))
            {
                folderName = "nvda";
            }
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backupPath = Path.Combine(parent, folderName + stamp + ".zip");
            var attempt = 1;
            while (File.Exists(backupPath))
            {
                backupPath = Path.Combine(parent, folderName + stamp + "-" + attempt + ".zip");
                attempt++;
            }

            using (var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create))
            {
                var manifest = "NVDA Sync portable program-file backup" + Environment.NewLine +
                    "Target: " + targetRoot + Environment.NewLine +
                    "Created: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                    "Excluded: userConfig, Settings, Logs, Backups" + Environment.NewLine;
                var manifestEntry = archive.CreateEntry("NVDASync-backup-manifest.txt", CompressionLevel.Optimal);
                using (var writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false)))
                {
                    writer.Write(manifest);
                }

                foreach (var file in Directory.EnumerateFiles(targetRoot, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsProtectedRootPath(targetRoot, file))
                    {
                        continue;
                    }
                    var relative = GetRelativePath(targetRoot, file);
                    archive.CreateEntryFromFile(file, relative, CompressionLevel.Optimal);
                }
            }
            return backupPath;
        }

        private static void CopyProgramFiles(string sourceRoot, string targetRoot, PortableNvdaUpdateResult result, CancellationToken cancellationToken)
        {
            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsProtectedRootPath(sourceRoot, file))
                {
                    continue;
                }
                var relative = GetRelativePath(sourceRoot, file);
                var targetFile = Path.Combine(targetRoot, relative);
                var existed = File.Exists(targetFile);
                if (existed && !NeedsCopy(file, targetFile))
                {
                    result.FilesSkipped++;
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(targetFile));
                var tempPath = targetFile + ".nvda-sync.tmp";
                File.Copy(file, tempPath, true);
                if (File.Exists(targetFile))
                {
                    File.SetAttributes(targetFile, FileAttributes.Normal);
                    File.Delete(targetFile);
                }
                File.Move(tempPath, targetFile);
                File.SetLastWriteTimeUtc(targetFile, File.GetLastWriteTimeUtc(file));
                if (existed)
                {
                    result.FilesReplaced++;
                    Log("Replaced program file " + relative);
                }
                else
                {
                    result.FilesAdded++;
                    Log("Added program file " + relative);
                }
            }
        }

        private static void DeleteStaleProgramFiles(string sourceRoot, string targetRoot, PortableNvdaUpdateResult result, CancellationToken cancellationToken)
        {
            foreach (var targetFile in Directory.EnumerateFiles(targetRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsProtectedRootPath(targetRoot, targetFile))
                {
                    continue;
                }
                var relative = GetRelativePath(targetRoot, targetFile);
                var sourceFile = Path.Combine(sourceRoot, relative);
                if (File.Exists(sourceFile))
                {
                    continue;
                }
                try
                {
                    File.SetAttributes(targetFile, FileAttributes.Normal);
                    File.Delete(targetFile);
                    result.FilesDeleted++;
                    Log("Deleted stale program file " + relative);
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    Log("Failed to delete stale program file " + relative + ": " + ex.Message);
                }
            }
        }

        private static void DeleteEmptyProgramDirectories(string sourceRoot, string targetRoot, PortableNvdaUpdateResult result, CancellationToken cancellationToken)
        {
            var directories = new List<string>(Directory.EnumerateDirectories(targetRoot, "*", SearchOption.AllDirectories));
            directories.Sort((a, b) => b.Length.CompareTo(a.Length));
            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsProtectedRootPath(targetRoot, directory))
                {
                    continue;
                }
                var relative = GetRelativePath(targetRoot, directory);
                if (Directory.Exists(Path.Combine(sourceRoot, relative)))
                {
                    continue;
                }
                try
                {
                    if (Directory.GetFileSystemEntries(directory).Length == 0)
                    {
                        Directory.Delete(directory);
                        result.DirectoriesDeleted++;
                    }
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    Log("Failed to delete stale program folder " + relative + ": " + ex.Message);
                }
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
            return !HashEquals(sourceFile, targetFile);
        }

        private static bool HashEquals(string left, string right)
        {
            using (var sha = SHA256.Create())
            using (var leftStream = File.OpenRead(left))
            using (var rightStream = File.OpenRead(right))
            {
                var leftHash = sha.ComputeHash(leftStream);
                var rightHash = sha.ComputeHash(rightStream);
                if (leftHash.Length != rightHash.Length)
                {
                    return false;
                }
                for (var i = 0; i < leftHash.Length; i++)
                {
                    if (leftHash[i] != rightHash[i])
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        private static bool IsProtectedRootPath(string root, string path)
        {
            var relative = GetRelativePath(root, path);
            var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            return ProtectedRootFolders.Contains(first);
        }

        private static bool IsTargetAvailable(string target)
        {
            var root = Path.GetPathRoot(target);
            if (!string.IsNullOrWhiteSpace(root) && !Directory.Exists(root))
            {
                return false;
            }
            return Directory.Exists(target);
        }

        private static bool IsProgramFilesPath(string path)
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            return IsSameOrChild(programFiles, path) || IsSameOrChild(programFilesX86, path);
        }

        private static bool IsNvdaRunningFrom(string targetRoot)
        {
            foreach (var process in Process.GetProcessesByName("nvda"))
            {
                try
                {
                    var fileName = process.MainModule.FileName;
                    if (!string.IsNullOrWhiteSpace(fileName) && IsSameOrChild(targetRoot, fileName))
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }
            return false;
        }

        private static bool IsSameOrChild(string parent, string possibleChild)
        {
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(possibleChild))
            {
                return false;
            }
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
