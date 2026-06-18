using System;
using System.Drawing;
using System.Windows.Forms;

namespace NvdaAddonSync
{
    internal sealed class PreferencesForm : Form
    {
        private readonly AppSettings settings;
        private readonly Action preferencesChanged;
        private readonly TabControl tabs;
        private readonly CheckedListBox componentsListBox;
        private readonly CheckBox autoSyncCheckBox;
        private readonly CheckBox deleteStaleCheckBox;
        private readonly CheckBox excludePythonCacheCheckBox;
        private readonly CheckBox runAtStartupCheckBox;
        private readonly CheckBox startMinimizedCheckBox;
        private readonly ComboBox updateCheckFrequencyComboBox;
        private readonly CheckBox installUpdatesSilentlyCheckBox;
        private bool loading;

        public PreferencesForm(AppSettings settings, Action preferencesChanged)
        {
            this.settings = settings;
            this.preferencesChanged = preferencesChanged;
            Text = "NVDA Sync Preferences";
            StartPosition = FormStartPosition.CenterParent;
            Width = 620;
            Height = 440;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.Padding = new Padding(10);
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.SelectedIndexChanged += delegate
            {
                if (!loading)
                {
                    settings.LastPreferencesTab = tabs.SelectedIndex;
                    ApplyNow();
                }
            };
            root.Controls.Add(tabs, 0, 0);

            var syncPage = new TabPage("Sync");
            tabs.TabPages.Add(syncPage);

            var syncLayout = new TableLayoutPanel();
            syncLayout.Dock = DockStyle.Fill;
            syncLayout.ColumnCount = 1;
            syncLayout.RowCount = 4;
            syncLayout.Padding = new Padding(8);
            syncLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            syncLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            syncLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            syncLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            syncPage.Controls.Add(syncLayout);

            var componentsLabel = new Label();
            componentsLabel.AutoSize = true;
            componentsLabel.Text = "&Components to sync";
            syncLayout.Controls.Add(componentsLabel, 0, 0);

            componentsListBox = new CheckedListBox();
            componentsListBox.Dock = DockStyle.Fill;
            componentsListBox.CheckOnClick = true;
            componentsListBox.AccessibleName = "Components to sync";
            foreach (var component in SyncComponent.All)
            {
                componentsListBox.Items.Add(component);
            }
            componentsListBox.ItemCheck += delegate
            {
                if (!loading && IsHandleCreated)
                {
                    BeginInvoke(new Action(ApplyComponentSelection));
                }
            };
            syncLayout.Controls.Add(componentsListBox, 0, 1);

            autoSyncCheckBox = new CheckBox();
            autoSyncCheckBox.Text = "&Watch primary folder and sync when it changes";
            autoSyncCheckBox.AutoSize = true;
            autoSyncCheckBox.CheckedChanged += delegate { ApplyNow(); };
            syncLayout.Controls.Add(autoSyncCheckBox, 0, 2);

            deleteStaleCheckBox = new CheckBox();
            deleteStaleCheckBox.Text = "&Delete stale items from secondary folders";
            deleteStaleCheckBox.AutoSize = true;
            deleteStaleCheckBox.CheckedChanged += delegate { ApplyNow(); };
            syncLayout.Controls.Add(deleteStaleCheckBox, 0, 3);

            var filesPage = new TabPage("Files");
            tabs.TabPages.Add(filesPage);

            var filesLayout = new FlowLayoutPanel();
            filesLayout.Dock = DockStyle.Fill;
            filesLayout.FlowDirection = FlowDirection.TopDown;
            filesLayout.Padding = new Padding(8);
            filesLayout.WrapContents = false;
            filesPage.Controls.Add(filesLayout);

            excludePythonCacheCheckBox = new CheckBox();
            excludePythonCacheCheckBox.Text = "E&xclude Python cache files";
            excludePythonCacheCheckBox.AutoSize = true;
            excludePythonCacheCheckBox.CheckedChanged += delegate { ApplyNow(); };
            filesLayout.Controls.Add(excludePythonCacheCheckBox);

            var startupPage = new TabPage("Startup and updates");
            tabs.TabPages.Add(startupPage);

            var startupLayout = new FlowLayoutPanel();
            startupLayout.Dock = DockStyle.Fill;
            startupLayout.FlowDirection = FlowDirection.TopDown;
            startupLayout.Padding = new Padding(8);
            startupLayout.WrapContents = false;
            startupPage.Controls.Add(startupLayout);

            runAtStartupCheckBox = new CheckBox();
            runAtStartupCheckBox.Text = "Run NVDA Sync at Windows &startup";
            runAtStartupCheckBox.AutoSize = true;
            runAtStartupCheckBox.CheckedChanged += delegate { ApplyNow(); };
            startupLayout.Controls.Add(runAtStartupCheckBox);

            startMinimizedCheckBox = new CheckBox();
            startMinimizedCheckBox.Text = "Start &minimized to the notification area";
            startMinimizedCheckBox.AutoSize = true;
            startMinimizedCheckBox.CheckedChanged += delegate { ApplyNow(); };
            startupLayout.Controls.Add(startMinimizedCheckBox);

            var updateLabel = new Label();
            updateLabel.AutoSize = true;
            updateLabel.Text = "Check for &updates";
            startupLayout.Controls.Add(updateLabel);

            updateCheckFrequencyComboBox = new ComboBox();
            updateCheckFrequencyComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            updateCheckFrequencyComboBox.Width = 220;
            updateCheckFrequencyComboBox.AccessibleName = "Check for updates";
            updateCheckFrequencyComboBox.Items.AddRange(new object[] { "Never", "At startup", "Hourly", "Daily" });
            updateCheckFrequencyComboBox.SelectedIndexChanged += delegate { ApplyNow(); };
            startupLayout.Controls.Add(updateCheckFrequencyComboBox);

            installUpdatesSilentlyCheckBox = new CheckBox();
            installUpdatesSilentlyCheckBox.Text = "&Install updates silently when possible";
            installUpdatesSilentlyCheckBox.AutoSize = true;
            installUpdatesSilentlyCheckBox.CheckedChanged += delegate { ApplyNow(); };
            startupLayout.Controls.Add(installUpdatesSilentlyCheckBox);

            var updateNote = new Label();
            updateNote.AutoSize = true;
            updateNote.MaximumSize = new Size(540, 0);
            updateNote.Text = "Silent installs only run when a GitHub release contains an NVDA Sync ZIP package. Settings and logs are preserved.";
            startupLayout.Controls.Add(updateNote);

            var buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.AutoSize = true;
            buttons.Padding = new Padding(0, 8, 0, 0);
            root.Controls.Add(buttons, 0, 1);

            var closeButton = new Button();
            closeButton.Text = "Close";
            closeButton.AutoSize = true;
            closeButton.Click += delegate { Close(); };
            buttons.Controls.Add(closeButton);
            AcceptButton = closeButton;
            CancelButton = closeButton;

            LoadSettings();
            KeyDown += OnKeyDown;
        }

        private void LoadSettings()
        {
            loading = true;
            SetComponentChecked(SyncComponent.Addons, settings.SyncAddons);
            SetComponentChecked(SyncComponent.InputGestures, settings.SyncInputGestures);
            SetComponentChecked(SyncComponent.NvdaIni, settings.SyncNvdaIni);
            SetComponentChecked(SyncComponent.SpeechDictionaries, settings.SyncSpeechDictionaries);
            SetComponentChecked(SyncComponent.ConfigProfiles, settings.SyncConfigProfiles);
            SetComponentChecked(SyncComponent.OtherConfigFiles, settings.SyncOtherConfigFiles);
            SetComponentChecked(SyncComponent.OtherConfigFolders, settings.SyncOtherConfigFolders);
            autoSyncCheckBox.Checked = settings.AutoSync;
            deleteStaleCheckBox.Checked = settings.DeleteStaleItems;
            excludePythonCacheCheckBox.Checked = settings.ExcludePythonCache;
            runAtStartupCheckBox.Checked = settings.RunAtStartup;
            startMinimizedCheckBox.Checked = settings.StartMinimized;
            updateCheckFrequencyComboBox.SelectedItem = DisplayUpdateFrequency(settings.UpdateCheckFrequency);
            installUpdatesSilentlyCheckBox.Checked = settings.InstallUpdatesSilently;
            if (settings.LastPreferencesTab >= 0 && settings.LastPreferencesTab < tabs.TabPages.Count)
            {
                tabs.SelectedIndex = settings.LastPreferencesTab;
            }
            loading = false;
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

        private void ApplyComponentSelection()
        {
            if (loading)
            {
                return;
            }
            settings.SyncAddons = IsComponentChecked(SyncComponent.Addons);
            settings.SyncInputGestures = IsComponentChecked(SyncComponent.InputGestures);
            settings.SyncNvdaIni = IsComponentChecked(SyncComponent.NvdaIni);
            settings.SyncSpeechDictionaries = IsComponentChecked(SyncComponent.SpeechDictionaries);
            settings.SyncConfigProfiles = IsComponentChecked(SyncComponent.ConfigProfiles);
            settings.SyncOtherConfigFiles = IsComponentChecked(SyncComponent.OtherConfigFiles);
            settings.SyncOtherConfigFolders = IsComponentChecked(SyncComponent.OtherConfigFolders);
            if (SyncComponent.FromSettings(settings).Count == 0)
            {
                settings.SyncAddons = true;
                SetComponentChecked(SyncComponent.Addons, true);
            }
            ApplyNow();
        }

        private void ApplyNow()
        {
            if (loading)
            {
                return;
            }
            settings.AutoSync = autoSyncCheckBox.Checked;
            settings.DeleteStaleItems = deleteStaleCheckBox.Checked;
            settings.ExcludePythonCache = excludePythonCacheCheckBox.Checked;
            settings.RunAtStartup = runAtStartupCheckBox.Checked;
            settings.StartMinimized = startMinimizedCheckBox.Checked;
            settings.UpdateCheckFrequency = StoredUpdateFrequency(Convert.ToString(updateCheckFrequencyComboBox.SelectedItem));
            settings.InstallUpdatesSilently = installUpdatesSilentlyCheckBox.Checked;
            settings.LastPreferencesTab = tabs.SelectedIndex;
            if (preferencesChanged != null)
            {
                preferencesChanged();
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
                e.Handled = true;
                return;
            }

            if (e.Control)
            {
                var tabNumber = e.KeyCode - Keys.D1;
                if (tabNumber >= 0 && tabNumber < tabs.TabPages.Count)
                {
                    tabs.SelectedIndex = tabNumber;
                    e.Handled = true;
                }
            }
        }

        private static string DisplayUpdateFrequency(string value)
        {
            var stored = StoredUpdateFrequency(value);
            if (string.Equals(stored, "Startup", StringComparison.OrdinalIgnoreCase)) return "At startup";
            if (string.Equals(stored, "Hourly", StringComparison.OrdinalIgnoreCase)) return "Hourly";
            if (string.Equals(stored, "Daily", StringComparison.OrdinalIgnoreCase)) return "Daily";
            return "Never";
        }

        private static string StoredUpdateFrequency(string value)
        {
            return AppSettings.NormalizeUpdateCheckFrequency(value);
        }
    }
}
