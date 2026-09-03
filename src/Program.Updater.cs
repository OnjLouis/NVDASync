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
            var quiet = args.Any(arg => string.Equals(arg, "--update-quiet", StringComparison.OrdinalIgnoreCase));
            try
            {
                string zipUrl;
                string signatureUrl;
                string expectedVersion;
                string targetDir;
                string exePath;
                string tempBase;
                string pidText;
                var noRestart = args.Any(arg => string.Equals(arg, "--update-no-restart", StringComparison.OrdinalIgnoreCase));
                TryGetOptionValue(args, "--update-url", out zipUrl);
                TryGetOptionValue(args, "--signature-url", out signatureUrl);
                TryGetOptionValue(args, "--update-version", out expectedVersion);
                TryGetOptionValue(args, "--update-target", out targetDir);
                TryGetOptionValue(args, "--update-exe", out exePath);
                TryGetOptionValue(args, "--update-temp", out tempBase);
                TryGetOptionValue(args, "--update-wait-pid", out pidText);

                if (string.IsNullOrWhiteSpace(zipUrl) || string.IsNullOrWhiteSpace(signatureUrl) || string.IsNullOrWhiteSpace(expectedVersion) || string.IsNullOrWhiteSpace(targetDir) || string.IsNullOrWhiteSpace(exePath))
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

                ApplyUpdate(zipUrl, signatureUrl, expectedVersion, targetDir, exePath, string.IsNullOrWhiteSpace(tempBase) ? Path.GetTempPath() : tempBase, noRestart);
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                WriteUpdateHistory(args, "ERROR: " + ex.Message);
                WriteUpdaterLog(args, ex);
                if (!quiet)
                {
                    MessageBox.Show("NVDA Sync update failed:" + Environment.NewLine + Environment.NewLine + ex.Message, "NVDA Sync updater", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                Environment.ExitCode = 1;
            }
        }

        private static void ApplyUpdate(string zipUrl, string signatureUrl, string expectedVersion, string targetDir, string exePath, string tempBase, bool noRestart)
        {
            Directory.CreateDirectory(tempBase);
            var root = Path.Combine(tempBase, "NVDASyncUpdate_" + Guid.NewGuid().ToString("N"));
            var zip = Path.Combine(root, "update.zip");
            var signature = zip + ".sig";
            var stage = Path.Combine(root, "stage");
            var backupStage = Path.Combine(root, "backup");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(stage);
            Directory.CreateDirectory(backupStage);

            try
            {
                WriteUpdateHistory(targetDir, "Downloading update ZIP.");
                DownloadUpdateZip(zipUrl, zip);
                DownloadUpdateZip(signatureUrl, signature);
                WriteUpdateHistory(targetDir, "Verifying update signature.");
                if (!UpdateService.VerifyPackageSignature(zip, signature))
                {
                    throw new InvalidOperationException("The update signature is missing or invalid. No files were changed.");
                }
                WriteUpdateHistory(targetDir, "Extracting update ZIP.");
                ExtractSafely(zip, stage);

                var source = FindUpdateSourceFolder(stage);
                if (string.IsNullOrWhiteSpace(source))
                {
                    throw new InvalidOperationException("The update ZIP does not contain NVDASync.exe.");
                }

                var stagedExe = Path.Combine(source, "NVDASync.exe");
                System.Version expected;
                System.Version actual;
                var stagedVersion = FileVersionInfo.GetVersionInfo(stagedExe).FileVersion;
                if (!System.Version.TryParse(expectedVersion, out expected) || !System.Version.TryParse(stagedVersion, out actual) || !VersionsMatch(actual, expected))
                {
                    throw new InvalidOperationException("The signed update version does not match the GitHub release version.");
                }

                var allowedFiles = new[] { "NVDASync.exe", "NvdaAddonSync.exe", "Manual.html", "LICENSE.txt", "Get latest NVDA Sync.url" };
                var stagedFiles = Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly);
                if (Directory.GetDirectories(source).Length > 0 || stagedFiles.Any(path => !allowedFiles.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("The signed update contains unexpected files or folders.");
                }
                if (!new[] { "NVDASync.exe", "Manual.html", "LICENSE.txt" }.All(name => File.Exists(Path.Combine(source, name))))
                {
                    throw new InvalidOperationException("The signed update is incomplete.");
                }

                WriteUpdateHistory(targetDir, "Applying files.");
                Directory.CreateDirectory(targetDir);
                foreach (var item in stagedFiles)
                {
                    var name = Path.GetFileName(item);
                    var destination = Path.Combine(targetDir, name);
                    if (File.Exists(destination)) File.Copy(destination, Path.Combine(backupStage, name), true);
                }
                SaveUpdateBackup(targetDir, backupStage);
                var newFiles = stagedFiles.Where(item => !File.Exists(Path.Combine(targetDir, Path.GetFileName(item)))).Select(Path.GetFileName).ToList();
                try
                {
                    foreach (var item in stagedFiles) CopyFileWithRetry(item, Path.Combine(targetDir, Path.GetFileName(item)));
                }
                catch
                {
                    foreach (var newFile in newFiles) TryDeleteFile(Path.Combine(targetDir, newFile));
                    foreach (var backup in Directory.GetFiles(backupStage)) CopyFileWithRetry(backup, Path.Combine(targetDir, Path.GetFileName(backup)));
                    throw;
                }
                TryDeleteFile(Path.Combine(targetDir, "README.md"));
                TryDeleteFile(Path.Combine(targetDir, "NvdaAddonSync.exe"));
                UpdateStartupRegistrationAfterUpdate(targetDir);
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

        private static void DownloadUpdateZip(string zipUrl, string destination)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.UserAgent] = "NVDA Sync updater";
                client.DownloadFile(zipUrl, destination);
            }
        }

        private static void ExtractSafely(string zipPath, string destination)
        {
            var normalizedDestination = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var seenTargets = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                    if (!target.StartsWith(normalizedDestination, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("The update archive contains an unsafe path.");
                    }
                    if (!seenTargets.Add(target))
                    {
                        throw new InvalidOperationException("The update archive contains duplicate paths.");
                    }
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
            }
        }

        private static bool VersionsMatch(System.Version left, System.Version right)
        {
            return left.Major == right.Major &&
                   left.Minor == right.Minor &&
                   Math.Max(0, left.Build) == Math.Max(0, right.Build) &&
                   Math.Max(0, left.Revision) == Math.Max(0, right.Revision);
        }

        private static void SaveUpdateBackup(string targetDir, string backupStage)
        {
            if (Directory.GetFiles(backupStage).Length == 0) return;
            var backups = Path.Combine(targetDir, Path.Combine("Backups", "Updates"));
            Directory.CreateDirectory(backups);
            var archive = Path.Combine(backups, "NVDASync-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
            ZipFile.CreateFromDirectory(backupStage, archive);
            foreach (var old in Directory.GetFiles(backups, "NVDASync-*.zip").OrderByDescending(path => File.GetLastWriteTimeUtc(path)).Skip(2))
            {
                try { File.Delete(old); } catch { }
            }
        }

        private static string FindUpdateSourceFolder(string stage)
        {
            var direct = Path.Combine(stage, "NVDASync.exe");
            return File.Exists(direct) ? stage : string.Empty;
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
