using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NvdaAddonSync
{
    internal sealed class PendingAddonWorkForm : Form
    {
        private readonly AppSettings settings;
        private readonly ComboBox locationComboBox;
        private readonly CheckedListBox itemsListBox;
        private readonly TextBox detailsTextBox;
        private readonly TextBox logTextBox;
        private readonly Button clearButton;
        private string currentFolder;
        private bool loadingLocations;

        public PendingAddonWorkForm(AppSettings settings, string primaryFolder)
        {
            this.settings = settings;
            Text = "Review Pending Add-on Work";
            StartPosition = FormStartPosition.CenterParent;
            Width = 850;
            Height = 580;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 10,
                Padding = new Padding(10)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var explanation = new TextBox
            {
                Dock = DockStyle.Top,
                Multiline = true,
                ReadOnly = true,
                Height = 62,
                Text = "Review add-on installations or removals which NVDA has left waiting. Genuine work must be completed by restarting that NVDA copy. Only removal entries proven stale can be cleared; a backup is made first.",
                AccessibleName = "Pending add-on work explanation"
            };
            root.Controls.Add(explanation, 0, 0);

            var locationLabel = new Label { AutoSize = true, Text = "&NVDA folder" };
            root.Controls.Add(locationLabel, 0, 1);

            var locationPanel = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1 };
            locationPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            locationPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.Controls.Add(locationPanel, 0, 2);

            locationComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = "NVDA folder"
            };
            locationComboBox.SelectionChangeCommitted += delegate { OnLocationSelectionChanged(); };
            locationPanel.Controls.Add(locationComboBox, 0, 0);

            var browseButton = new Button { AutoSize = true, Text = "&Browse..." };
            browseButton.Click += delegate { BrowseForFolder(); };
            locationPanel.Controls.Add(browseButton, 1, 0);

            var itemsLabel = new Label { AutoSize = true, Text = "&Pending add-on items" };
            root.Controls.Add(itemsLabel, 0, 3);

            itemsListBox = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                AccessibleName = "Pending add-on items"
            };
            itemsListBox.SelectedIndexChanged += delegate { UpdateDetails(); };
            itemsListBox.ItemCheck += OnItemCheck;
            root.Controls.Add(itemsListBox, 0, 4);

            var detailsLabel = new Label { AutoSize = true, Text = "&Details" };
            root.Controls.Add(detailsLabel, 0, 5);

            detailsTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                AccessibleName = "Selected pending add-on item details"
            };
            root.Controls.Add(detailsTextBox, 0, 6);

            var logLabel = new Label { AutoSize = true, Text = "Review &log" };
            root.Controls.Add(logLabel, 0, 7);

            logTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                AccessibleName = "Pending add-on work log"
            };
            root.Controls.Add(logTextBox, 0, 8);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(0, 8, 0, 0)
            };
            root.Controls.Add(buttons, 0, 9);

            var closeButton = new Button { AutoSize = true, Text = "Close" };
            closeButton.Click += delegate { Close(); };
            buttons.Controls.Add(closeButton);

            var refreshButton = new Button { AutoSize = true, Text = "&Refresh" };
            refreshButton.Click += delegate { InspectCurrentFolder(true); };
            buttons.Controls.Add(refreshButton);

            clearButton = new Button { AutoSize = true, Text = "&Clear selected stale entries", Enabled = false };
            clearButton.Click += delegate { ClearSelectedEntries(); };
            buttons.Controls.Add(clearButton);

            var selectButton = new Button { AutoSize = true, Text = "&Select stale entries" };
            selectButton.Click += delegate { SelectStaleEntries(); };
            buttons.Controls.Add(selectButton);

            CancelButton = closeButton;
            LoadLocations(primaryFolder);
            InspectCurrentFolder(true);
            Shown += delegate { explanation.Focus(); };
        }

        private sealed class LocationOption
        {
            public string Label { get; set; }
            public string Folder { get; set; }
            public override string ToString() { return Label; }
        }

        private void LoadLocations(string primaryFolder)
        {
            loadingLocations = true;
            try
            {
                AddLocation("Primary", primaryFolder);
                for (var index = 0; index < settings.SecondaryFolderProfiles.Count; index++)
                {
                    var profile = settings.SecondaryFolderProfiles[index];
                    if (profile != null)
                    {
                        AddLocation("Secondary folder " + (index + 1), profile.Path);
                    }
                }
                if (locationComboBox.Items.Count > 0)
                {
                    locationComboBox.SelectedIndex = 0;
                    currentFolder = ((LocationOption)locationComboBox.SelectedItem).Folder;
                }
            }
            finally
            {
                loadingLocations = false;
            }
        }

        private void AddLocation(string label, string folder)
        {
            if (!string.IsNullOrWhiteSpace(folder))
            {
                locationComboBox.Items.Add(new LocationOption { Label = label + ": " + folder, Folder = folder });
            }
        }

        private void OnLocationSelectionChanged()
        {
            if (loadingLocations)
            {
                return;
            }
            var keepFocus = locationComboBox.Focused;
            var option = locationComboBox.SelectedItem as LocationOption;
            if (option != null)
            {
                currentFolder = option.Folder;
                InspectCurrentFolder(true);
            }
            if (keepFocus)
            {
                BeginInvoke(new Action(delegate { if (!IsDisposed && locationComboBox.CanFocus) locationComboBox.Focus(); }));
            }
        }

        private void BrowseForFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose NVDA folder to review";
                dialog.ShowNewFolderButton = false;
                if (!string.IsNullOrWhiteSpace(currentFolder) && Directory.Exists(currentFolder))
                {
                    dialog.SelectedPath = currentFolder;
                }
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                loadingLocations = true;
                try
                {
                    var option = new LocationOption { Label = "Custom folder: " + dialog.SelectedPath, Folder = dialog.SelectedPath };
                    locationComboBox.Items.Add(option);
                    locationComboBox.SelectedItem = option;
                    currentFolder = option.Folder;
                }
                finally
                {
                    loadingLocations = false;
                }
                InspectCurrentFolder(true);
            }
        }

        private void InspectCurrentFolder(bool selectFirst)
        {
            itemsListBox.Items.Clear();
            detailsTextBox.Clear();
            clearButton.Enabled = false;
            try
            {
                currentFolder = SyncEngine.ResolveNvdaConfigDirectory(currentFolder ?? string.Empty, "NVDA folder", true);
                locationComboBox.AccessibleDescription = currentFolder;
                var report = NvdaAddonStateService.Inspect(currentFolder);
                foreach (var item in report.Items)
                {
                    itemsListBox.Items.Add(item, false);
                }
                if (itemsListBox.Items.Count == 0)
                {
                    AddLog("No pending add-on work was found in " + currentFolder);
                }
                else
                {
                    AddLog("Found " + itemsListBox.Items.Count + " pending add-on item(s) in " + currentFolder);
                    if (selectFirst)
                    {
                        itemsListBox.SelectedIndex = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("Could not inspect NVDA folder: " + ex.Message);
            }
        }

        private void UpdateDetails()
        {
            var item = itemsListBox.SelectedItem as PendingAddonWorkItem;
            detailsTextBox.Text = item == null ? string.Empty : item.Details;
            detailsTextBox.SelectionStart = 0;
            detailsTextBox.SelectionLength = 0;
        }

        private void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            var item = itemsListBox.Items[e.Index] as PendingAddonWorkItem;
            if (e.NewValue == CheckState.Checked && (item == null || !item.CanRepair))
            {
                e.NewValue = CheckState.Unchecked;
                detailsTextBox.Text = item == null ? string.Empty : item.Details;
            }
            BeginInvoke(new Action(UpdateClearButton));
        }

        private void UpdateClearButton()
        {
            clearButton.Enabled = SelectedStaleItems().Count > 0;
        }

        private void SelectStaleEntries()
        {
            for (var index = 0; index < itemsListBox.Items.Count; index++)
            {
                var item = itemsListBox.Items[index] as PendingAddonWorkItem;
                itemsListBox.SetItemChecked(index, item != null && item.CanRepair);
            }
            UpdateClearButton();
        }

        private List<PendingAddonWorkItem> SelectedStaleItems()
        {
            var selected = new List<PendingAddonWorkItem>();
            foreach (var checkedItem in itemsListBox.CheckedItems)
            {
                var item = checkedItem as PendingAddonWorkItem;
                if (item != null && item.CanRepair)
                {
                    selected.Add(item);
                }
            }
            return selected;
        }

        private void ClearSelectedEntries()
        {
            var selected = SelectedStaleItems();
            if (selected.Count == 0)
            {
                return;
            }
            var names = new List<string>();
            foreach (var item in selected)
            {
                names.Add(item.AddonName);
            }
            var message = "Clear these proven stale add-on removal entries?" + Environment.NewLine + Environment.NewLine
                + string.Join(Environment.NewLine, names.ToArray()) + Environment.NewLine + Environment.NewLine
                + "A timestamped backup of addonsState.json will be created first.";
            if (MessageBox.Show(this, message, "Clear stale add-on state", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            {
                return;
            }

            var statePath = Path.Combine(currentFolder, "addonsState.json");
            var relaunch = LiveNvdaGuard.PrepareForWrites(this, new[] { statePath }, "add-on state", AddLog);
            if (relaunch == null)
            {
                AddLog("Pending add-on state repair cancelled; no changes were made.");
                return;
            }
            try
            {
                AddLog("Clearing selected stale add-on removal state from " + statePath);
                var result = NvdaAddonStateService.ClearStaleRemovals(currentFolder, selected);
                foreach (var removed in result.RemovedEntries)
                {
                    AddLog("Cleared " + removed);
                }
                AddLog("Backup created: " + result.BackupPath);
                AddLog("Pending add-on state repair completed successfully.");
                InspectCurrentFolder(true);
            }
            catch (Exception ex)
            {
                AddLog("Pending add-on state repair failed: " + ex.Message);
                MessageBox.Show(this, "The stale state was not cleared." + Environment.NewLine + Environment.NewLine + ex.Message, "Pending add-on state", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                LiveNvdaGuard.Relaunch(relaunch, AddLog);
            }
        }

        private void AddLog(string message)
        {
            var line = DateTime.Now.ToString("HH:mm:ss") + " " + message;
            if (logTextBox.TextLength > 0)
            {
                logTextBox.AppendText(Environment.NewLine);
            }
            logTextBox.AppendText(line);
            logTextBox.SelectionStart = logTextBox.TextLength;
            logTextBox.ScrollToCaret();
            MainForm.AppendMainLog("[Pending add-on work] " + message);
        }
    }
}
