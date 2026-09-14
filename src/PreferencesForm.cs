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
        private readonly ComboBox addonModeComboBox;
        private readonly CheckBox syncProgramFilesCheckBox;
        private readonly CheckBox createProgramBackupsCheckBox;
        private readonly CheckBox autoSyncCheckBox;
        private readonly NumericUpDown unavailableSecondaryRetryMinutesNumeric;
        private readonly CheckBox deleteStaleCheckBox;
        private readonly CheckBox excludePythonCacheCheckBox;
        private readonly CheckBox excludeLogFilesCheckBox;
        private readonly CheckBox writeLogFileCheckBox;
        private readonly CheckBox runAtStartupCheckBox;
        private readonly CheckBox startMinimizedCheckBox;
        private readonly ComboBox updateCheckFrequencyComboBox;
        private readonly CheckBox installUpdatesSilentlyCheckBox;
        private readonly CheckBox playStatusSoundsCheckBox;
        private readonly CheckBox enableGlobalHotkeyCheckBox;
        private readonly TextBox showHideHotkeyTextBox;
        private readonly CheckBox warnBeforeSingleModifierHotkeysCheckBox;
        private readonly CheckBox enableToggleSyncHotkeyCheckBox;
        private readonly TextBox toggleSyncHotkeyTextBox;
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
            syncLayout.RowCount = 9;
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

            var addonModeLabel = new Label();
            addonModeLabel.AutoSize = true;
            addonModeLabel.Text = "&Add-on mode";
            syncLayout.Controls.Add(addonModeLabel, 0, 2);

            addonModeComboBox = new ComboBox();
            addonModeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            addonModeComboBox.Width = 280;
            addonModeComboBox.AccessibleName = "Add-on mode";
            addonModeComboBox.Items.Add(AddonSyncMode.DisplayName(AddonSyncMode.All));
            addonModeComboBox.Items.Add(AddonSyncMode.DisplayName(AddonSyncMode.ExistingOnly));
            addonModeComboBox.Items.Add(AddonSyncMode.DisplayName(AddonSyncMode.None));
            addonModeComboBox.SelectedIndexChanged += delegate { ApplyNow(); };
            syncLayout.Controls.Add(addonModeComboBox, 0, 3);

            syncProgramFilesCheckBox = new CheckBox();
            syncProgramFilesCheckBox.Text = "Portable NVDA &program file updates";
            syncProgramFilesCheckBox.AutoSize = true;
            syncProgramFilesCheckBox.CheckedChanged += delegate { ApplyNow(); };
            syncLayout.Controls.Add(syncProgramFilesCheckBox, 0, 4);
            createProgramBackupsCheckBox = new CheckBox();
            createProgramBackupsCheckBox.Text = "Create &backup ZIP before program-file updates";
            createProgramBackupsCheckBox.AutoSize = true;
            createProgramBackupsCheckBox.CheckedChanged += delegate { ApplyNow(); };
            syncLayout.Controls.Add(createProgramBackupsCheckBox, 0, 5);

            autoSyncCheckBox = new CheckBox();
            autoSyncCheckBox.Text = "&Watch primary folder and sync after changes settle";
            autoSyncCheckBox.AutoSize = true;
            autoSyncCheckBox.CheckedChanged += delegate { ApplyNow(); };
            syncLayout.Controls.Add(autoSyncCheckBox, 0, 6);

            var retryLayout = new FlowLayoutPanel();
            retryLayout.AutoSize = true;
            retryLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            retryLayout.WrapContents = false;
            retryLayout.Margin = Padding.Empty;
            var retryLabel = new Label();
            retryLabel.AutoSize = true;
            retryLabel.Text = "Unavailable folder &retry interval (minutes):";
            retryLabel.Margin = new Padding(0, 5, 6, 0);
            retryLayout.Controls.Add(retryLabel);
            unavailableSecondaryRetryMinutesNumeric = new NumericUpDown();
            unavailableSecondaryRetryMinutesNumeric.Minimum = AppSettings.MinimumUnavailableSecondaryRetryMinutes;
            unavailableSecondaryRetryMinutesNumeric.Maximum = AppSettings.MaximumUnavailableSecondaryRetryMinutes;
            unavailableSecondaryRetryMinutesNumeric.Width = 80;
            unavailableSecondaryRetryMinutesNumeric.AccessibleName = "Unavailable folder retry interval in minutes";
            unavailableSecondaryRetryMinutesNumeric.ValueChanged += delegate { ApplyNow(); };
            retryLayout.Controls.Add(unavailableSecondaryRetryMinutesNumeric);
            retryLabel.UseMnemonic = true;
            syncLayout.Controls.Add(retryLayout, 0, 7);

            deleteStaleCheckBox = new CheckBox();
            deleteStaleCheckBox.Text = "&Delete stale items from secondary folders";
            deleteStaleCheckBox.AutoSize = true;
            deleteStaleCheckBox.CheckedChanged += delegate { ApplyNow(); };
            syncLayout.Controls.Add(deleteStaleCheckBox, 0, 8);

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

            excludeLogFilesCheckBox = new CheckBox();
            excludeLogFilesCheckBox.Text = "Exclude &log files";
            excludeLogFilesCheckBox.AutoSize = true;
            excludeLogFilesCheckBox.CheckedChanged += delegate { ApplyNow(); };
            filesLayout.Controls.Add(excludeLogFilesCheckBox);

            writeLogFileCheckBox = new CheckBox();
            writeLogFileCheckBox.Text = "Write rolling log &file";
            writeLogFileCheckBox.AutoSize = true;
            writeLogFileCheckBox.CheckedChanged += delegate { ApplyNow(); };
            filesLayout.Controls.Add(writeLogFileCheckBox);

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

            var notificationsPage = new TabPage("Sounds and hotkeys");
            tabs.TabPages.Add(notificationsPage);

            var notificationsLayout = new FlowLayoutPanel();
            notificationsLayout.Dock = DockStyle.Fill;
            notificationsLayout.FlowDirection = FlowDirection.TopDown;
            notificationsLayout.Padding = new Padding(8);
            notificationsLayout.WrapContents = false;
            notificationsPage.Controls.Add(notificationsLayout);

            playStatusSoundsCheckBox = new CheckBox();
            playStatusSoundsCheckBox.Text = "Pla&y status sounds";
            playStatusSoundsCheckBox.AutoSize = true;
            playStatusSoundsCheckBox.CheckedChanged += delegate { ApplyNow(); };
            notificationsLayout.Controls.Add(playStatusSoundsCheckBox);

            enableGlobalHotkeyCheckBox = new CheckBox();
            enableGlobalHotkeyCheckBox.Text = "Enable &global show/hide hotkey";
            enableGlobalHotkeyCheckBox.AutoSize = true;
            enableGlobalHotkeyCheckBox.CheckedChanged += delegate
            {
                showHideHotkeyTextBox.Enabled = enableGlobalHotkeyCheckBox.Checked;
            };
            notificationsLayout.Controls.Add(enableGlobalHotkeyCheckBox);

            var hotkeyLayout = new FlowLayoutPanel();
            hotkeyLayout.AutoSize = true;
            hotkeyLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            hotkeyLayout.WrapContents = false;
            hotkeyLayout.Margin = Padding.Empty;
            var hotkeyLabel = new Label();
            hotkeyLabel.AutoSize = true;
            hotkeyLabel.Text = "Show/hide hot&key:";
            hotkeyLabel.Margin = new Padding(0, 5, 6, 0);
            hotkeyLayout.Controls.Add(hotkeyLabel);

            showHideHotkeyTextBox = new TextBox();
            showHideHotkeyTextBox.Width = 220;
            showHideHotkeyTextBox.ReadOnly = false;
            showHideHotkeyTextBox.ShortcutsEnabled = false;
            showHideHotkeyTextBox.AccessibleName = "Global show or hide hotkey";
            showHideHotkeyTextBox.AccessibleDescription = "Press a global key combination. Two modifiers are safest. One modifier is allowed only with function keys, Grave, or Backslash. Press Delete or Backspace to clear this hotkey. Modifier-only and unsafe Windows shortcuts are rejected.";
            showHideHotkeyTextBox.KeyDown += OnHotkeyKeyDown;
            showHideHotkeyTextBox.KeyPress += delegate(object sender, KeyPressEventArgs e) { e.Handled = true; };
            hotkeyLayout.Controls.Add(showHideHotkeyTextBox);
            notificationsLayout.Controls.Add(hotkeyLayout);

            enableToggleSyncHotkeyCheckBox = new CheckBox();
            enableToggleSyncHotkeyCheckBox.Text = "Enable global &toggle sync hotkey";
            enableToggleSyncHotkeyCheckBox.AutoSize = true;
            enableToggleSyncHotkeyCheckBox.CheckedChanged += delegate
            {
                toggleSyncHotkeyTextBox.Enabled = enableToggleSyncHotkeyCheckBox.Checked;
            };
            notificationsLayout.Controls.Add(enableToggleSyncHotkeyCheckBox);

            var toggleHotkeyLayout = new FlowLayoutPanel();
            toggleHotkeyLayout.AutoSize = true;
            toggleHotkeyLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            toggleHotkeyLayout.WrapContents = false;
            toggleHotkeyLayout.Margin = Padding.Empty;
            var toggleHotkeyLabel = new Label();
            toggleHotkeyLabel.AutoSize = true;
            toggleHotkeyLabel.Text = "Toggle sync h&otkey:";
            toggleHotkeyLabel.Margin = new Padding(0, 5, 6, 0);
            toggleHotkeyLayout.Controls.Add(toggleHotkeyLabel);

            toggleSyncHotkeyTextBox = new TextBox();
            toggleSyncHotkeyTextBox.Width = 220;
            toggleSyncHotkeyTextBox.ReadOnly = false;
            toggleSyncHotkeyTextBox.ShortcutsEnabled = false;
            toggleSyncHotkeyTextBox.AccessibleName = "Toggle automatic sync global hotkey";
            toggleSyncHotkeyTextBox.AccessibleDescription = showHideHotkeyTextBox.AccessibleDescription;
            toggleSyncHotkeyTextBox.KeyDown += OnHotkeyKeyDown;
            toggleSyncHotkeyTextBox.KeyPress += delegate(object sender, KeyPressEventArgs e) { e.Handled = true; };
            toggleHotkeyLayout.Controls.Add(toggleSyncHotkeyTextBox);
            notificationsLayout.Controls.Add(toggleHotkeyLayout);

            warnBeforeSingleModifierHotkeysCheckBox = new CheckBox();
            warnBeforeSingleModifierHotkeysCheckBox.Text = "Warn before sa&ving single-modifier global hotkeys";
            warnBeforeSingleModifierHotkeysCheckBox.AutoSize = true;
            warnBeforeSingleModifierHotkeysCheckBox.AccessibleDescription = "When checked, NVDA Sync warns before saving an allowed global hotkey that uses only one modifier.";
            notificationsLayout.Controls.Add(warnBeforeSingleModifierHotkeysCheckBox);

            var soundsNote = new Label();
            soundsNote.AutoSize = true;
            soundsNote.MaximumSize = new Size(540, 0);
            soundsNote.Text = "Custom WAV files in Settings\\sounds override bundled sounds with the same filenames.";
            notificationsLayout.Controls.Add(soundsNote);

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
            addonModeComboBox.SelectedItem = AddonSyncMode.DisplayName(settings.DefaultAddonMode);
            syncProgramFilesCheckBox.Checked = settings.SyncNvdaProgramFiles;
            createProgramBackupsCheckBox.Checked = settings.CreateProgramBackups;
            autoSyncCheckBox.Checked = settings.AutoSync;
            unavailableSecondaryRetryMinutesNumeric.Value = settings.UnavailableSecondaryRetryMinutes;
            unavailableSecondaryRetryMinutesNumeric.Enabled = settings.AutoSync;
            deleteStaleCheckBox.Checked = settings.DeleteStaleItems;
            excludePythonCacheCheckBox.Checked = settings.ExcludePythonCache;
            excludeLogFilesCheckBox.Checked = settings.ExcludeLogFiles;
            writeLogFileCheckBox.Checked = settings.WriteLogFile;
            runAtStartupCheckBox.Checked = settings.RunAtStartup;
            startMinimizedCheckBox.Checked = settings.StartMinimized;
            updateCheckFrequencyComboBox.SelectedItem = DisplayUpdateFrequency(settings.UpdateCheckFrequency);
            installUpdatesSilentlyCheckBox.Checked = settings.InstallUpdatesSilently;
            playStatusSoundsCheckBox.Checked = settings.PlayStatusSounds;
            enableGlobalHotkeyCheckBox.Checked = settings.EnableGlobalHotkey;
            showHideHotkeyTextBox.Text = settings.ShowHideHotkey;
            showHideHotkeyTextBox.Enabled = settings.EnableGlobalHotkey;
            warnBeforeSingleModifierHotkeysCheckBox.Checked = settings.WarnBeforeSingleModifierHotkeys;
            enableToggleSyncHotkeyCheckBox.Checked = settings.EnableToggleSyncHotkey;
            toggleSyncHotkeyTextBox.Text = settings.ToggleSyncHotkey;
            toggleSyncHotkeyTextBox.Enabled = settings.EnableToggleSyncHotkey;
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
            var showHotkey = showHideHotkeyTextBox.Text.Trim();
            var toggleHotkey = toggleSyncHotkeyTextBox.Text.Trim();
            GlobalHotkeyDefinition parsed;
            if ((enableGlobalHotkeyCheckBox.Checked && !GlobalHotkeyDefinition.TryParse(showHotkey, out parsed)) ||
                (enableToggleSyncHotkeyCheckBox.Checked && !GlobalHotkeyDefinition.TryParse(toggleHotkey, out parsed)) ||
                (enableGlobalHotkeyCheckBox.Checked && enableToggleSyncHotkeyCheckBox.Checked &&
                    string.Equals(showHotkey, toggleHotkey, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            settings.DefaultAddonMode = StoredAddonMode(Convert.ToString(addonModeComboBox.SelectedItem));
            settings.SyncNvdaProgramFiles = syncProgramFilesCheckBox.Checked;
            settings.CreateProgramBackups = createProgramBackupsCheckBox.Checked;
            settings.AutoSync = autoSyncCheckBox.Checked;
            settings.UnavailableSecondaryRetryMinutes = Decimal.ToInt32(unavailableSecondaryRetryMinutesNumeric.Value);
            unavailableSecondaryRetryMinutesNumeric.Enabled = settings.AutoSync;
            settings.DeleteStaleItems = deleteStaleCheckBox.Checked;
            settings.ExcludePythonCache = excludePythonCacheCheckBox.Checked;
            settings.ExcludeLogFiles = excludeLogFilesCheckBox.Checked;
            settings.WriteLogFile = writeLogFileCheckBox.Checked;
            settings.RunAtStartup = runAtStartupCheckBox.Checked;
            settings.StartMinimized = startMinimizedCheckBox.Checked;
            settings.UpdateCheckFrequency = StoredUpdateFrequency(Convert.ToString(updateCheckFrequencyComboBox.SelectedItem));
            settings.InstallUpdatesSilently = installUpdatesSilentlyCheckBox.Checked;
            settings.PlayStatusSounds = playStatusSoundsCheckBox.Checked;
            settings.LastPreferencesTab = tabs.SelectedIndex;
            if (preferencesChanged != null)
            {
                preferencesChanged();
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if ((showHideHotkeyTextBox.Focused || toggleSyncHotkeyTextBox.Focused) && e.KeyCode != Keys.Escape)
            {
                return;
            }
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

        private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Tab || e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter)
            {
                return;
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back)
            {
                var hotkeyBox = (TextBox)sender;
                hotkeyBox.Text = string.Empty;
                if (hotkeyBox == showHideHotkeyTextBox)
                {
                    enableGlobalHotkeyCheckBox.Checked = false;
                }
                else
                {
                    enableToggleSyncHotkeyCheckBox.Checked = false;
                }
                return;
            }
            if (GlobalHotkeyDefinition.IsModifierOnly(e.KeyData))
            {
                return;
            }

            var text = GlobalHotkeyDefinition.FromKeys(e.KeyData);
            GlobalHotkeyDefinition definition;
            if (!GlobalHotkeyDefinition.TryParse(text, out definition))
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }
            ((TextBox)sender).Text = text;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ValidateEnabledHotkeys())
            {
                e.Cancel = true;
                return;
            }
            if (warnBeforeSingleModifierHotkeysCheckBox.Checked &&
                ((enableGlobalHotkeyCheckBox.Checked && GlobalHotkeyDefinition.IsSingleModifierHotkey(showHideHotkeyTextBox.Text)) ||
                 (enableToggleSyncHotkeyCheckBox.Checked && GlobalHotkeyDefinition.IsSingleModifierHotkey(toggleSyncHotkeyTextBox.Text))))
            {
                bool suppressFuturePrompts;
                if (!SingleModifierHotkeyConfirmationForm.Ask(this, out suppressFuturePrompts))
                {
                    e.Cancel = true;
                    tabs.SelectedIndex = 3;
                    if (enableGlobalHotkeyCheckBox.Checked && GlobalHotkeyDefinition.IsSingleModifierHotkey(showHideHotkeyTextBox.Text))
                    {
                        showHideHotkeyTextBox.Focus();
                    }
                    else
                    {
                        toggleSyncHotkeyTextBox.Focus();
                    }
                    return;
                }
                if (suppressFuturePrompts)
                {
                    warnBeforeSingleModifierHotkeysCheckBox.Checked = false;
                    settings.WarnBeforeSingleModifierHotkeys = false;
                }
            }
            SaveHotkeySettings();
            ApplyNow();
            base.OnFormClosing(e);
        }

        private void SaveHotkeySettings()
        {
            settings.EnableGlobalHotkey = enableGlobalHotkeyCheckBox.Checked;
            settings.ShowHideHotkey = showHideHotkeyTextBox.Text.Trim();
            settings.WarnBeforeSingleModifierHotkeys = warnBeforeSingleModifierHotkeysCheckBox.Checked;
            settings.EnableToggleSyncHotkey = enableToggleSyncHotkeyCheckBox.Checked;
            settings.ToggleSyncHotkey = toggleSyncHotkeyTextBox.Text.Trim();
        }

        internal void SetAutoSyncChecked(bool enabled)
        {
            loading = true;
            autoSyncCheckBox.Checked = enabled;
            unavailableSecondaryRetryMinutesNumeric.Enabled = enabled;
            loading = false;
        }

        private bool ValidateEnabledHotkeys()
        {
            GlobalHotkeyDefinition parsed;
            if (enableGlobalHotkeyCheckBox.Checked && !GlobalHotkeyDefinition.TryParse(showHideHotkeyTextBox.Text, out parsed))
            {
                MessageBox.Show(this, "Choose a valid show/hide hotkey or turn that hotkey off.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tabs.SelectedIndex = 3;
                showHideHotkeyTextBox.Focus();
                return false;
            }
            if (enableToggleSyncHotkeyCheckBox.Checked && !GlobalHotkeyDefinition.TryParse(toggleSyncHotkeyTextBox.Text, out parsed))
            {
                MessageBox.Show(this, "Choose a valid toggle sync hotkey or turn that hotkey off.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tabs.SelectedIndex = 3;
                toggleSyncHotkeyTextBox.Focus();
                return false;
            }
            if (enableGlobalHotkeyCheckBox.Checked && enableToggleSyncHotkeyCheckBox.Checked &&
                string.Equals(showHideHotkeyTextBox.Text.Trim(), toggleSyncHotkeyTextBox.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "The show/hide and toggle sync hotkeys must be different.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                tabs.SelectedIndex = 3;
                toggleSyncHotkeyTextBox.Focus();
                return false;
            }
            return true;
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
        private static string StoredAddonMode(string value)
        {
            if (string.Equals(value, AddonSyncMode.DisplayName(AddonSyncMode.ExistingOnly), StringComparison.OrdinalIgnoreCase)) return AddonSyncMode.ExistingOnly;
            if (string.Equals(value, AddonSyncMode.DisplayName(AddonSyncMode.None), StringComparison.OrdinalIgnoreCase)) return AddonSyncMode.None;
            return AddonSyncMode.All;
        }
    }
}
