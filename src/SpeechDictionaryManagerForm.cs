using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace NvdaAddonSync
{
    internal sealed class SpeechDictionaryManagerForm : Form
    {
        private readonly AppSettings settings;
        private readonly ComboBox sourceLocationComboBox;
        private readonly ComboBox sourceDictionaryComboBox;
        private readonly ListView entriesListView;
        private readonly ComboBox destinationLocationComboBox;
        private readonly ComboBox destinationDictionaryComboBox;
        private readonly TextBox logTextBox;
        private string currentSourceFolder;
        private SpeechDictionaryParseResult currentParseResult;
        private SpeechDictionaryFileInfo currentSourceDictionary;
        private bool loadingSourceLocations;
        private bool loadingDestinationLocations;

        public SpeechDictionaryManagerForm(AppSettings settings, string initialFolder)
        {
            this.settings = settings;
            Text = "Speech Dictionary Entries";
            StartPosition = FormStartPosition.CenterParent;
            Width = 900;
            Height = 640;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 10;
            root.Padding = new Padding(10);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var sourceLabel = new Label();
            sourceLabel.AutoSize = true;
            sourceLabel.Text = "&Source NVDA folder";
            root.Controls.Add(sourceLabel, 0, 0);

            var sourcePanel = new TableLayoutPanel();
            sourcePanel.Dock = DockStyle.Top;
            sourcePanel.ColumnCount = 2;
            sourcePanel.RowCount = 1;
            sourcePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sourcePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.Controls.Add(sourcePanel, 0, 1);

            sourceLocationComboBox = new ComboBox();
            sourceLocationComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            sourceLocationComboBox.Dock = DockStyle.Fill;
            sourceLocationComboBox.AccessibleName = "Source NVDA folder";
            sourceLocationComboBox.SelectionChangeCommitted += delegate { SourceLocationChanged(); };
            sourcePanel.Controls.Add(sourceLocationComboBox, 0, 0);

            var browseSourceButton = new Button();
            browseSourceButton.Text = "&Browse...";
            browseSourceButton.AutoSize = true;
            browseSourceButton.Click += delegate { BrowseForSourceFolder(); };
            sourcePanel.Controls.Add(browseSourceButton, 1, 0);

            var sourceDictionaryPanel = new TableLayoutPanel();
            sourceDictionaryPanel.Dock = DockStyle.Top;
            sourceDictionaryPanel.ColumnCount = 2;
            sourceDictionaryPanel.RowCount = 1;
            sourceDictionaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            sourceDictionaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(sourceDictionaryPanel, 0, 2);

            var dictionaryLabel = new Label();
            dictionaryLabel.AutoSize = true;
            dictionaryLabel.Text = "Source &dictionary";
            dictionaryLabel.Padding = new Padding(0, 6, 8, 0);
            sourceDictionaryPanel.Controls.Add(dictionaryLabel, 0, 0);

            sourceDictionaryComboBox = new ComboBox();
            sourceDictionaryComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            sourceDictionaryComboBox.Dock = DockStyle.Fill;
            sourceDictionaryComboBox.AccessibleName = "Source speech dictionary";
            sourceDictionaryComboBox.SelectionChangeCommitted += delegate { SourceDictionaryChanged(true); };
            sourceDictionaryPanel.Controls.Add(sourceDictionaryComboBox, 1, 0);

            entriesListView = new ListView();
            entriesListView.Dock = DockStyle.Fill;
            entriesListView.View = View.Details;
            entriesListView.FullRowSelect = true;
            entriesListView.HideSelection = false;
            entriesListView.MultiSelect = true;
            entriesListView.AccessibleName = "Speech dictionary entries";
            entriesListView.Columns.Add("Pattern", 180);
            entriesListView.Columns.Add("Replacement", 180);
            entriesListView.Columns.Add("Case-sensitive", 95);
            entriesListView.Columns.Add("Type", 70);
            entriesListView.Columns.Add("Comment", 300);
            root.Controls.Add(entriesListView, 0, 3);

            var destinationLabel = new Label();
            destinationLabel.AutoSize = true;
            destinationLabel.Text = "Destination NVDA f&older";
            root.Controls.Add(destinationLabel, 0, 4);

            var destinationPanel = new TableLayoutPanel();
            destinationPanel.Dock = DockStyle.Top;
            destinationPanel.ColumnCount = 2;
            destinationPanel.RowCount = 1;
            destinationPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            destinationPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.Controls.Add(destinationPanel, 0, 5);

            destinationLocationComboBox = new ComboBox();
            destinationLocationComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            destinationLocationComboBox.Dock = DockStyle.Fill;
            destinationLocationComboBox.AccessibleName = "Destination NVDA folder";
            destinationLocationComboBox.SelectionChangeCommitted += delegate { DestinationLocationChanged(); };
            destinationPanel.Controls.Add(destinationLocationComboBox, 0, 0);

            var browseDestinationButton = new Button();
            browseDestinationButton.Text = "B&rowse...";
            browseDestinationButton.AutoSize = true;
            browseDestinationButton.Click += delegate { BrowseForDestinationFolder(); };
            destinationPanel.Controls.Add(browseDestinationButton, 1, 0);

            var destinationDictionaryPanel = new TableLayoutPanel();
            destinationDictionaryPanel.Dock = DockStyle.Top;
            destinationDictionaryPanel.ColumnCount = 2;
            destinationDictionaryPanel.RowCount = 1;
            destinationDictionaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            destinationDictionaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(destinationDictionaryPanel, 0, 6);

            var destinationDictionaryLabel = new Label();
            destinationDictionaryLabel.AutoSize = true;
            destinationDictionaryLabel.Text = "Destination dictionar&y";
            destinationDictionaryLabel.Padding = new Padding(0, 6, 8, 0);
            destinationDictionaryPanel.Controls.Add(destinationDictionaryLabel, 0, 0);

            destinationDictionaryComboBox = new ComboBox();
            destinationDictionaryComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            destinationDictionaryComboBox.Dock = DockStyle.Fill;
            destinationDictionaryComboBox.AccessibleName = "Destination speech dictionary";
            destinationDictionaryPanel.Controls.Add(destinationDictionaryComboBox, 1, 0);

            var actionsPanel = new FlowLayoutPanel();
            actionsPanel.AutoSize = true;
            actionsPanel.Dock = DockStyle.Top;
            root.Controls.Add(actionsPanel, 0, 7);

            var syncWholeButton = new Button();
            syncWholeButton.Text = "&Sync whole dictionary";
            syncWholeButton.AutoSize = true;
            syncWholeButton.Click += delegate { SyncWholeDictionary(); };
            actionsPanel.Controls.Add(syncWholeButton);

            var importButton = new Button();
            importButton.Text = "&Import .dic file";
            importButton.AutoSize = true;
            importButton.Click += delegate { ImportDictionaryFile(); };
            actionsPanel.Controls.Add(importButton);

            var deleteButton = new Button();
            deleteButton.Text = "De&lete selected";
            deleteButton.AutoSize = true;
            deleteButton.Click += delegate { DeleteSelectedEntries(); };
            actionsPanel.Controls.Add(deleteButton);

            var copyButton = new Button();
            copyButton.Text = "&Copy selected";
            copyButton.AutoSize = true;
            copyButton.Click += delegate { CopyOrMoveSelectedEntries(false); };
            actionsPanel.Controls.Add(copyButton);

            var moveButton = new Button();
            moveButton.Text = "&Move selected";
            moveButton.AutoSize = true;
            moveButton.Click += delegate { CopyOrMoveSelectedEntries(true); };
            actionsPanel.Controls.Add(moveButton);

            logTextBox = new TextBox();
            logTextBox.Dock = DockStyle.Fill;
            logTextBox.Multiline = true;
            logTextBox.ReadOnly = true;
            logTextBox.ScrollBars = ScrollBars.Vertical;
            logTextBox.AccessibleName = "Speech dictionary manager log";
            root.Controls.Add(logTextBox, 0, 8);

            var buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.AutoSize = true;
            buttons.Padding = new Padding(0, 8, 0, 0);
            root.Controls.Add(buttons, 0, 9);

            var closeButton = new Button();
            closeButton.Text = "Close";
            closeButton.AutoSize = true;
            closeButton.Click += delegate { Close(); };
            buttons.Controls.Add(closeButton);
            AcceptButton = closeButton;
            CancelButton = closeButton;

            SpeechDictionaryFileService.Message += AddLog;
            FormClosed += delegate { SpeechDictionaryFileService.Message -= AddLog; };
            KeyDown += OnKeyDown;

            LoadLocations(initialFolder);
            SetSourceFolder(initialFolder, true);
        }

        private sealed class FolderLocationOption
        {
            public string Label { get; set; }
            public string Folder { get; set; }

            public override string ToString()
            {
                return Label;
            }
        }

        private sealed class DictionaryDestinationOption
        {
            public string Label { get; set; }
            public string Path { get; set; }
            public string RelativePath { get; set; }

            public override string ToString()
            {
                return Label;
            }
        }

        private sealed class ImportTargetSelection
        {
            public string Folder { get; set; }
            public string Label { get; set; }
        }

        private void LoadLocations(string initialFolder)
        {
            loadingSourceLocations = true;
            loadingDestinationLocations = true;
            try
            {
                sourceLocationComboBox.Items.Clear();
                destinationLocationComboBox.Items.Clear();
                AddLocation(sourceLocationComboBox, "Primary", initialFolder);
                AddLocation(destinationLocationComboBox, "Primary", initialFolder);
                for (var index = 0; index < settings.SecondaryFolderProfiles.Count; index++)
                {
                    var profile = settings.SecondaryFolderProfiles[index];
                    if (profile == null || string.IsNullOrWhiteSpace(profile.Path))
                    {
                        continue;
                    }
                    AddLocation(sourceLocationComboBox, "Secondary folder " + (index + 1), profile.Path);
                    AddLocation(destinationLocationComboBox, "Secondary folder " + (index + 1), profile.Path);
                }
                if (sourceLocationComboBox.Items.Count > 0)
                {
                    sourceLocationComboBox.SelectedIndex = 0;
                }
                if (destinationLocationComboBox.Items.Count > 1)
                {
                    destinationLocationComboBox.SelectedIndex = 1;
                }
                else if (destinationLocationComboBox.Items.Count > 0)
                {
                    destinationLocationComboBox.SelectedIndex = 0;
                }
            }
            finally
            {
                loadingSourceLocations = false;
                loadingDestinationLocations = false;
            }
        }

        private static void AddLocation(ComboBox comboBox, string name, string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }
            comboBox.Items.Add(new FolderLocationOption { Label = name + ": " + folder, Folder = folder });
        }

        private void SourceLocationChanged()
        {
            if (loadingSourceLocations)
            {
                return;
            }
            var option = sourceLocationComboBox.SelectedItem as FolderLocationOption;
            if (option != null)
            {
                SetSourceFolder(option.Folder, true);
            }
        }

        private void DestinationLocationChanged()
        {
            if (loadingDestinationLocations)
            {
                return;
            }
            LoadDestinationDictionaries();
        }

        private void BrowseForSourceFolder()
        {
            var selected = BrowseFolder(currentSourceFolder, "Choose source NVDA folder");
            if (selected == null)
            {
                return;
            }
            loadingSourceLocations = true;
            try
            {
                sourceLocationComboBox.Items.Add(new FolderLocationOption { Label = "Custom folder: " + selected, Folder = selected });
                sourceLocationComboBox.SelectedIndex = sourceLocationComboBox.Items.Count - 1;
            }
            finally
            {
                loadingSourceLocations = false;
            }
            SetSourceFolder(selected, true);
        }

        private void BrowseForDestinationFolder()
        {
            var selected = BrowseFolder(DestinationFolder(), "Choose destination NVDA folder");
            if (selected == null)
            {
                return;
            }
            loadingDestinationLocations = true;
            try
            {
                destinationLocationComboBox.Items.Add(new FolderLocationOption { Label = "Custom folder: " + selected, Folder = selected });
                destinationLocationComboBox.SelectedIndex = destinationLocationComboBox.Items.Count - 1;
            }
            finally
            {
                loadingDestinationLocations = false;
            }
            LoadDestinationDictionaries();
        }

        private string BrowseFolder(string initialPath, string description)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = description;
                dialog.ShowNewFolderButton = true;
                if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
                {
                    dialog.SelectedPath = initialPath;
                }
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
            }
        }

        private void SetSourceFolder(string folder, bool selectFirstDictionary)
        {
            try
            {
                currentSourceFolder = SyncEngine.ResolveNvdaConfigDirectory(folder ?? string.Empty, "Source NVDA folder", true);
                LoadSourceDictionaries(selectFirstDictionary);
            }
            catch (Exception ex)
            {
                currentSourceFolder = folder ?? string.Empty;
                sourceDictionaryComboBox.Items.Clear();
                entriesListView.Items.Clear();
                currentParseResult = null;
                currentSourceDictionary = null;
                AddLog("Could not open source NVDA folder: " + ex.Message);
            }
        }

        private void LoadSourceDictionaries(bool selectFirstDictionary)
        {
            sourceDictionaryComboBox.Items.Clear();
            entriesListView.Items.Clear();
            currentParseResult = null;
            currentSourceDictionary = null;
            var dictionaries = SpeechDictionaryFileService.DiscoverDictionaryFiles(currentSourceFolder);
            foreach (var dictionary in dictionaries)
            {
                sourceDictionaryComboBox.Items.Add(dictionary);
            }
            AddLog("Found " + FormatCount(dictionaries.Count, "speech dictionary", "speech dictionaries") + " in " + currentSourceFolder);
            if (selectFirstDictionary && sourceDictionaryComboBox.Items.Count > 0)
            {
                sourceDictionaryComboBox.SelectedIndex = 0;
                SourceDictionaryChanged(true);
            }
            else
            {
                LoadDestinationDictionaries();
            }
        }

        private void SourceDictionaryChanged(bool selectMatchingDestination)
        {
            currentSourceDictionary = sourceDictionaryComboBox.SelectedItem as SpeechDictionaryFileInfo;
            entriesListView.Items.Clear();
            currentParseResult = null;
            if (currentSourceDictionary == null)
            {
                LoadDestinationDictionaries();
                return;
            }
            try
            {
                currentParseResult = SpeechDictionaryFileService.ParseFile(currentSourceDictionary.Path);
                foreach (var entry in currentParseResult.Entries)
                {
                    var item = new ListViewItem(entry.Pattern);
                    item.SubItems.Add(entry.Replacement);
                    item.SubItems.Add(entry.DisplayCaseSensitive);
                    item.SubItems.Add(entry.TypeRaw);
                    item.SubItems.Add(entry.Comment);
                    item.Tag = entry;
                    entriesListView.Items.Add(item);
                }
                AddLog("Loaded " + FormatCount(entriesListView.Items.Count, "entry", "entries") + " from " + currentSourceDictionary.Path);
            }
            catch (Exception ex)
            {
                AddLog("Could not read dictionary: " + ex.Message);
            }
            LoadDestinationDictionaries();
            if (selectMatchingDestination)
            {
                SelectMatchingDestinationDictionary();
            }
        }

        private void LoadDestinationDictionaries()
        {
            destinationDictionaryComboBox.Items.Clear();
            var destinationFolder = DestinationFolder();
            if (string.IsNullOrWhiteSpace(destinationFolder))
            {
                return;
            }
            try
            {
                destinationFolder = SyncEngine.ResolveNvdaConfigDirectory(destinationFolder, "Destination NVDA folder", false);
                var dictionaries = SpeechDictionaryFileService.DiscoverDictionaryFiles(destinationFolder);
                foreach (var dictionary in dictionaries)
                {
                    destinationDictionaryComboBox.Items.Add(new DictionaryDestinationOption
                    {
                        Label = dictionary.DisplayName + ": " + dictionary.RelativePath,
                        Path = dictionary.Path,
                        RelativePath = dictionary.RelativePath
                    });
                }
                if (currentSourceDictionary != null)
                {
                    var destinationPath = SpeechDictionaryFileService.BuildDestinationPath(destinationFolder, currentSourceDictionary.RelativePath);
                    destinationDictionaryComboBox.Items.Insert(0, new DictionaryDestinationOption
                    {
                        Label = "Matching source dictionary: " + currentSourceDictionary.RelativePath,
                        Path = destinationPath,
                        RelativePath = currentSourceDictionary.RelativePath
                    });
                }
                if (destinationDictionaryComboBox.Items.Count > 0)
                {
                    destinationDictionaryComboBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                AddLog("Could not read destination dictionaries: " + ex.Message);
            }
        }

        private void SelectMatchingDestinationDictionary()
        {
            if (currentSourceDictionary == null)
            {
                return;
            }
            for (var index = 0; index < destinationDictionaryComboBox.Items.Count; index++)
            {
                var option = destinationDictionaryComboBox.Items[index] as DictionaryDestinationOption;
                if (option != null && string.Equals(option.RelativePath, currentSourceDictionary.RelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    destinationDictionaryComboBox.SelectedIndex = index;
                    return;
                }
            }
        }

        private void SyncWholeDictionary()
        {
            if (currentSourceDictionary == null)
            {
                AddLog("Choose a source dictionary first.");
                FocusLog();
                return;
            }
            var destinationPath = DestinationDictionaryPath();
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                AddLog("Choose a destination dictionary first.");
                FocusLog();
                return;
            }
            var confirmation = MessageBox.Show(
                this,
                "Replace the destination dictionary with the source dictionary?" + Environment.NewLine + Environment.NewLine +
                "Source:" + Environment.NewLine + currentSourceDictionary.Path + Environment.NewLine + Environment.NewLine +
                "Destination:" + Environment.NewLine + destinationPath + Environment.NewLine + Environment.NewLine +
                "NVDA Sync will create a backup beside the destination dictionary before changing it.",
                "Sync whole speech dictionary",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);
            if (confirmation != DialogResult.OK)
            {
                return;
            }
            AddLog("Requested whole dictionary sync from " + currentSourceDictionary.Path + " to " + destinationPath);
            var relaunchNvda = LiveNvdaGuard.PrepareForWrites(this, new[] { destinationPath }, "speech dictionary", AddLog);
            if (relaunchNvda == null)
            {
                AddLog("Whole dictionary sync cancelled.");
                FocusLog();
                return;
            }
            try
            {
                SpeechDictionaryFileService.ReplaceFile(currentSourceDictionary.Path, destinationPath);
            }
            catch (Exception ex)
            {
                AddLog("Whole dictionary sync failed: " + ex.Message);
            }
            LiveNvdaGuard.Relaunch(relaunchNvda, AddLog);
            LoadDestinationDictionaries();
            FocusLog();
        }

        private void ImportDictionaryFile()
        {
            string importPath;
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Import NVDA speech dictionary";
                dialog.Filter = "NVDA speech dictionaries (*.dic)|*.dic|All files (*.*)|*.*";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                var initialImportFolder = currentSourceDictionary == null ? currentSourceFolder : Path.GetDirectoryName(currentSourceDictionary.Path);
                if (!string.IsNullOrWhiteSpace(initialImportFolder) && Directory.Exists(initialImportFolder))
                {
                    dialog.InitialDirectory = initialImportFolder;
                }
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                importPath = dialog.FileName;
            }

            var destinationSelection = ChooseImportDestinationFolder(importPath);
            if (destinationSelection == null)
            {
                AddLog("Dictionary import cancelled before choosing a target NVDA folder.");
                FocusLog();
                return;
            }

            var destinationFolder = destinationSelection.Folder;
            try
            {
                destinationFolder = SyncEngine.ResolveNvdaConfigDirectory(destinationFolder, "Import target NVDA folder", false);
            }
            catch (Exception ex)
            {
                AddLog("Could not open import target NVDA folder: " + ex.Message);
                FocusLog();
                return;
            }

            string relativePath;
            string destinationPath;
            int entryCount;
            try
            {
                relativePath = SpeechDictionaryFileService.InferImportRelativePath(importPath);
                destinationPath = SpeechDictionaryFileService.BuildDestinationPath(destinationFolder, relativePath);
                entryCount = SpeechDictionaryFileService.ParseFile(importPath).Entries.Count;
                if (entryCount == 0)
                {
                    AddLog("Import failed: the selected .dic file does not contain any dictionary entries.");
                    FocusLog();
                    return;
                }
            }
            catch (Exception ex)
            {
                AddLog("Import failed: " + ex.Message);
                FocusLog();
                return;
            }

            var message = new StringBuilder();
            message.AppendLine("Import this NVDA speech dictionary?");
            message.AppendLine();
            message.AppendLine("Source:");
            message.AppendLine(importPath);
            message.AppendLine();
            message.AppendLine("Destination:");
            message.AppendLine(destinationPath);
            message.AppendLine();
            message.AppendLine("Target folder:");
            message.AppendLine(destinationSelection.Label);
            message.AppendLine();
            message.AppendLine("Inferred location:");
            message.AppendLine(relativePath);
            message.AppendLine();
            message.AppendLine("Entries: " + entryCount);
            message.AppendLine("NVDA Sync will create a backup beside the destination dictionary before replacing it.");
            var confirmation = MessageBox.Show(this, message.ToString(), "Import speech dictionary", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirmation != DialogResult.OK)
            {
                return;
            }

            AddLog("Requested dictionary import from " + importPath + " to " + destinationPath);
            var relaunchNvda = LiveNvdaGuard.PrepareForWrites(this, new[] { destinationPath }, "speech dictionary", AddLog);
            if (relaunchNvda == null)
            {
                AddLog("Dictionary import cancelled.");
                FocusLog();
                return;
            }
            try
            {
                SpeechDictionaryFileService.ImportFile(importPath, destinationFolder);
            }
            catch (Exception ex)
            {
                AddLog("Dictionary import failed: " + ex.Message);
            }
            LiveNvdaGuard.Relaunch(relaunchNvda, AddLog);
            LoadDestinationDictionaries();
            FocusLog();
        }

        private ImportTargetSelection ChooseImportDestinationFolder(string importPath)
        {
            using (var dialog = new Form())
            {
                dialog.Text = "Choose import target";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.Width = 680;
                dialog.Height = 220;

                var root = new TableLayoutPanel();
                root.Dock = DockStyle.Fill;
                root.Padding = new Padding(12);
                root.RowCount = 4;
                root.ColumnCount = 2;
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                dialog.Controls.Add(root);

                var explanation = new Label();
                explanation.AutoSize = true;
                explanation.Dock = DockStyle.Top;
                explanation.Text = "Choose the NVDA folder that should receive this dictionary. The source file can be anywhere.";
                root.Controls.Add(explanation, 0, 0);
                root.SetColumnSpan(explanation, 2);

                var sourceLabel = new Label();
                sourceLabel.AutoSize = true;
                sourceLabel.Dock = DockStyle.Top;
                sourceLabel.Padding = new Padding(0, 8, 0, 8);
                sourceLabel.Text = "File: " + importPath;
                root.Controls.Add(sourceLabel, 0, 1);
                root.SetColumnSpan(sourceLabel, 2);

                var targetComboBox = new ComboBox();
                targetComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
                targetComboBox.Dock = DockStyle.Top;
                targetComboBox.AccessibleName = "Import target NVDA folder";
                root.Controls.Add(targetComboBox, 0, 2);

                foreach (var target in ImportTargetOptions())
                {
                    targetComboBox.Items.Add(target);
                }
                if (targetComboBox.Items.Count > 0)
                {
                    targetComboBox.SelectedIndex = 0;
                }

                var browseButton = new Button();
                browseButton.Text = "&Browse...";
                browseButton.AutoSize = true;
                browseButton.Click += delegate
                {
                    var selected = BrowseFolder(DestinationFolder(), "Choose import target NVDA folder");
                    if (selected == null)
                    {
                        return;
                    }
                    var custom = new FolderLocationOption { Label = "Custom folder: " + selected, Folder = selected };
                    targetComboBox.Items.Add(custom);
                    targetComboBox.SelectedItem = custom;
                };
                root.Controls.Add(browseButton, 1, 2);

                var buttonsPanel = new FlowLayoutPanel();
                buttonsPanel.AutoSize = true;
                buttonsPanel.Dock = DockStyle.Bottom;
                buttonsPanel.FlowDirection = FlowDirection.RightToLeft;
                root.Controls.Add(buttonsPanel, 0, 3);
                root.SetColumnSpan(buttonsPanel, 2);

                var okButton = new Button();
                okButton.Text = "OK";
                okButton.AutoSize = true;
                okButton.DialogResult = DialogResult.OK;
                buttonsPanel.Controls.Add(okButton);

                var cancelButton = new Button();
                cancelButton.Text = "Cancel";
                cancelButton.AutoSize = true;
                cancelButton.DialogResult = DialogResult.Cancel;
                buttonsPanel.Controls.Add(cancelButton);

                dialog.AcceptButton = okButton;
                dialog.CancelButton = cancelButton;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return null;
                }
                var option = targetComboBox.SelectedItem as FolderLocationOption;
                if (option == null || string.IsNullOrWhiteSpace(option.Folder))
                {
                    return null;
                }
                return new ImportTargetSelection { Folder = option.Folder, Label = option.Label };
            }
        }

        private IEnumerable<FolderLocationOption> ImportTargetOptions()
        {
            foreach (var item in destinationLocationComboBox.Items)
            {
                var option = item as FolderLocationOption;
                if (option == null || string.IsNullOrWhiteSpace(option.Folder))
                {
                    continue;
                }
                yield return new FolderLocationOption { Label = option.Label, Folder = option.Folder };
            }
        }

        private void DeleteSelectedEntries()
        {
            var indices = SelectedEntryIndices();
            if (indices.Count == 0)
            {
                AddLog("Choose one or more entries first.");
                FocusLog();
                return;
            }
            if (currentSourceDictionary == null)
            {
                AddLog("Choose a source dictionary first.");
                FocusLog();
                return;
            }
            var confirmation = MessageBox.Show(
                this,
                "Delete " + FormatCount(indices.Count, "selected entry", "selected entries") + " from:" + Environment.NewLine + currentSourceDictionary.Path + Environment.NewLine + Environment.NewLine +
                "NVDA Sync will create a backup beside the dictionary before changing it.",
                "Delete speech dictionary entries",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (confirmation != DialogResult.OK)
            {
                return;
            }
            AddLog("Requested delete from " + currentSourceDictionary.Path + ": " + FormatCount(indices.Count, "entry", "entries"));
            var relaunchNvda = LiveNvdaGuard.PrepareForWrites(this, new[] { currentSourceDictionary.Path }, "speech dictionary", AddLog);
            if (relaunchNvda == null)
            {
                AddLog("Delete cancelled.");
                FocusLog();
                return;
            }
            try
            {
                SpeechDictionaryFileService.DeleteEntries(currentSourceDictionary.Path, indices);
            }
            catch (Exception ex)
            {
                AddLog("Delete failed: " + ex.Message);
            }
            LiveNvdaGuard.Relaunch(relaunchNvda, AddLog);
            SourceDictionaryChanged(false);
            FocusLog();
        }

        private void CopyOrMoveSelectedEntries(bool move)
        {
            var indices = SelectedEntryIndices();
            if (indices.Count == 0)
            {
                AddLog("Choose one or more entries first.");
                FocusLog();
                return;
            }
            if (currentSourceDictionary == null)
            {
                AddLog("Choose a source dictionary first.");
                FocusLog();
                return;
            }
            var destinationPath = DestinationDictionaryPath();
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                AddLog("Choose a destination dictionary first.");
                FocusLog();
                return;
            }
            var message = new StringBuilder();
            message.AppendLine((move ? "Move " : "Copy ") + FormatCount(indices.Count, "selected dictionary entry", "selected dictionary entries") + ".");
            message.AppendLine();
            message.AppendLine("Source:");
            message.AppendLine(currentSourceDictionary.Path);
            message.AppendLine();
            message.AppendLine("Destination:");
            message.AppendLine(destinationPath);
            message.AppendLine();
            message.AppendLine(move
                ? "Move writes the entries to the destination, then removes them from the source."
                : "Copy leaves the source dictionary unchanged.");
            message.AppendLine("Duplicate patterns are allowed in NVDA dictionaries, so matching entries are appended rather than blocked.");
            message.AppendLine("NVDA Sync will create backups beside existing dictionaries before changing them.");
            var confirmation = MessageBox.Show(this, message.ToString(), (move ? "Move" : "Copy") + " speech dictionary entries", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirmation != DialogResult.OK)
            {
                return;
            }
            AddLog("Requested " + (move ? "move" : "copy") + " from " + currentSourceDictionary.Path + " to " + destinationPath + ": " + FormatCount(indices.Count, "entry", "entries"));
            var pathsToWrite = move ? new[] { currentSourceDictionary.Path, destinationPath } : new[] { destinationPath };
            var relaunchNvda = LiveNvdaGuard.PrepareForWrites(this, pathsToWrite, "speech dictionary", AddLog);
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
                    SpeechDictionaryFileService.MoveEntries(currentSourceDictionary.Path, destinationPath, indices);
                }
                else
                {
                    SpeechDictionaryFileService.CopyEntries(currentSourceDictionary.Path, destinationPath, indices);
                }
            }
            catch (Exception ex)
            {
                AddLog((move ? "Move" : "Copy") + " failed: " + ex.Message);
            }
            LiveNvdaGuard.Relaunch(relaunchNvda, AddLog);
            SourceDictionaryChanged(false);
            FocusLog();
        }

        private List<int> SelectedEntryIndices()
        {
            var indices = new List<int>();
            foreach (ListViewItem item in entriesListView.SelectedItems)
            {
                var entry = item.Tag as SpeechDictionaryEntry;
                if (entry != null)
                {
                    indices.Add(entry.Index);
                }
            }
            return indices;
        }

        private string DestinationFolder()
        {
            var option = destinationLocationComboBox.SelectedItem as FolderLocationOption;
            return option == null ? string.Empty : option.Folder;
        }

        private string DestinationDictionaryPath()
        {
            var option = destinationDictionaryComboBox.SelectedItem as DictionaryDestinationOption;
            return option == null ? string.Empty : option.Path;
        }

        private static string FormatCount(int count, string singular, string plural)
        {
            return count + " " + (count == 1 ? singular : plural);
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
            MainForm.AppendMainLog("[Speech dictionaries] " + message);
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
