using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace NvdaAddonSync
{
    internal sealed class IniSectionManagerForm : Form
    {
        private readonly AppSettings settings;
        private readonly string targetFileName;
        private readonly string backupNameExample;
        private readonly ComboBox sourceComboBox;
        private readonly CheckedListBox sectionsListBox;
        private readonly TextBox previewTextBox;
        private readonly ComboBox destinationComboBox;
        private readonly TextBox logTextBox;
        private string currentFolder;
        private string currentIniPath;
        private IniParseResult currentParseResult;
        private bool loadingSourceLocations;

        public IniSectionManagerForm(AppSettings settings, string initialFolder, string targetFileName, string title)
        {
            this.settings = settings;
            this.targetFileName = string.IsNullOrWhiteSpace(targetFileName) ? "nvda.ini" : targetFileName;
            backupNameExample = Path.GetFileNameWithoutExtension(this.targetFileName) + "YYYYMMDD-HHMMSS" + Path.GetExtension(this.targetFileName);
            Text = string.IsNullOrWhiteSpace(title) ? this.targetFileName + " Section Cleanup" : title;
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
            folderLabel.Text = this.targetFileName + " &location";
            root.Controls.Add(folderLabel, 0, 0);

            var folderPanel = new TableLayoutPanel();
            folderPanel.Dock = DockStyle.Top;
            folderPanel.ColumnCount = 2;
            folderPanel.RowCount = 1;
            folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folderPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(folderPanel, 0, 1);

            sourceComboBox = new ComboBox();
            sourceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            sourceComboBox.Dock = DockStyle.Fill;
            sourceComboBox.AccessibleName = this.targetFileName + " location";
            sourceComboBox.SelectionChangeCommitted += delegate { SourceLocationChanged(); };
            folderPanel.Controls.Add(sourceComboBox, 0, 0);

            var browseButton = new Button();
            browseButton.Text = "&Browse...";
            browseButton.AutoSize = true;
            browseButton.Click += delegate { BrowseForFolder(); };
            folderPanel.Controls.Add(browseButton, 1, 0);

            var sectionsPanel = new TableLayoutPanel();
            sectionsPanel.Dock = DockStyle.Fill;
            sectionsPanel.ColumnCount = 2;
            sectionsPanel.RowCount = 1;
            sectionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            sectionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            root.Controls.Add(sectionsPanel, 0, 2);

            sectionsListBox = new CheckedListBox();
            sectionsListBox.Dock = DockStyle.Fill;
            sectionsListBox.CheckOnClick = true;
            sectionsListBox.AccessibleName = "Sections found in " + this.targetFileName;
            sectionsListBox.SelectedIndexChanged += delegate { UpdatePreview(); };
            sectionsPanel.Controls.Add(sectionsListBox, 0, 0);

            previewTextBox = new TextBox();
            previewTextBox.Dock = DockStyle.Fill;
            previewTextBox.Multiline = true;
            previewTextBox.ReadOnly = true;
            previewTextBox.ScrollBars = ScrollBars.Both;
            previewTextBox.WordWrap = false;
            previewTextBox.AccessibleName = "Selected " + this.targetFileName + " section preview";
            sectionsPanel.Controls.Add(previewTextBox, 1, 0);

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

            IniSectionService.Message += AddLog;
            FormClosed += delegate { IniSectionService.Message -= AddLog; };
            KeyDown += OnKeyDown;

            LoadSourceLocations(initialFolder);
            LoadDestinations();
            SetFolder(initialFolder);
        }

        private sealed class IniLocationOption
        {
            public string Label { get; set; }
            public string Folder { get; set; }

            public override string ToString()
            {
                return Label;
            }
        }

        private void LoadSourceLocations(string initialFolder)
        {
            loadingSourceLocations = true;
            try
            {
                sourceComboBox.Items.Clear();
                AddSourceLocation("Primary", initialFolder);
                for (var index = 0; index < settings.SecondaryFolderProfiles.Count; index++)
                {
                    var profile = settings.SecondaryFolderProfiles[index];
                    if (profile == null || string.IsNullOrWhiteSpace(profile.Path))
                    {
                        continue;
                    }
                    AddSourceLocation("Secondary folder " + (index + 1), profile.Path);
                }
                if (sourceComboBox.Items.Count > 0)
                {
                    sourceComboBox.SelectedIndex = 0;
                }
            }
            finally
            {
                loadingSourceLocations = false;
            }
        }

        private void AddSourceLocation(string name, string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }
            sourceComboBox.Items.Add(new IniLocationOption
            {
                Label = name + ": " + folder,
                Folder = folder
            });
        }

        private void SourceLocationChanged()
        {
            if (loadingSourceLocations)
            {
                return;
            }
            var keepFocusOnSource = sourceComboBox.Focused;
            var option = sourceComboBox.SelectedItem as IniLocationOption;
            if (option != null)
            {
                SetFolder(option.Folder, false);
            }
            if (keepFocusOnSource && sourceComboBox.CanFocus)
            {
                BeginInvoke(new Action(delegate
                {
                    if (!IsDisposed && sourceComboBox.CanFocus)
                    {
                        sourceComboBox.Focus();
                    }
                }));
            }
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
                    loadingSourceLocations = true;
                    try
                    {
                        sourceComboBox.Items.Add(new IniLocationOption
                        {
                            Label = "Custom folder: " + dialog.SelectedPath,
                            Folder = dialog.SelectedPath
                        });
                        sourceComboBox.SelectedIndex = sourceComboBox.Items.Count - 1;
                    }
                    finally
                    {
                        loadingSourceLocations = false;
                    }
                    SetFolder(dialog.SelectedPath);
                }
            }
        }

        private void SetFolder(string folder)
        {
            SetFolder(folder, true);
        }

        private void SetFolder(string folder, bool selectFirstSection)
        {
            try
            {
                currentFolder = SyncEngine.ResolveNvdaConfigDirectory(folder ?? string.Empty, "NVDA data folder", true);
                currentIniPath = Path.Combine(currentFolder, targetFileName);
                sourceComboBox.AccessibleDescription = currentIniPath;
                LoadSections(selectFirstSection);
            }
            catch (Exception ex)
            {
                currentFolder = folder ?? string.Empty;
                currentIniPath = string.Empty;
                sourceComboBox.AccessibleDescription = currentFolder;
                sectionsListBox.Items.Clear();
                AddLog("Could not open NVDA data folder: " + ex.Message);
            }
        }

        private void LoadSections()
        {
            LoadSections(true);
        }

        private void LoadSections(bool selectFirstSection)
        {
            sectionsListBox.Items.Clear();
            previewTextBox.Clear();
            currentParseResult = null;
            logTextBox.Clear();
            if (string.IsNullOrWhiteSpace(currentIniPath) || !File.Exists(currentIniPath))
            {
                AddLog("No " + targetFileName + " found in this folder.");
                return;
            }
            try
            {
                currentParseResult = IniSectionService.ParseFile(currentIniPath);
                foreach (var section in currentParseResult.Sections)
                {
                    sectionsListBox.Items.Add(section.Name, false);
                }
                AddLog("Loaded " + sectionsListBox.Items.Count + " section(s) from " + currentIniPath);
                if (selectFirstSection && sectionsListBox.Items.Count > 0)
                {
                    sectionsListBox.SelectedIndex = 0;
                    UpdatePreview();
                }
            }
            catch (Exception ex)
            {
                AddLog("Could not read " + targetFileName + ": " + ex.Message);
            }
        }

        private void UpdatePreview()
        {
            if (currentParseResult == null || sectionsListBox.SelectedItem == null)
            {
                previewTextBox.Clear();
                return;
            }
            var selectedName = Convert.ToString(sectionsListBox.SelectedItem);
            foreach (var section in currentParseResult.Sections)
            {
                if (string.Equals(section.Name, selectedName, StringComparison.Ordinal))
                {
                    previewTextBox.Text = string.Join(currentParseResult.LineEnding, section.RawLines.ToArray());
                    previewTextBox.SelectionStart = 0;
                    previewTextBox.SelectionLength = 0;
                    return;
                }
            }
            previewTextBox.Clear();
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
                "NVDA Sync will create a backup beside " + targetFileName + " before changing it, named like " + backupNameExample + "." + Environment.NewLine + Environment.NewLine +
                "Continue?",
                "Delete " + targetFileName + " sections",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (confirmation != DialogResult.OK)
            {
                return;
            }
            AddLog("Requested delete from " + currentIniPath + ": " + FormatSectionList(names));
            var relaunchNvda = PrepareForIniWrites(new[] { currentIniPath });
            if (relaunchNvda == null)
            {
                AddLog("Delete cancelled.");
                FocusLog();
                return;
            }
            try
            {
                IniSectionService.DeleteSections(currentIniPath, names);
            }
            catch (Exception ex)
            {
                AddLog("Delete failed: " + ex.Message);
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
            var destinationIniPath = Path.Combine(destinationFolder, targetFileName);
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
                message.AppendLine("NVDA Sync will create backups beside existing " + targetFileName + " files before changing them.");
            }
            else
            {
                message.AppendLine();
                message.AppendLine("Copy leaves the source " + targetFileName + " unchanged.");
                message.AppendLine("NVDA Sync will create a backup beside the destination " + targetFileName + " before changing it.");
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
            var confirmation = MessageBox.Show(this, message.ToString(), (move ? "Move" : "Copy") + " " + targetFileName + " sections", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirmation != DialogResult.OK)
            {
                return;
            }
            AddLog("Requested " + (move ? "move" : "copy") + " from " + currentIniPath + " to " + destinationIniPath + ": " + FormatSectionList(names));
            var pathsToWrite = move ? new[] { currentIniPath, destinationIniPath } : new[] { destinationIniPath };
            var relaunchNvda = PrepareForIniWrites(pathsToWrite);
            if (relaunchNvda == null)
            {
                AddLog((move ? "Move" : "Copy") + " cancelled.");
                FocusLog();
                return;
            }
            try
            {
                if (move)
                {
                    IniSectionService.MoveSections(currentIniPath, destinationIniPath, names, true);
                }
                else
                {
                    IniSectionService.CopySections(currentIniPath, destinationIniPath, names, true);
                }
            }
            catch (Exception ex)
            {
                AddLog((move ? "Move" : "Copy") + " failed: " + ex.Message);
            }
            RelaunchNvda(relaunchNvda);
            LoadSections();
            FocusLog();
        }

        private List<string> PrepareForIniWrites(IEnumerable<string> iniPaths)
        {
            var nvdaExePaths = RunningNvdaExePathsForIniFiles(iniPaths, targetFileName);
            if (nvdaExePaths.Count == 0)
            {
                AddLog("No running NVDA instance matched the " + targetFileName + " file(s) being changed.");
                return new List<string>();
            }
            var message = new StringBuilder();
            message.AppendLine("NVDA appears to be running for one or more " + targetFileName + " files that will be changed.");
            message.AppendLine();
            message.AppendLine("If the file is edited while NVDA is running, NVDA can write its in-memory settings back over these changes.");
            message.AppendLine();
            message.AppendLine("Close NVDA now, apply the changes, and relaunch NVDA afterward?");
            if (!ShowLiveNvdaRestartConfirmation(message.ToString()))
            {
                return null;
            }
            AddLog("Closing NVDA before changing live " + targetFileName + ".");
            var closed = new List<string>();
            foreach (var exePath in nvdaExePaths)
            {
                if (CloseRunningNvda(exePath))
                {
                    closed.Add(exePath);
                    AddLog("Closed NVDA successfully: " + exePath);
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

        private bool ShowLiveNvdaRestartConfirmation(string message)
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

                return dialog.ShowDialog(this) == DialogResult.OK;
            }
        }

        private static List<string> RunningNvdaExePathsForIniFiles(IEnumerable<string> iniPaths, string targetFileName)
        {
            var result = new List<string>();
            var liveInstalledIni = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "nvda", targetFileName);
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
                foreach (var iniPath in iniPaths)
                {
                    if (string.IsNullOrWhiteSpace(iniPath))
                    {
                        continue;
                    }
                    if (string.Equals(Path.GetFullPath(iniPath), Path.GetFullPath(liveInstalledIni), StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsInstalledNvdaExecutable(exePath, installedNvdaExe))
                        {
                            AddUniquePath(result, exePath);
                        }
                        continue;
                    }
                    var configFolder = Path.GetDirectoryName(Path.GetFullPath(iniPath));
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
                foreach (var iniPath in iniPaths)
                {
                    if (string.IsNullOrWhiteSpace(iniPath))
                    {
                        continue;
                    }
                    if (string.Equals(Path.GetFullPath(iniPath), Path.GetFullPath(liveInstalledIni), StringComparison.OrdinalIgnoreCase))
                    {
                        AddUniquePath(result, installedNvdaExe);
                    }
                }
            }
            return result;
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
            paths.Add(path);
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
                string processPath;
                try
                {
                    processPath = GetProcessExecutablePath(process);
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
                    string processPath = null;
                    try
                    {
                        processPath = GetProcessExecutablePath(process);
                    }
                    catch
                    {
                    }
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

        private const int ProcessQueryLimitedInformation = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool QueryFullProcessImageName(IntPtr processHandle, int flags, StringBuilder executablePath, ref int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private void RelaunchNvda(IEnumerable<string> exePaths)
        {
            foreach (var exePath in exePaths)
            {
                try
                {
                    if (File.Exists(exePath))
                    {
                        AddLog("Reopening NVDA: " + exePath);
                        Process.Start(new ProcessStartInfo { FileName = exePath, WorkingDirectory = Path.GetDirectoryName(exePath), UseShellExecute = true });
                        AddLog("Reopened NVDA successfully: " + exePath);
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
                var destinationNames = IniSectionService.GetSectionNames(destinationIniPath);
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

        private static string FormatSectionList(IEnumerable<string> names)
        {
            var builder = new StringBuilder();
            foreach (var name in names)
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }
                builder.Append("[");
                builder.Append(name);
                builder.Append("]");
            }
            return builder.ToString();
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
            MainForm.AppendMainLog("[NVDA.ini cleanup] " + message);
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
