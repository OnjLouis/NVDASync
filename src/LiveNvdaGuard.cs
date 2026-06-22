using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace NvdaAddonSync
{
    internal static class LiveNvdaGuard
    {
        public static List<string> PrepareForWrites(IWin32Window owner, IEnumerable<string> paths, string fileLabel, Action<string> log)
        {
            var nvdaExePaths = RunningNvdaExePathsForFiles(paths);
            if (nvdaExePaths.Count == 0)
            {
                Log(log, "No running NVDA instance matched the " + fileLabel + " file(s) being changed.");
                return new List<string>();
            }

            var message = new StringBuilder();
            message.AppendLine("NVDA appears to be running for one or more " + fileLabel + " files that will be changed.");
            message.AppendLine();
            message.AppendLine("If the file is edited while NVDA is running, NVDA can write its in-memory settings back over these changes.");
            message.AppendLine();
            message.AppendLine("Close NVDA now, apply the changes, and relaunch NVDA afterward?");
            if (!ShowLiveNvdaRestartConfirmation(owner, message.ToString()))
            {
                return null;
            }

            Log(log, "Closing NVDA before changing live " + fileLabel + ".");
            var closed = new List<string>();
            foreach (var exePath in nvdaExePaths)
            {
                if (CloseRunningNvda(exePath))
                {
                    closed.Add(exePath);
                    Log(log, "Closed NVDA successfully: " + exePath);
                }
                else
                {
                    Log(log, "Could not close NVDA cleanly: " + exePath);
                    MessageBox.Show(owner, "NVDA did not close cleanly. No changes were made." + Environment.NewLine + Environment.NewLine + exePath, "Close and restart NVDA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Relaunch(closed, log);
                    return null;
                }
            }
            return closed;
        }

        public static void Relaunch(IEnumerable<string> exePaths, Action<string> log)
        {
            foreach (var exePath in exePaths)
            {
                try
                {
                    if (File.Exists(exePath))
                    {
                        Log(log, "Reopening NVDA: " + exePath);
                        Process.Start(new ProcessStartInfo { FileName = exePath, WorkingDirectory = Path.GetDirectoryName(exePath), UseShellExecute = true });
                        Log(log, "Reopened NVDA successfully: " + exePath);
                    }
                }
                catch (Exception ex)
                {
                    Log(log, "Could not relaunch NVDA: " + ex.Message);
                }
            }
        }

        private static bool ShowLiveNvdaRestartConfirmation(IWin32Window owner, string message)
        {
            using (var dialog = new Form())
            {
                dialog.Text = "Close and restart NVDA";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.Width = 620;
                dialog.Height = 330;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;

                var root = new TableLayoutPanel();
                root.Dock = DockStyle.Fill;
                root.ColumnCount = 1;
                root.RowCount = 3;
                root.Padding = new Padding(12);
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                dialog.Controls.Add(root);

                var messageBox = new TextBox();
                messageBox.Multiline = true;
                messageBox.ReadOnly = true;
                messageBox.Dock = DockStyle.Fill;
                messageBox.ScrollBars = ScrollBars.Vertical;
                messageBox.Text = message;
                messageBox.AccessibleName = "Live NVDA warning";
                root.Controls.Add(messageBox, 0, 0);

                var checkBox = new CheckBox();
                checkBox.AutoSize = true;
                checkBox.Text = "Close NVDA, apply the changes, then relaunch NVDA";
                checkBox.AccessibleName = "Confirm NVDA restart";
                root.Controls.Add(checkBox, 0, 1);

                var buttons = new FlowLayoutPanel();
                buttons.FlowDirection = FlowDirection.RightToLeft;
                buttons.Dock = DockStyle.Fill;
                buttons.AutoSize = true;
                buttons.Padding = new Padding(0, 8, 0, 0);
                root.Controls.Add(buttons, 0, 2);

                var okButton = new Button();
                okButton.Text = "OK";
                okButton.AutoSize = true;
                okButton.Enabled = false;
                okButton.DialogResult = DialogResult.OK;
                buttons.Controls.Add(okButton);

                var cancelButton = new Button();
                cancelButton.Text = "Cancel";
                cancelButton.AutoSize = true;
                cancelButton.DialogResult = DialogResult.Cancel;
                buttons.Controls.Add(cancelButton);

                checkBox.CheckedChanged += delegate { okButton.Enabled = checkBox.Checked; };
                dialog.AcceptButton = okButton;
                dialog.CancelButton = cancelButton;
                dialog.Shown += delegate { messageBox.Focus(); };

                return dialog.ShowDialog(owner) == DialogResult.OK;
            }
        }

        private static List<string> RunningNvdaExePathsForFiles(IEnumerable<string> paths)
        {
            var result = new List<string>();
            var liveInstalledConfigFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nvda");
            var installedNvdaExe = DetectInstalledNvdaExecutablePath();
            var hasRunningNvda = Process.GetProcessesByName("nvda").Length > 0;
            var anyReadableNvdaPath = false;
            foreach (var process in Process.GetProcessesByName("nvda"))
            {
                var exePath = GetProcessExecutablePath(process);
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    anyReadableNvdaPath = true;
                }
                foreach (var path in paths)
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }
                    var fullPath = Path.GetFullPath(path);
                    var configFolder = ConfigFolderForPath(fullPath);
                    if (string.Equals(configFolder, liveInstalledConfigFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsInstalledNvdaExecutable(exePath, installedNvdaExe))
                        {
                            AddUniquePath(result, exePath);
                        }
                        continue;
                    }
                    if (string.Equals(Path.GetFileName(configFolder), "userConfig", StringComparison.OrdinalIgnoreCase))
                    {
                        var portableRoot = Path.GetDirectoryName(configFolder);
                        var portableExe = Path.Combine(portableRoot, "nvda.exe");
                        if (!string.IsNullOrWhiteSpace(exePath) &&
                            string.Equals(Path.GetFullPath(portableExe), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase))
                        {
                            AddUniquePath(result, exePath);
                        }
                    }
                }
            }
            if (hasRunningNvda && !anyReadableNvdaPath)
            {
                foreach (var path in paths)
                {
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }
                    var configFolder = ConfigFolderForPath(Path.GetFullPath(path));
                    if (string.Equals(configFolder, liveInstalledConfigFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        AddUniquePath(result, installedNvdaExe);
                    }
                }
            }
            return result;
        }

        private static string ConfigFolderForPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }
            var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            while (!string.IsNullOrWhiteSpace(directory))
            {
                var name = Path.GetFileName(directory);
                if (string.Equals(name, "userConfig", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "nvda", StringComparison.OrdinalIgnoreCase))
                {
                    return directory;
                }
                directory = Path.GetDirectoryName(directory);
            }
            return Path.GetDirectoryName(path) ?? string.Empty;
        }

        private static bool IsInstalledNvdaExecutable(string exePath, string installedNvdaExe)
        {
            return !string.IsNullOrWhiteSpace(exePath) &&
                   !string.IsNullOrWhiteSpace(installedNvdaExe) &&
                   string.Equals(Path.GetFullPath(exePath), Path.GetFullPath(installedNvdaExe), StringComparison.OrdinalIgnoreCase);
        }

        private static string GetProcessExecutablePath(Process process)
        {
            try
            {
                return process.MainModule.FileName;
            }
            catch
            {
            }

            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = OpenProcess(ProcessQueryLimitedInformation, false, process.Id);
                if (handle == IntPtr.Zero)
                {
                    return string.Empty;
                }
                var capacity = 1024;
                var builder = new StringBuilder(capacity);
                if (QueryFullProcessImageName(handle, 0, builder, ref capacity))
                {
                    return builder.ToString();
                }
            }
            catch
            {
            }
            finally
            {
                if (handle != IntPtr.Zero)
                {
                    CloseHandle(handle);
                }
            }
            return string.Empty;
        }

        private static string DetectInstalledNvdaExecutablePath()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVDA", "nvda.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "NVDA", "nvda.exe")
            };
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return "nvda.exe";
        }

        private static void AddUniquePath(List<string> paths, string path)
        {
            foreach (var existing in paths)
            {
                if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path);
            }
        }

        private static bool CloseRunningNvda(string exePath)
        {
            if (TryQuitNvdaWithCommandLine(exePath))
            {
                return WaitForNvdaToExit(exePath, 15000);
            }
            var closed = true;
            foreach (var process in Process.GetProcessesByName("nvda"))
            {
                var processPath = GetProcessExecutablePath(process);
                if (!string.Equals(processPath, exePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                try
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(15000))
                    {
                        closed = false;
                    }
                }
                catch
                {
                    closed = false;
                }
            }
            return closed;
        }

        private static bool TryQuitNvdaWithCommandLine(string exePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "-q",
                    WorkingDirectory = File.Exists(exePath) ? Path.GetDirectoryName(exePath) : null,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool WaitForNvdaToExit(string exePath, int timeoutMilliseconds)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (DateTime.UtcNow < deadline)
            {
                var stillRunning = false;
                foreach (var process in Process.GetProcessesByName("nvda"))
                {
                    var processPath = GetProcessExecutablePath(process);
                    if (string.IsNullOrWhiteSpace(processPath) ||
                        string.IsNullOrWhiteSpace(exePath) ||
                        string.Equals(processPath, exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        stillRunning = true;
                        break;
                    }
                }
                if (!stillRunning)
                {
                    return true;
                }
                System.Threading.Thread.Sleep(250);
            }
            return false;
        }

        private static void Log(Action<string> log, string message)
        {
            if (log != null)
            {
                log(message);
            }
        }

        private const int ProcessQueryLimitedInformation = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool QueryFullProcessImageName(IntPtr processHandle, int flags, StringBuilder executablePath, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
