using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace NvdaAddonSync
{
    internal static partial class Program
    {
        private static void ApplyUpdateFromCommandLine(string[] args)
        {
            try
            {
                string zipUrl;
                string targetDir;
                string exePath;
                string tempBase;
                string pidText;
                var noRestart = args.Any(arg => string.Equals(arg, "--update-no-restart", StringComparison.OrdinalIgnoreCase));
                TryGetOptionValue(args, "--update-url", out zipUrl);
                TryGetOptionValue(args, "--update-target", out targetDir);
                TryGetOptionValue(args, "--update-exe", out exePath);
                TryGetOptionValue(args, "--update-temp", out tempBase);
                TryGetOptionValue(args, "--update-wait-pid", out pidText);

                if (string.IsNullOrWhiteSpace(zipUrl) || string.IsNullOrWhiteSpace(targetDir) || string.IsNullOrWhiteSpace(exePath))
                {
                    throw new InvalidOperationException("The updater was not given enough information to install the update.");
                }

                WriteUpdateHistory(targetDir, "Update command received.");
                int processId;
                if (int.TryParse(pidText, out processId) && processId > 0)
                {
                    WriteUpdateHistory(targetDir, "Waiting for NVDA Sync process " + processId + " to exit.");
                    WaitForProcessExit(processId);
                }

                ApplyUpdate(zipUrl, targetDir, exePath, string.IsNullOrWhiteSpace(tempBase) ? Path.GetTempPath() : tempBase, noRestart);
            }
            catch (Exception ex)
            {
                WriteUpdateHistory(args, "ERROR: " + ex.Message);
                WriteUpdaterLog(args, ex);
                MessageBox.Show("NVDA Sync update failed:" + Environment.NewLine + Environment.NewLine + ex.Message, "NVDA Sync updater", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void ApplyUpdate(string zipUrl, string targetDir, string exePath, string tempBase, bool noRestart)
        {
            Directory.CreateDirectory(tempBase);
            var root = Path.Combine(tempBase, "NVDASyncUpdate_" + Guid.NewGuid().ToString("N"));
            var zip = Path.Combine(root, "update.zip");
            var stage = Path.Combine(root, "stage");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(stage);

            try
            {
                WriteUpdateHistory(targetDir, "Downloading update ZIP.");
                DownloadUpdateZip(zipUrl, zip);
                WriteUpdateHistory(targetDir, "Extracting update ZIP.");
                ZipFile.ExtractToDirectory(zip, stage);

                var source = FindUpdateSourceFolder(stage);
                if (string.IsNullOrWhiteSpace(source))
                {
                    throw new InvalidOperationException("The update ZIP does not contain NVDASync.exe.");
                }

                WriteUpdateHistory(targetDir, "Applying files.");
                Directory.CreateDirectory(targetDir);
                var backupRoot = Path.Combine(Path.Combine(targetDir, "Backups\\Updates"), DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                foreach (var item in Directory.GetFileSystemEntries(source))
                {
                    var name = Path.GetFileName(item);
                    if (IsPreservedRuntimeItem(name))
                    {
                        continue;
                    }
                    var destination = Path.Combine(targetDir, name);
                    if (Directory.Exists(item))
                    {
                        ReplaceDirectory(item, destination, backupRoot);
                    }
                    else
                    {
                        CopyFileWithRetry(item, destination);
                    }
                }
                TryDeleteFile(Path.Combine(targetDir, "README.md"));
                TryDeleteFile(Path.Combine(targetDir, "NvdaAddonSync.exe"));
                UpdateStartupRegistrationAfterUpdate(targetDir);
                RemoveEmptyDirectory(backupRoot);
                CleanupEmptyBackupFolders(targetDir);
            }
            finally
            {
                TryDeleteDirectory(root);
            }

            if (noRestart)
            {
                WriteUpdateHistory(targetDir, "Update applied. Restart skipped by command line.");
                return;
            }

            WriteUpdateHistory(targetDir, "Update applied. Restarting NVDA Sync.");
            TryRestartUpdatedApp(PreferredExecutablePath(targetDir, exePath), targetDir);
        }

        private static bool IsPreservedRuntimeItem(string name)
        {
            return string.Equals(name, "Settings", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Logs", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Backups", StringComparison.OrdinalIgnoreCase);
        }

        private static void ReplaceDirectory(string source, string destination, string backupRoot)
        {
            if (Directory.Exists(destination))
            {
                NewBackupZip(destination, backupRoot, "Previous-" + Path.GetFileName(destination));
                DeleteDirectoryWithRetry(destination);
            }
            CopyDirectory(source, destination);
        }

        private static void DownloadUpdateZip(string zipUrl, string destination)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "NVDA Sync updater";
                client.DownloadFile(zipUrl, destination);
            }
        }

        private static string FindUpdateSourceFolder(string stage)
        {
            var direct = Path.Combine(stage, "NVDASync.exe");
            if (File.Exists(direct))
            {
                return stage;
            }
            var candidates = Directory.GetFiles(stage, "NVDASync.exe", SearchOption.AllDirectories);
            if (candidates.Length == 0)
            {
                candidates = Directory.GetFiles(stage, "NvdaAddonSync.exe", SearchOption.AllDirectories);
            }
            return candidates.Length == 0 ? string.Empty : Path.GetDirectoryName(candidates[0]);
        }

        private static string PreferredExecutablePath(string targetDir, string fallbackExePath)
        {
            var preferred = Path.Combine(targetDir, "NVDASync.exe");
            return File.Exists(preferred) ? preferred : fallbackExePath;
        }

        private static void UpdateStartupRegistrationAfterUpdate(string targetDir)
        {
            try
            {
                var legacyExe = Path.Combine(targetDir, "NvdaAddonSync.exe");
                var preferredExe = PreferredExecutablePath(targetDir, legacyExe);
                if (StartupRegistration.IsEnabledForDirectory(targetDir))
                {
                    StartupRegistration.SetEnabledForPath(true, preferredExe);
                }
                NotificationIconRegistration.MigrateLegacyExecutablePath(legacyExe, preferredExe);
            }
            catch { }
        }

        private static void WaitForProcessExit(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    process.WaitForExit(30000);
                }
            }
            catch { }
        }

        private static void TryRestartUpdatedApp(string exePath, string targetDir)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                {
                    throw new FileNotFoundException("The updated NVDA Sync executable could not be found.", exePath ?? string.Empty);
                }
                Process.Start(new ProcessStartInfo { FileName = exePath, WorkingDirectory = Directory.Exists(targetDir) ? targetDir : Path.GetDirectoryName(exePath), UseShellExecute = true });
            }
            catch (Exception ex)
            {
                WriteUpdaterLog(null, ex);
                MessageBox.Show("NVDA Sync was updated, but it could not be restarted automatically." + Environment.NewLine + Environment.NewLine + "Please start NVDA Sync from its installed folder or shortcut." + Environment.NewLine + Environment.NewLine + ex.Message, "NVDA Sync updater", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(destination, RelativePath(source, directory)));
            }
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, RelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                CopyFileWithRetry(file, target);
            }
        }

        private static void CopyFileWithRetry(string source, string destination)
        {
            Exception last = null;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(source, destination, true);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(1000);
                }
            }
            throw new IOException("Could not replace " + destination + " after waiting.", last);
        }

        private static void DeleteDirectoryWithRetry(string path)
        {
            Exception last = null;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(1000);
                }
            }
            throw new IOException("Could not replace " + path + " after waiting.", last);
        }

        private static string RelativePath(string root, string path)
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ? fullPath.Substring(fullRoot.Length) : Path.GetFileName(path);
        }

        private static void NewBackupZip(string path, string backupRoot, string name)
        {
            if (!Directory.Exists(path)) return;
            Directory.CreateDirectory(backupRoot);
            var safeName = string.Join("_", (name ?? "Backup").Split(Path.GetInvalidFileNameChars()));
            var zipPath = Path.Combine(backupRoot, safeName + ".zip");
            if (File.Exists(zipPath))
            {
                zipPath = Path.Combine(backupRoot, safeName + "-" + Guid.NewGuid().ToString("N") + ".zip");
            }
            ZipFile.CreateFromDirectory(path, zipPath);
        }

        private static void RemoveEmptyDirectory(string folder)
        {
            try
            {
                if (Directory.Exists(folder) && !Directory.GetFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }
            catch { }
        }

        private static void CleanupEmptyBackupFolders(string targetDir)
        {
            try
            {
                var backups = Path.Combine(targetDir, "Backups");
                if (!Directory.Exists(backups)) return;
                foreach (var folder in Directory.GetDirectories(backups, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
                {
                    RemoveEmptyDirectory(folder);
                }
                RemoveEmptyDirectory(Path.Combine(backups, "Updates"));
                RemoveEmptyDirectory(backups);
            }
            catch { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch { }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch { }
        }

        private static void WriteUpdaterLog(string[] args, Exception exception)
        {
            try
            {
                string targetDir;
                if (args == null || !TryGetOptionValue(args, "--update-target", out targetDir) || string.IsNullOrWhiteSpace(targetDir))
                {
                    targetDir = AppDomain.CurrentDomain.BaseDirectory;
                }
                var logRoot = Path.Combine(targetDir, "Logs");
                Directory.CreateDirectory(logRoot);
                File.AppendAllText(Path.Combine(logRoot, "Updater.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " NVDA Sync updater error" + Environment.NewLine + (exception == null ? "(No exception object.)" : exception.ToString()) + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }

        private static void WriteUpdateHistory(string[] args, string message)
        {
            try
            {
                string targetDir;
                if (args == null || !TryGetOptionValue(args, "--update-target", out targetDir) || string.IsNullOrWhiteSpace(targetDir))
                {
                    targetDir = AppDomain.CurrentDomain.BaseDirectory;
                }
                WriteUpdateHistory(targetDir, message);
            }
            catch { }
        }

        private static void WriteUpdateHistory(string targetDir, string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(targetDir))
                {
                    targetDir = AppDomain.CurrentDomain.BaseDirectory;
                }
                var logRoot = Path.Combine(targetDir, "Logs");
                Directory.CreateDirectory(logRoot);
                File.AppendAllText(Path.Combine(logRoot, "Update.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + (string.IsNullOrWhiteSpace(message) ? "(no update message)" : message) + Environment.NewLine);
            }
            catch { }
        }

        private static bool TryGetOptionValue(string[] args, string option, out string value)
        {
            value = string.Empty;
            if (args == null) return false;
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase))
                {
                    value = args[i + 1];
                    return true;
                }
            }
            return false;
        }
    }
}
