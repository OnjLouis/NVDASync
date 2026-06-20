using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace NvdaAddonSync
{
    internal sealed class NvdaIniSectionManagerForm : Form
    {
        private readonly AppSettings settings;
        private readonly TextBox folderTextBox;
        private readonly CheckedListBox sectionsListBox;
        private readonly ComboBox destinationComboBox;
        private readonly TextBox logTextBox;
        private string currentFolder;
        private string currentIniPath;

        public NvdaIniSectionManagerForm(AppSettings settings, string initialFolder)
        {
            this.settings = settings;
            Text = "NVDA.ini Section Cleanup";
            StartPosition = FormStartPosition.CenterParent;
            Width = 760;
            Height = 560;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 7;
            root.Padding = new Padding(10);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var folderLabel = new Label();
            folderLabel.AutoSize = true;
            folderLabel.Text = "NVDA data &folder";
            root.Controls.Add(folderLabel, 0, 0);

            var folderPanel = new TableLayoutPanel();
            folderPanel.Dock = DockStyle.Top;
            folderPanel.ColumnCount = 2;
            folderPanel.RowCount = 1;
            folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.Controls.Add(folderPanel, 0, 1);

            folderTextBox = new TextBox();
            folderTextBox.ReadOnly = true;
            folderTextBox.Dock = DockStyle.Fill;
            folderTextBox.AccessibleName = "NVDA data folder";
            folderPanel.Controls.Add(folderTextBox, 0, 0);

            var browseButton = new Button();
            browseButton.Text = "&Browse...";
            browseButton.AutoSize = true;
            browseButton.Click += delegate { BrowseForFolder(); };
            folderPanel.Controls.Add(browseButton, 1, 0);

            sectionsListBox = new CheckedListBox();
            sectionsListBox.Dock = DockStyle.Fill;
            sectionsListBox.CheckOnClick = true;
            sectionsListBox.AccessibleName = "Sections found in nvda.ini";
            root.Controls.Add(sectionsListBox, 0, 2);

            var actionsPanel = new FlowLayoutPanel();
            actionsPanel.AutoSize = true;
            actionsPanel.Dock = DockStyle.Top;
            root.Controls.Add(actionsPanel, 0, 3);

            var deleteButton = new Button();
            deleteButton.Text = "&Delete selected";
            deleteButton.AutoSize = true;
            deleteButton.Click += delegate { DeleteSelectedSections(); };
            actionsPanel.Controls.Add(deleteButton);

            var destinationLabel = new Label();
            destinationLabel.AutoSize = true;
            destinationLabel.Text = "Copy/move destinat&ion";
            destinationLabel.Padding = new Padding(12, 6, 0, 0);
            actionsPanel.Controls.Add(destinationLabel);

            destinationComboBox = new ComboBox();
            destinationComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            destinationComboBox.Width = 360;
            destinationComboBox.AccessibleName = "Copy or move destination secondary folder";
            actionsPanel.Controls.Add(destinationComboBox);

            var copyButton = new Button();
            copyButton.Text = "&Copy selected";
            copyButton.AutoSize = true;
            copyButton.Click += delegate { CopySelectedSections(); };
            actionsPanel.Controls.Add(copyButton);

            var moveButton = new Button();
            moveButton.Text = "&Move selected";
            moveButton.AutoSize = true;
            moveButton.Click += delegate { MoveSelectedSections(); };
            actionsPanel.Controls.Add(moveButton);

            var logLabel = new Label();
            logLabel.AutoSize = true;
            logLabel.Text = "Section manager &log";
            root.Controls.Add(logLabel, 0, 4);

            logTextBox = new TextBox();
            logTextBox.Dock = DockStyle.Fill;
            logTextBox.Multiline = true;
            logTextBox.ReadOnly = true;
            logTextBox.ScrollBars = ScrollBars.Vertical;
            logTextBox.AccessibleName = "Section manager log";
            root.Controls.Add(logTextBox, 0, 5);

            var buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.AutoSize = true;
            buttons.Padding = new Padding(0, 8, 0, 0);
            root.Controls.Add(buttons, 0, 6);

            var closeButton = new Button();
            closeButton.Text = "Close";
            closeButton.AutoSize = true;
            closeButton.Click += delegate { Close(); };
            buttons.Controls.Add(closeButton);
            AcceptButton = closeButton;
            CancelButton = closeButton;

            NvdaIniSectionService.Message += AddLog;
            FormClosed += delegate { NvdaIniSectionService.Message -= AddLog; };
            KeyDown += OnKeyDown;

            LoadDestinations();
            SetFolder(initialFolder);
        }

        private void LoadDestinations()
        {
            destinationComboBox.Items.Clear();
            foreach (var profile in settings.SecondaryFolderProfiles)
            {
                if (profile == null || string.IsNullOrWhiteSpace(profile.Path))
                {
                    continue;
                }
                destinationComboBox.Items.Add(profile.Path);
            }
            if (destinationComboBox.Items.Count > 0)
            {
                destinationComboBox.SelectedIndex = 0;
            }
        }

        private void BrowseForFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose NVDA data folder";
                dialog.ShowNewFolderButton = false;
                if (!string.IsNullOrWhiteSpace(currentFolder) && Directory.Exists(currentFolder))
                {
                    dialog.SelectedPath = currentFolder;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    SetFolder(dialog.SelectedPath);
                }
            }
        }

        private void SetFolder(string folder)
        {
            try
            {
                currentFolder = SyncEngine.ResolveNvdaConfigDirectory(folder ?? string.Empty, "NVDA data folder", true);
                currentIniPath = Path.Combine(currentFolder, "nvda.ini");
                folderTextBox.Text = currentFolder;
                LoadSections();
            }
            catch (Exception ex)
            {
                currentFolder = folder ?? string.Empty;
                currentIniPath = string.Empty;
                folderTextBox.Text = currentFolder;
                sectionsListBox.Items.Clear();
                AddLog("Could not open NVDA data folder: " + ex.Message);
            }
        }

        private void LoadSections()
        {
            sectionsListBox.Items.Clear();
            logTextBox.Clear();
            if (string.IsNullOrWhiteSpace(currentIniPath) || !File.Exists(currentIniPath))
            {
                AddLog("No nvda.ini found in this folder.");
                return;
            }
            try
            {
                foreach (var name in NvdaIniSectionService.GetSectionNames(currentIniPath))
                {
                    sectionsListBox.Items.Add(name, false);
                }
                AddLog("Loaded " + sectionsListBox.Items.Count + " section(s) from " + currentIniPath);
            }
            catch (Exception ex)
            {
                AddLog("Could not read nvda.ini: " + ex.Message);
            }
        }

        private void DeleteSelectedSections()
        {
            var names = CheckedSectionNames();
            if (names.Count == 0)
            {
                AddLog("Choose one or more sections first.");
                FocusLog();
                return;
            }
            var confirmation = MessageBox.Show(
                this,
                "Delete " + names.Count + " section(s) from:" + Environment.NewLine + currentIniPath + Environment.NewLine + Environment.NewLine +
                "This cannot be undone.",
                "Delete nvda.ini sections",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (confirmation != DialogResult.OK)
            {
                return;
            }
            var relaunchNvda = PrepareForIniWrites(new[] { currentIniPath });
            if (relaunchNvda == null)
            {
                AddLog("Delete cancelled.");
                FocusLog();
                return;
            }
            foreach (var name in names)
            {
                try
                {
                    NvdaIniSectionService.DeleteSection(currentIniPath, name);
                }
                catch (Exception ex)
                {
                    AddLog("Delete failed for [" + name + "]: " + ex.Message);
                }
            }
            RelaunchNvda(relaunchNvda);
            LoadSections();
            FocusLog();
        }

        private void CopySelectedSections()
        {
            CopyOrMoveSelectedSections(false);
        }

        private void MoveSelectedSections()
        {
            CopyOrMoveSelectedSections(true);
        }

        private void CopyOrMoveSelectedSections(bool move)
        {
            var names = CheckedSectionNames();
            if (names.Count == 0)
            {
                AddLog("Choose one or more sections first.");
                FocusLog();
                return;
            }
            if (destinationComboBox.SelectedItem == null)
            {
                AddLog("Choose a destination secondary folder first.");
                FocusLog();
                return;
            }

            string destinationFolder;
            try
            {
                destinationFolder = SyncEngine.ResolveNvdaConfigDirectory(Convert.ToString(destinationComboBox.SelectedItem), "Move destination", false);
            }
            catch (Exception ex)
            {
                AddLog("Could not resolve destination: " + ex.Message);
                FocusLog();
                return;
            }
            var destinationIniPath = Path.Combine(destinationFolder, "nvda.ini");
            var conflicts = ExistingDestinationSections(destinationIniPath, names);
            var message = new StringBuilder();
            message.AppendLine((move ? "Move " : "Copy ") + names.Count + " section(s) from:");
            message.AppendLine(currentIniPath);
            message.AppendLine();
            message.AppendLine("To:");
            message.AppendLine(destinationIniPath);
            if (move)
            {
                message.AppendLine();
                message.AppendLine("Move deletes the selected section(s) from the source after writing them to the destination.");
            }
            else
            {
                message.AppendLine();
                message.AppendLine("Copy leaves the source nvda.ini unchanged.");
            }
            if (conflicts.Count > 0)
            {
                message.AppendLine();
                message.AppendLine("The destination already contains these section(s), which will be replaced:");
                foreach (var conflict in conflicts)
                {
                    message.AppendLine("[" + conflict + "]");
                }
            }
            var confirmation = MessageBox.Show(this, message.ToString(), (move ? "Move" : "Copy") + " nvda.ini sections", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirmation != DialogResult.OK)
            {
                return;
            }
            var pathsToWrite = move ? new[] { currentIniPath, destinationIniPath } : new[] { destinationIniPath };
            var relaunchNvda = PrepareForIniWrites(pathsToWrite);
            if (relaunchNvda == null)
            {
                AddLog((move ? "Move" : "Copy") + " cancelled.");
                FocusLog();
                return;
            }
            foreach (var name in names)
            {
                try
                {
                    if (move)
                    {
                        NvdaIniSectionService.MoveSection(currentIniPath, destinationIniPath, name, true);
                    }
                    else
                    {
                        NvdaIniSectionService.CopySection(currentIniPath, destinationIniPath, name, true);
                    }
                }
                catch (Exception ex)
                {
                    AddLog((move ? "Move" : "Copy") + " failed for [" + name + "]: " + ex.Message);
                }
            }
            RelaunchNvda(relaunchNvda);
            LoadSections();
            FocusLog();
        }

        private List<string> PrepareForIniWrites(IEnumerable<string> iniPaths)
        {
            var nvdaExePaths = RunningNvdaExePathsForIniFiles(iniPaths);
            if (nvdaExePaths.Count == 0)
            {
                return new List<string>();
            }
            var message = new StringBuilder();
            message.AppendLine("NVDA appears to be running for one or more nvda.ini files that will be changed.");
            message.AppendLine();
            message.AppendLine("If the file is edited while NVDA is running, NVDA can write its in-memory settings back over these changes.");
            message.AppendLine();
            message.AppendLine("Close NVDA now, apply the changes, and relaunch NVDA afterward?");
            var answer = MessageBox.Show(this, message.ToString(), "Close NVDA before changing nvda.ini", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                return null;
            }
            var closed = new List<string>();
            foreach (var exePath in nvdaExePaths)
            {
                if (CloseRunningNvda(exePath))
                {
                    closed.Add(exePath);
                }
                else
                {
                    AddLog("Could not close NVDA cleanly: " + exePath);
                    MessageBox.Show(this, "NVDA did not close cleanly. No changes were made." + Environment.NewLine + Environment.NewLine + exePath, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    RelaunchNvda(closed);
                    return null;
                }
            }
            return closed;
        }

        private static List<string> RunningNvdaExePathsForIniFiles(IEnumerable<string> iniPaths)
        {
            var result = new List<string>();
            var liveInstalledIni = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nvda", "nvda.ini");
            foreach (var process in Process.GetProcessesByName("nvda"))
            {
                string exePath;
                try
                {
                    exePath = process.MainModule.FileName;
                }
                catch
                {
                    continue;
                }
                foreach (var iniPath in iniPaths)
                {
                    if (string.IsNullOrWhiteSpace(iniPath))
                    {
                        continue;
                    }
                    if (string.Equals(Path.GetFullPath(iniPath), Path.GetFullPath(liveInstalledIni), StringComparison.OrdinalIgnoreCase))
                    {
                        AddUniquePath(result, exePath);
                        continue;
                    }
                    var configFolder = Path.GetDirectoryName(Path.GetFullPath(iniPath));
                    if (string.Equals(Path.GetFileName(configFolder), "userConfig", StringComparison.OrdinalIgnoreCase))
                    {
                        var portableRoot = Path.GetDirectoryName(configFolder);
                        var portableExe = Path.Combine(portableRoot, "nvda.exe");
                        if (string.Equals(Path.GetFullPath(portableExe), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase))
                        {
                            AddUniquePath(result, exePath);
                        }
                    }
                }
            }
            return result;
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
            paths.Add(path);
        }

        private static bool CloseRunningNvda(string exePath)
        {
            var closed = true;
            foreach (var process in Process.GetProcessesByName("nvda"))
            {
                string processPath;
                try
                {
                    processPath = process.MainModule.FileName;
                }
                catch
                {
                    continue;
                }
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

        private void RelaunchNvda(IEnumerable<string> exePaths)
        {
            foreach (var exePath in exePaths)
            {
                try
                {
                    if (File.Exists(exePath))
                    {
                        Process.Start(new ProcessStartInfo { FileName = exePath, WorkingDirectory = Path.GetDirectoryName(exePath), UseShellExecute = true });
                        AddLog("Relaunched NVDA: " + exePath);
                    }
                }
                catch (Exception ex)
                {
                    AddLog("Could not relaunch NVDA: " + ex.Message);
                }
            }
        }

        private List<string> ExistingDestinationSections(string destinationIniPath, List<string> names)
        {
            var conflicts = new List<string>();
            if (!File.Exists(destinationIniPath))
            {
                return conflicts;
            }
            try
            {
                var destinationNames = NvdaIniSectionService.GetSectionNames(destinationIniPath);
                foreach (var name in names)
                {
                    foreach (var destinationName in destinationNames)
                    {
                        if (string.Equals(name, destinationName, StringComparison.Ordinal))
                        {
                            conflicts.Add(name);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("Could not check destination conflicts: " + ex.Message);
            }
            return conflicts;
        }

        private List<string> CheckedSectionNames()
        {
            var names = new List<string>();
            foreach (var item in sectionsListBox.CheckedItems)
            {
                names.Add(Convert.ToString(item));
            }
            return names;
        }

        private void AddLog(string message)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AddLog), message);
                return;
            }
            logTextBox.AppendText(message + Environment.NewLine);
        }

        private void FocusLog()
        {
            if (logTextBox.CanFocus)
            {
                logTextBox.Focus();
                logTextBox.SelectionStart = logTextBox.TextLength;
                logTextBox.SelectionLength = 0;
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
                e.Handled = true;
            }
        }
    }
}
