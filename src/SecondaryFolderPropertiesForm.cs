using System;
using System.Drawing;
using System.Windows.Forms;

namespace NvdaAddonSync
{
    internal sealed class SecondaryFolderPropertiesForm : Form
    {
        private readonly SecondaryFolderProfile profile;
        private readonly AppSettings settings;
        private readonly CheckBox useDefaultsCheckBox;
        private readonly CheckedListBox componentsListBox;
        private readonly ComboBox addonModeComboBox;
        private readonly CheckBox syncProgramFilesCheckBox;
        private readonly CheckBox createProgramBackupsCheckBox;
        private readonly CheckBox deleteStaleCheckBox;
        private readonly CheckBox excludePythonCacheCheckBox;
        private readonly CheckBox excludeLogFilesCheckBox;
        private bool loading;

        public SecondaryFolderPropertiesForm(SecondaryFolderProfile profile, AppSettings settings)
        {
            this.profile = profile;
            this.settings = settings;
            Text = "Secondary Folder Properties";
            StartPosition = FormStartPosition.CenterParent;
            Width = 640;
            Height = 520;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 8;
            root.Padding = new Padding(10);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var pathLabel = new Label();
            pathLabel.AutoSize = true;
            pathLabel.Text = "Secondary folder";
            root.Controls.Add(pathLabel, 0, 0);

            var pathTextBox = new TextBox();
            pathTextBox.ReadOnly = true;
            pathTextBox.Dock = DockStyle.Top;
            pathTextBox.Text = profile.Path;
            pathTextBox.AccessibleName = "Secondary folder";
            root.Controls.Add(pathTextBox, 0, 1);

            useDefaultsCheckBox = new CheckBox();
            useDefaultsCheckBox.Text = "Use &global defaults";
            useDefaultsCheckBox.AutoSize = true;
            useDefaultsCheckBox.CheckedChanged += delegate { ApplyEnabledState(); SaveToProfile(); };
            root.Controls.Add(useDefaultsCheckBox, 0, 2);

            componentsListBox = new CheckedListBox();
            componentsListBox.Dock = DockStyle.Fill;
            componentsListBox.CheckOnClick = true;
            componentsListBox.AccessibleName = "Components for this secondary folder";
            foreach (var component in SyncComponent.All)
            {
                componentsListBox.Items.Add(component);
            }
            componentsListBox.ItemCheck += delegate
            {
                if (!loading && IsHandleCreated)
                {
                    BeginInvoke(new Action(SaveToProfile));
                }
            };
            root.Controls.Add(componentsListBox, 0, 3);

            var addonLabel = new Label();
            addonLabel.AutoSize = true;
            addonLabel.Text = "Add-on &mode";
            root.Controls.Add(addonLabel, 0, 4);

            addonModeComboBox = new ComboBox();
            addonModeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            addonModeComboBox.Width = 280;
            addonModeComboBox.AccessibleName = "Add-on mode";
            addonModeComboBox.Items.Add(AddonSyncMode.DisplayName(AddonSyncMode.All));
            addonModeComboBox.Items.Add(AddonSyncMode.DisplayName(AddonSyncMode.ExistingOnly));
            addonModeComboBox.Items.Add(AddonSyncMode.DisplayName(AddonSyncMode.None));
            addonModeComboBox.SelectedIndexChanged += delegate { SaveToProfile(); };
            root.Controls.Add(addonModeComboBox, 0, 5);

            var optionsPanel = new FlowLayoutPanel();
            optionsPanel.Dock = DockStyle.Fill;
            optionsPanel.FlowDirection = FlowDirection.TopDown;
            optionsPanel.WrapContents = false;
            root.Controls.Add(optionsPanel, 0, 6);

            syncProgramFilesCheckBox = new CheckBox();
            syncProgramFilesCheckBox.Text = "Update portable NVDA &program files";
            syncProgramFilesCheckBox.AutoSize = true;
            syncProgramFilesCheckBox.CheckedChanged += delegate { SaveToProfile(); };
            optionsPanel.Controls.Add(syncProgramFilesCheckBox);
            createProgramBackupsCheckBox = new CheckBox();
            createProgramBackupsCheckBox.Text = "Create &backup ZIP before program-file updates";
            createProgramBackupsCheckBox.AutoSize = true;
            createProgramBackupsCheckBox.CheckedChanged += delegate { SaveToProfile(); };
            optionsPanel.Controls.Add(createProgramBackupsCheckBox);

            deleteStaleCheckBox = new CheckBox();
            deleteStaleCheckBox.Text = "&Delete stale items for this secondary folder";
            deleteStaleCheckBox.AutoSize = true;
            deleteStaleCheckBox.CheckedChanged += delegate { SaveToProfile(); };
            optionsPanel.Controls.Add(deleteStaleCheckBox);

            excludePythonCacheCheckBox = new CheckBox();
            excludePythonCacheCheckBox.Text = "E&xclude Python cache files";
            excludePythonCacheCheckBox.AutoSize = true;
            excludePythonCacheCheckBox.CheckedChanged += delegate { SaveToProfile(); };
            optionsPanel.Controls.Add(excludePythonCacheCheckBox);

            excludeLogFilesCheckBox = new CheckBox();
            excludeLogFilesCheckBox.Text = "Exclude &log files";
            excludeLogFilesCheckBox.AutoSize = true;
            excludeLogFilesCheckBox.CheckedChanged += delegate { SaveToProfile(); };
            optionsPanel.Controls.Add(excludeLogFilesCheckBox);

            var buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.AutoSize = true;
            buttons.Padding = new Padding(0, 8, 0, 0);
            root.Controls.Add(buttons, 0, 7);

            var closeButton = new Button();
            closeButton.Text = "Close";
            closeButton.AutoSize = true;
            closeButton.Click += delegate { Close(); };
            buttons.Controls.Add(closeButton);
            AcceptButton = closeButton;
            CancelButton = closeButton;

            LoadProfile();
            KeyDown += OnKeyDown;
        }

        private void LoadProfile()
        {
            loading = true;
            profile.Normalize();
            useDefaultsCheckBox.Checked = profile.UseGlobalDefaults;
            SetComponentChecked(SyncComponent.Addons, profile.SyncAddons);
            SetComponentChecked(SyncComponent.InputGestures, profile.SyncInputGestures);
            SetComponentChecked(SyncComponent.NvdaIni, profile.SyncNvdaIni);
            SetComponentChecked(SyncComponent.SpeechDictionaries, profile.SyncSpeechDictionaries);
            SetComponentChecked(SyncComponent.ConfigProfiles, profile.SyncConfigProfiles);
            SetComponentChecked(SyncComponent.OtherConfigFiles, profile.SyncOtherConfigFiles);
            SetComponentChecked(SyncComponent.OtherConfigFolders, profile.SyncOtherConfigFolders);
            addonModeComboBox.SelectedItem = AddonSyncMode.DisplayName(profile.AddonMode);
            syncProgramFilesCheckBox.Checked = profile.SyncNvdaProgramFiles;
            createProgramBackupsCheckBox.Checked = profile.CreateProgramBackups;
            deleteStaleCheckBox.Checked = profile.DeleteStaleItems;
            excludePythonCacheCheckBox.Checked = profile.ExcludePythonCache;
            excludeLogFilesCheckBox.Checked = profile.ExcludeLogFiles;
            loading = false;
            ApplyEnabledState();
        }

        private void SaveToProfile()
        {
            if (loading)
            {
                return;
            }
            profile.UseGlobalDefaults = useDefaultsCheckBox.Checked;
            profile.SyncAddons = IsComponentChecked(SyncComponent.Addons);
            profile.SyncInputGestures = IsComponentChecked(SyncComponent.InputGestures);
            profile.SyncNvdaIni = IsComponentChecked(SyncComponent.NvdaIni);
            profile.SyncSpeechDictionaries = IsComponentChecked(SyncComponent.SpeechDictionaries);
            profile.SyncConfigProfiles = IsComponentChecked(SyncComponent.ConfigProfiles);
            profile.SyncOtherConfigFiles = IsComponentChecked(SyncComponent.OtherConfigFiles);
            profile.SyncOtherConfigFolders = IsComponentChecked(SyncComponent.OtherConfigFolders);
            profile.AddonMode = StoredAddonMode(Convert.ToString(addonModeComboBox.SelectedItem));
            profile.SyncNvdaProgramFiles = syncProgramFilesCheckBox.Checked;
            profile.CreateProgramBackups = createProgramBackupsCheckBox.Checked;
            profile.DeleteStaleItems = deleteStaleCheckBox.Checked;
            profile.ExcludePythonCache = excludePythonCacheCheckBox.Checked;
            profile.ExcludeLogFiles = excludeLogFilesCheckBox.Checked;
            profile.Normalize();
        }

        private void ApplyEnabledState()
        {
            var enabled = !useDefaultsCheckBox.Checked;
            componentsListBox.Enabled = enabled;
            addonModeComboBox.Enabled = enabled;
            syncProgramFilesCheckBox.Enabled = enabled;
            createProgramBackupsCheckBox.Enabled = enabled;
            deleteStaleCheckBox.Enabled = enabled;
            excludePythonCacheCheckBox.Enabled = enabled;
            excludeLogFilesCheckBox.Enabled = enabled;
            if (useDefaultsCheckBox.Checked)
            {
                var text = "This secondary folder uses the global defaults: " + AddonSyncMode.DisplayName(settings.DefaultAddonMode);
                if (settings.SyncNvdaProgramFiles)
                {
                    text += ", portable program files enabled";
                }
                AccessibleDescription = text;
            }
        }

        private void SetComponentChecked(SyncComponent component, bool isChecked)
        {
            var index = componentsListBox.Items.IndexOf(component);
            if (index >= 0)
            {
                componentsListBox.SetItemChecked(index, isChecked);
            }
        }

        private bool IsComponentChecked(SyncComponent component)
        {
            var index = componentsListBox.Items.IndexOf(component);
            return index >= 0 && componentsListBox.GetItemChecked(index);
        }

        private static string StoredAddonMode(string value)
        {
            if (string.Equals(value, AddonSyncMode.DisplayName(AddonSyncMode.ExistingOnly), StringComparison.OrdinalIgnoreCase)) return AddonSyncMode.ExistingOnly;
            if (string.Equals(value, AddonSyncMode.DisplayName(AddonSyncMode.None), StringComparison.OrdinalIgnoreCase)) return AddonSyncMode.None;
            return AddonSyncMode.All;
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
