using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NvdaAddonSync
{
    internal sealed class MainForm : Form
    {
        private const int MaxSecondaryFolders = 5;

        private readonly AppSettings settings;
        private readonly SyncEngine syncEngine;
        private readonly System.Windows.Forms.Timer syncDebounceTimer;
        private readonly System.Windows.Forms.Timer availabilityTimer;
        private readonly FileSystemWatcher watcher;
        private readonly NotifyIcon trayIcon;
        private readonly ListBox primaryListBox;
        private readonly ListBox secondaryListBox;
        private readonly TextBox logTextBox;
        private readonly Button syncButton;
        private readonly Button cancelButton;
        private readonly System.Windows.Forms.Timer updateCheckTimer;
        private readonly EventWaitHandle showEvent;
        private readonly EventWaitHandle closeEvent;
        private readonly EventWaitHandle syncEvent;
        private readonly List<Thread> commandThreads;
        private CancellationTokenSource syncCancellation;
        private bool loaded;
        private bool syncing;
        private bool lastStartupRegistrationSetting;
        private string currentStatus;
        private string watchedPrimaryFolder;

        public MainForm()
        {
            Text = Program.ProductName;
            MinimumSize = new Size(640, 420);
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            commandThreads = new List<Thread>();

            settings = AppSettings.Load();
            ApplyStartupRegistration(false);
            lastStartupRegistrationSetting = settings.RunAtStartup;
            syncEngine = new SyncEngine();
            syncEngine.Message += AddLog;
            AddonPackService.Message += AddLog;
            PortableNvdaUpdateService.Message += AddLog;

            syncDebounceTimer = new System.Windows.Forms.Timer();
            syncDebounceTimer.Interval = 1500;
            syncDebounceTimer.Tick += OnSyncDebounceTimerTick;

            availabilityTimer = new System.Windows.Forms.Timer();
            availabilityTimer.Interval = 60000;
            availabilityTimer.Tick += OnAvailabilityTimerTick;

            updateCheckTimer = new System.Windows.Forms.Timer();
            updateCheckTimer.Tick += OnUpdateCheckTimerTick;

            watcher = new FileSystemWatcher();
            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size;
            watcher.Changed += OnPrimaryFolderChanged;
            watcher.Created += OnPrimaryFolderChanged;
            watcher.Deleted += OnPrimaryFolderChanged;
            watcher.Renamed += OnPrimaryFolderChanged;

            trayIcon = new NotifyIcon();
            currentStatus = "Starting";
            trayIcon.Text = BuildTrayText(currentStatus);
            trayIcon.Icon = SystemIcons.Application;
            trayIcon.Visible = true;
            trayIcon.Click += delegate { ShowMainWindow(); };
            trayIcon.DoubleClick += delegate { ShowMainWindow(); };
            trayIcon.ContextMenuStrip = BuildTrayMenu();

            showEvent = AppCommands.CreateEvent(AppCommand.Show);
            closeEvent = AppCommands.CreateEvent(AppCommand.Close);
            syncEvent = AppCommands.CreateEvent(AppCommand.Sync);
            StartCommandThread(showEvent, delegate { BeginInvoke(new Action(ShowMainWindow)); });
            StartCommandThread(closeEvent, delegate { BeginInvoke(new Action(Close)); });
            StartCommandThread(syncEvent, delegate { BeginInvoke(new Action(delegate { SyncNow("Command-line sync"); })); });

            var menu = BuildMenu();
            MainMenuStrip = menu;

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.Padding = new Padding(10);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);
            Controls.Add(menu);

            var primaryLabel = new Label();
            primaryLabel.AutoSize = true;
            primaryLabel.Text = "&Primary NVDA folder";
            root.Controls.Add(primaryLabel, 0, 0);

            var primaryPanel = new TableLayoutPanel();
            primaryPanel.Dock = DockStyle.Top;
            primaryPanel.ColumnCount = 3;
            primaryPanel.RowCount = 1;
            primaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            primaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            primaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.Controls.Add(primaryPanel, 0, 1);

            primaryListBox = new ListBox();
            primaryListBox.Dock = DockStyle.Fill;
            primaryListBox.Height = 44;
            primaryListBox.SelectionMode = SelectionMode.One;
            primaryListBox.AccessibleName = "Primary NVDA folder";
            primaryPanel.Controls.Add(primaryListBox, 0, 0);

            var browsePrimaryButton = new Button();
            browsePrimaryButton.Text = "&Browse...";
            browsePrimaryButton.AutoSize = true;
            browsePrimaryButton.Click += delegate { BrowseForPrimary(); };
            primaryPanel.Controls.Add(browsePrimaryButton, 1, 0);

            var detectPrimaryButton = new Button();
            detectPrimaryButton.Text = "&Detect...";
            detectPrimaryButton.AutoSize = true;
            detectPrimaryButton.Click += delegate { DetectPrimaryFolder(true); };
            primaryPanel.Controls.Add(detectPrimaryButton, 2, 0);

            var secondaryGroup = new GroupBox();
            secondaryGroup.Text = "Secondary NVDA folders";
            secondaryGroup.Dock = DockStyle.Fill;
            root.Controls.Add(secondaryGroup, 0, 2);

            secondaryListBox = new ListBox();
            secondaryListBox.Dock = DockStyle.Fill;
            secondaryListBox.SelectionMode = SelectionMode.One;
            secondaryListBox.ContextMenuStrip = BuildSecondaryContextMenu();
            secondaryListBox.DoubleClick += delegate { OpenSecondaryProperties(); };
            secondaryGroup.Controls.Add(secondaryListBox);

            var actionsPanel = new FlowLayoutPanel();
            actionsPanel.Dock = DockStyle.Top;
            actionsPanel.AutoSize = true;
            root.Controls.Add(actionsPanel, 0, 3);

            syncButton = new Button();
            syncButton.Text = "&Sync now";
            syncButton.AutoSize = true;
            syncButton.Click += delegate { FocusLog(); SyncNow("Manual sync"); };
            actionsPanel.Controls.Add(syncButton);

            cancelButton = new Button();
            cancelButton.Text = "&Cancel sync";
            cancelButton.AutoSize = true;
            cancelButton.Enabled = false;
            cancelButton.Click += delegate { CancelSync(); };
            actionsPanel.Controls.Add(cancelButton);

            var validateButton = new Button();
            validateButton.Text = "&Validate";
            validateButton.AutoSize = true;
            validateButton.Click += delegate { ValidateSettings(); FocusLog(); };
            actionsPanel.Controls.Add(validateButton);

            var hideButton = new Button();
            hideButton.Text = "Mi&nimise to tray";
            hideButton.AutoSize = true;
            hideButton.Click += delegate { Hide(); };
            actionsPanel.Controls.Add(hideButton);

            logTextBox = new TextBox();
            logTextBox.Dock = DockStyle.Fill;
            logTextBox.Multiline = true;
            logTextBox.ReadOnly = true;
            logTextBox.ScrollBars = ScrollBars.Vertical;
            logTextBox.AccessibleName = "NVDA Sync log";
            root.Controls.Add(logTextBox, 0, 4);

            var statusLabel = new Label();
            statusLabel.AutoSize = true;
            statusLabel.Text = "F1 opens the manual. Shift+F1 checks updates. Ctrl+F1 opens the project page. Ctrl+comma opens Preferences.";
            root.Controls.Add(statusLabel, 0, 5);

            LoadSettingsIntoControls();
            RestoreWindowBounds();
            loaded = true;
            DetectPrimaryFolder(false);
            ConfigureWatcher();
            ScheduleUpdateChecks();
            SetStatus("Ready");
            AddLog("Ready.");

            Shown += OnShown;
            FormClosing += OnFormClosing;
            Resize += OnResize;
        }

        private MenuStrip BuildMenu()
        {
            var menu = new MenuStrip();

            var foldersMenu = new ToolStripMenuItem("&Folders");
            foldersMenu.DropDownItems.Add(new ToolStripMenuItem("Set &primary folder...", null, delegate { BrowseForPrimary(); }));
            foldersMenu.DropDownItems.Add(new ToolStripMenuItem("&Detect primary folder", null, delegate { DetectPrimaryFolder(true); }));
            foldersMenu.DropDownItems.Add(new ToolStripSeparator());
            foldersMenu.DropDownItems.Add(new ToolStripMenuItem("&Add secondary folder...", null, delegate { AddSecondary(); }));
            foldersMenu.DropDownItems.Add(new ToolStripMenuItem("&Edit selected secondary...", null, delegate { EditSecondary(); }));
            foldersMenu.DropDownItems.Add(new ToolStripMenuItem("Secondary &properties...", null, delegate { OpenSecondaryProperties(); }));
            var removeItem = new ToolStripMenuItem("&Remove selected secondary", null, delegate { RemoveSecondary(); });
            removeItem.ShortcutKeyDisplayString = "Del";
            foldersMenu.DropDownItems.Add(removeItem);
            foldersMenu.DropDownItems.Add(new ToolStripSeparator());
            foldersMenu.DropDownItems.Add(new ToolStripMenuItem("&Validate settings", null, delegate { ValidateSettings(); FocusLog(); }));

            var addonsMenu = new ToolStripMenuItem("&Add-ons");
            addonsMenu.DropDownItems.Add(new ToolStripMenuItem("&Export add-on pack...", null, delegate { ExportAddonPack(); }));
            addonsMenu.DropDownItems.Add(new ToolStripMenuItem("&Install local add-ons to secondaries...", null, delegate { InstallLocalAddons(); }));

            var optionsMenu = new ToolStripMenuItem("&Options");
            var preferencesItem = new ToolStripMenuItem("&Preferences...", null, delegate { OpenPreferences(); });
            preferencesItem.ShortcutKeys = Keys.Control | Keys.Oemcomma;
            optionsMenu.DropDownItems.Add(preferencesItem);
            optionsMenu.DropDownItems.Add(new ToolStripSeparator());
            optionsMenu.DropDownItems.Add(new ToolStripMenuItem("Save &log...", null, delegate { SaveLogAs(); }));
            optionsMenu.DropDownItems.Add(new ToolStripMenuItem("Mi&nimise to tray", null, delegate { Hide(); }));
            optionsMenu.DropDownItems.Add(new ToolStripSeparator());
            optionsMenu.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, delegate { Close(); }));

            var helpMenu = new ToolStripMenuItem("&Help");
            var manualItem = new ToolStripMenuItem("&Manual", null, delegate { OpenManual(); });
            manualItem.ShortcutKeys = Keys.F1;
            helpMenu.DropDownItems.Add(manualItem);
            var updatesItem = new ToolStripMenuItem("Check for &updates...", null, delegate { CheckForUpdates(); });
            updatesItem.ShortcutKeys = Keys.Shift | Keys.F1;
            helpMenu.DropDownItems.Add(updatesItem);
            var projectItem = new ToolStripMenuItem("&Project page on GitHub", null, delegate { UpdateService.OpenProjectPage(); });
            projectItem.ShortcutKeys = Keys.Control | Keys.F1;
            helpMenu.DropDownItems.Add(projectItem);
            helpMenu.DropDownItems.Add(new ToolStripSeparator());
            helpMenu.DropDownItems.Add(new ToolStripMenuItem("&Contact", null, delegate { UpdateService.OpenContactPage(); }));
            helpMenu.DropDownItems.Add(new ToolStripMenuItem("&Donate", null, delegate { UpdateService.OpenDonatePage(); }));
            helpMenu.DropDownItems.Add(new ToolStripSeparator());
            helpMenu.DropDownItems.Add(new ToolStripMenuItem("&About NVDA Sync", null, delegate { ShowAbout(); }));

            menu.Items.Add(foldersMenu);
            menu.Items.Add(addonsMenu);
            menu.Items.Add(optionsMenu);
            menu.Items.Add(helpMenu);
            return menu;
        }

        private ContextMenuStrip BuildSecondaryContextMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Add secondary folder...", null, delegate { AddSecondary(); });
            menu.Items.Add("Edit selected secondary...", null, delegate { EditSecondary(); });
            menu.Items.Add("Properties...", null, delegate { OpenSecondaryProperties(); });
            menu.Items.Add("Remove selected secondary", null, delegate { RemoveSecondary(); });
            return menu;
        }

        private ContextMenuStrip BuildTrayMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Show", null, delegate { ShowMainWindow(); });
            menu.Items.Add("Sync now", null, delegate { SyncNow("Tray sync"); });
            menu.Items.Add("Preferences...", null, delegate { ShowMainWindow(); OpenPreferences(); });
            menu.Items.Add("Check for updates...", null, delegate { CheckForUpdates(); });
            menu.Items.Add("Help", null, delegate { OpenManual(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { Close(); });
            return menu;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Hide();
                return true;
            }
            if (keyData == Keys.F1)
            {
                OpenManual();
                return true;
            }
            if (keyData == (Keys.Shift | Keys.F1))
            {
                CheckForUpdates();
                return true;
            }
            if (keyData == (Keys.Control | Keys.F1))
            {
                UpdateService.OpenProjectPage();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Oemcomma))
            {
                OpenPreferences();
                return true;
            }
            if (keyData == Keys.Enter && secondaryListBox.Focused)
            {
                OpenSecondaryProperties();
                return true;
            }
            if (keyData == Keys.Delete && secondaryListBox.Focused)
            {
                RemoveSecondary();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void StartCommandThread(EventWaitHandle waitHandle, Action action)
        {
            var thread = new Thread(delegate()
            {
                while (!IsDisposed)
                {
                    try
                    {
                        waitHandle.WaitOne();
                        if (IsDisposed)
                        {
                            break;
                        }
                        action();
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch
                    {
                    }
                }
            });
            thread.IsBackground = true;
            thread.Name = "Command listener";
            commandThreads.Add(thread);
            thread.Start();
        }

        private void OnShown(object sender, EventArgs e)
        {
            if (settings.StartMinimized)
            {
                BeginInvoke(new Action(delegate { Hide(); }));
            }
        }

        private void OnResize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                WindowState = FormWindowState.Normal;
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            SaveSettingsFromControls();
            CancelSync();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            watcher.Dispose();
            syncDebounceTimer.Dispose();
            availabilityTimer.Dispose();
            updateCheckTimer.Dispose();
            showEvent.Dispose();
            closeEvent.Dispose();
            syncEvent.Dispose();
        }

        private void LoadSettingsIntoControls()
        {
            SetPrimaryFolder(settings.PrimaryFolder ?? "");
            secondaryListBox.Items.Clear();
            foreach (var profile in settings.SecondaryFolderProfiles)
            {
                if (profile != null)
                {
                    profile.Normalize();
                    secondaryListBox.Items.Add(profile);
                }
            }
        }

        private void RestoreWindowBounds()
        {
            if (settings.WindowWidth > 0 && settings.WindowHeight > 0)
            {
                Width = settings.WindowWidth;
                Height = settings.WindowHeight;
            }
            if (settings.WindowLeft != 0 || settings.WindowTop != 0)
            {
                Left = settings.WindowLeft;
                Top = settings.WindowTop;
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
            }
        }

        private void SaveSettingsFromControls()
        {
            if (!loaded)
            {
                return;
            }
            settings.PrimaryFolder = GetPrimaryFolder();
            settings.SecondaryFolderProfiles = GetSecondaryProfiles();
            settings.SyncLegacySecondaryFolders();
            if (WindowState == FormWindowState.Normal)
            {
                settings.WindowLeft = Left;
                settings.WindowTop = Top;
                settings.WindowWidth = Width;
                settings.WindowHeight = Height;
            }
            settings.Save();
        }

        private List<SecondaryFolderProfile> GetSecondaryProfiles()
        {
            var profiles = new List<SecondaryFolderProfile>();
            foreach (var item in secondaryListBox.Items)
            {
                var profile = item as SecondaryFolderProfile;
                if (profile != null)
                {
                    profile.Normalize();
                    profiles.Add(profile);
                    continue;
                }
                profiles.Add(SecondaryFolderProfile.FromPath(Convert.ToString(item)));
            }
            return profiles;
        }

        private List<string> GetSecondaryFolders()
        {
            var folders = new List<string>();
            foreach (var profile in GetSecondaryProfiles())
            {
                folders.Add(profile.Path);
            }
            return folders;
        }

        private string GetPrimaryFolder()
        {
            if (primaryListBox.Items.Count == 0)
            {
                return string.Empty;
            }
            return Convert.ToString(primaryListBox.Items[0]).Trim();
        }

        private void SetPrimaryFolder(string folder)
        {
            primaryListBox.Items.Clear();
            var value = (folder ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                primaryListBox.Items.Add(value);
                primaryListBox.SelectedIndex = 0;
            }
        }

        private void DetectPrimaryFolder(bool announceResult)
        {
            var current = GetPrimaryFolder();
            if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
            {
                if (announceResult)
                {
                    AddLog("Primary folder is already set to " + current);
                    FocusLog();
                }
                return;
            }

            var detected = FindLikelyNvdaConfigFolder();
            if (string.IsNullOrWhiteSpace(detected))
            {
                if (announceResult)
                {
                    AddLog("Could not detect an NVDA folder automatically. Use Browse to choose one.");
                    FocusLog();
                }
                return;
            }

            SetPrimaryFolder(detected);
            SaveSettingsFromControls();
            ConfigureWatcher();
            AddLog("Detected primary folder: " + detected);
            if (announceResult)
            {
                FocusLog();
            }
        }

        private static string FindLikelyNvdaConfigFolder()
        {
            var candidates = new List<string>();
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
            {
                candidates.Add(Path.Combine(appData, "nvda"));
            }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile) && Directory.Exists(userProfile))
            {
                candidates.AddRange(FindLikelyNvdaFoldersUnder(userProfile, 400));
            }

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
                {
                    continue;
                }
                try
                {
                    return SyncEngine.ResolveNvdaConfigDirectory(candidate, "Detected NVDA folder", true);
                }
                catch
                {
                }
            }
            return string.Empty;
        }

        private static IEnumerable<string> FindLikelyNvdaFoldersUnder(string root, int maxDirectories)
        {
            var results = new List<string>();
            var queue = new Queue<Tuple<string, int>>();
            queue.Enqueue(Tuple.Create(root, 0));
            var visited = 0;

            while (queue.Count > 0 && visited < maxDirectories)
            {
                var item = queue.Dequeue();
                var folder = item.Item1;
                var depth = item.Item2;
                visited++;

                var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.Equals(name, "nvda", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "userConfig", StringComparison.OrdinalIgnoreCase) ||
                    File.Exists(Path.Combine(folder, "nvda.ini")) ||
                    File.Exists(Path.Combine(folder, "nvda.exe")))
                {
                    results.Add(folder);
                }

                if (depth >= 4)
                {
                    continue;
                }

                string[] children;
                try
                {
                    children = Directory.GetDirectories(folder);
                }
                catch
                {
                    continue;
                }
                foreach (var child in children)
                {
                    var childName = Path.GetFileName(child);
                    if (string.Equals(childName, "node_modules", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(childName, ".git", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(childName, "__pycache__", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    queue.Enqueue(Tuple.Create(child, depth + 1));
                }
            }
            return results;
        }

        private void ConfigureWatcher()
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                if (!settings.AutoSync)
                {
                    availabilityTimer.Stop();
                    watchedPrimaryFolder = null;
                    SetStatus("Ready");
                    return;
                }
                availabilityTimer.Start();
                var primary = GetPrimaryFolder();
                if (Directory.Exists(primary))
                {
                    var changedWatchPath = !string.Equals(watchedPrimaryFolder, primary, StringComparison.OrdinalIgnoreCase);
                    watcher.Path = primary;
                    watcher.EnableRaisingEvents = true;
                    watchedPrimaryFolder = primary;
                    SetStatus("Watching");
                    if (changedWatchPath)
                    {
                        AddLog("Watching " + primary);
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("Watcher disabled: " + ex.Message);
            }
        }
        private void OnPrimaryFolderChanged(object sender, FileSystemEventArgs e)
        {
            BeginInvoke(new Action(delegate
            {
                syncDebounceTimer.Stop();
                syncDebounceTimer.Start();
            }));
        }

        private void OnSyncDebounceTimerTick(object sender, EventArgs e)
        {
            syncDebounceTimer.Stop();
            SyncNow("Primary changed");
        }

        private void OnAvailabilityTimerTick(object sender, EventArgs e)
        {
            if (!settings.AutoSync || syncing)
            {
                return;
            }
            if (HasUnavailableSecondary())
            {
                SyncNow("Checking reconnected secondaries");
            }
        }

        private void ScheduleUpdateChecks()
        {
            updateCheckTimer.Stop();
            var frequency = AppSettings.NormalizeUpdateCheckFrequency(settings.UpdateCheckFrequency);
            if (string.Equals(frequency, "Never", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (string.Equals(frequency, "Startup", StringComparison.OrdinalIgnoreCase))
            {
                updateCheckTimer.Interval = 30000;
            }
            else if (string.Equals(frequency, "Hourly", StringComparison.OrdinalIgnoreCase))
            {
                updateCheckTimer.Interval = 60 * 60 * 1000;
            }
            else
            {
                updateCheckTimer.Interval = 24 * 60 * 60 * 1000;
            }
            updateCheckTimer.Start();
        }

        private void OnUpdateCheckTimerTick(object sender, EventArgs e)
        {
            var frequency = AppSettings.NormalizeUpdateCheckFrequency(settings.UpdateCheckFrequency);
            if (string.Equals(frequency, "Startup", StringComparison.OrdinalIgnoreCase))
            {
                updateCheckTimer.Stop();
            }
            CheckForUpdatesAutomatically();
        }

        private void CheckForUpdatesAutomatically()
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            BeginInvoke(new Action(delegate
            {
                if (IsDisposed)
                {
                    return;
                }
                UpdateService.CheckForUpdatesAutomatic(this, Program.Version, delegate { Close(); }, settings.InstallUpdatesSilently);
            }));
        }

        private bool HasUnavailableSecondary()
        {
            foreach (var secondary in GetSecondaryFolders())
            {
                if (string.IsNullOrWhiteSpace(secondary))
                {
                    continue;
                }
                var root = Path.GetPathRoot(secondary);
                if (!string.IsNullOrWhiteSpace(root) && !Directory.Exists(root))
                {
                    return true;
                }
                if (!Directory.Exists(secondary))
                {
                    var parent = Path.GetDirectoryName(secondary);
                    if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void BrowseForPrimary()
        {
            var selected = BrowseFolder(GetPrimaryFolder(), "Choose primary NVDA folder");
            if (selected != null)
            {
                SetPrimaryFolder(ResolveFolderForUi(selected, "Primary folder", true));
                SaveSettingsFromControls();
                ConfigureWatcher();
            }
        }

        private void AddSecondary()
        {
            if (secondaryListBox.Items.Count >= MaxSecondaryFolders)
            {
                MessageBox.Show(this, "You can add up to five secondary folders.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var selected = BrowseFolder("", "Choose secondary NVDA folder");
            if (selected != null)
            {
                secondaryListBox.Items.Add(SecondaryFolderProfile.FromPath(ResolveFolderForUi(selected, "Secondary folder", false)));
                SaveSettingsFromControls();
            }
        }

        private void EditSecondary()
        {
            if (secondaryListBox.SelectedIndex < 0)
            {
                return;
            }
            var current = Convert.ToString(secondaryListBox.SelectedItem);
            var selected = BrowseFolder(current, "Choose secondary NVDA folder");
            if (selected != null)
            {
                secondaryListBox.Items[secondaryListBox.SelectedIndex] = ResolveFolderForUi(selected, "Secondary folder", false);
                SaveSettingsFromControls();
            }
        }


        private void OpenSecondaryProperties()
        {
            if (secondaryListBox.SelectedIndex < 0)
            {
                return;
            }
            var profile = secondaryListBox.SelectedItem as SecondaryFolderProfile;
            if (profile == null)
            {
                profile = SecondaryFolderProfile.FromPath(Convert.ToString(secondaryListBox.SelectedItem));
                secondaryListBox.Items[secondaryListBox.SelectedIndex] = profile;
            }
            using (var dialog = new SecondaryFolderPropertiesForm(profile, settings))
            {
                dialog.ShowDialog(this);
            }
            profile.Normalize();
            var index = secondaryListBox.SelectedIndex;
            secondaryListBox.Items[index] = profile;
            secondaryListBox.SelectedIndex = index;
            SaveSettingsFromControls();
        }
        private void ResolvePrimaryFolder()
        {
            var value = GetPrimaryFolder();
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }
            try
            {
                var resolved = SyncEngine.ResolveNvdaConfigDirectory(value, "Primary folder", true);
                if (!string.Equals(value, resolved, StringComparison.OrdinalIgnoreCase))
                {
                    SetPrimaryFolder(resolved);
                    AddLog("Resolved primary to " + resolved);
                }
            }
            catch (Exception ex)
            {
                AddLog("Could not resolve primary folder: " + ex.Message);
            }
        }

        private string ResolveFolderForUi(string selected, string label, bool mustExist)
        {
            try
            {
                var resolved = SyncEngine.ResolveNvdaConfigDirectory(selected, label, mustExist);
                if (!string.Equals(selected, resolved, StringComparison.OrdinalIgnoreCase))
                {
                    AddLog("Resolved " + label.ToLowerInvariant() + " to " + resolved);
                }
                return resolved;
            }
            catch (Exception ex)
            {
                AddLog("Could not resolve " + label.ToLowerInvariant() + ": " + ex.Message);
                return selected;
            }
        }

        private void RemoveSecondary()
        {
            if (secondaryListBox.SelectedIndex < 0)
            {
                return;
            }
            var index = secondaryListBox.SelectedIndex;
            secondaryListBox.Items.RemoveAt(index);
            if (secondaryListBox.Items.Count > 0)
            {
                secondaryListBox.SelectedIndex = Math.Min(index, secondaryListBox.Items.Count - 1);
            }
            SaveSettingsFromControls();
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

        private void ValidateSettings()
        {
            logTextBox.Clear();
            try
            {
                SyncEngine.ResolveNvdaConfigDirectory(GetPrimaryFolder(), "Primary folder", true);
                var profiles = GetSecondaryProfiles();
                if (profiles.Count == 0)
                {
                    throw new InvalidOperationException("Add at least one secondary folder.");
                }
                var hasWork = false;
                var needsProgramSource = false;
                foreach (var profile in profiles)
                {
                    var policy = profile.Resolve(settings);
                    if (!policy.HasWork)
                    {
                        throw new InvalidOperationException("Choose at least one sync option for " + profile.Path + ".");
                    }
                    hasWork = true;
                    SyncEngine.ResolveNvdaConfigDirectory(policy.Path, "Secondary folder", false);
                    if (policy.SyncNvdaProgramFiles)
                    {
                        needsProgramSource = true;
                        PortableNvdaUpdateService.ResolvePortableRoot(policy.Path, false);
                    }
                }
                if (!hasWork)
                {
                    throw new InvalidOperationException("Choose at least one component or portable program-file update to sync.");
                }
                if (needsProgramSource)
                {
                    PortableNvdaUpdateService.DetectInstalledNvdaProgramFolder();
                }
                AddLog("Settings look usable.");
            }
            catch (Exception ex)
            {
                AddLog("Validation failed: " + ex.Message);
            }
        }
        private void FocusLog()
        {
            if (IsDisposed || !IsHandleCreated || !logTextBox.CanFocus)
            {
                return;
            }
            logTextBox.Focus();
            logTextBox.SelectionStart = logTextBox.TextLength;
            logTextBox.SelectionLength = 0;
        }

        private async void SyncNow(string reason)
        {
            if (syncing)
            {
                AddLog("Sync already running.");
                FocusLog();
                return;
            }
            syncing = true;
            syncButton.Enabled = false;
            cancelButton.Enabled = true;
            SetStatus("Syncing");
            AddLog(reason + ".");
            var syncStopwatch = Stopwatch.StartNew();
            ResolvePrimaryFolder();
            var primary = SyncEngine.ResolveNvdaConfigDirectory(GetPrimaryFolder(), "Primary folder", true);
            var policies = new List<SecondaryFolderPolicy>();
            foreach (var profile in GetSecondaryProfiles())
            {
                var policy = profile.Resolve(settings);
                policy.Path = SyncEngine.ResolveNvdaConfigDirectory(policy.Path, "Secondary folder", false);
                if (policy.HasWork)
                {
                    policies.Add(policy);
                }
            }
            if (policies.Count == 0)
            {
                AddLog("Choose at least one component or portable program-file update to sync.");
                FocusLog();
                syncing = false;
                syncButton.Enabled = true;
                cancelButton.Enabled = false;
                SetStatus("Ready");
                return;
            }
            settings.PrimaryFolder = primary;
            settings.SecondaryFolderProfiles = GetSecondaryProfiles();
            settings.SyncLegacySecondaryFolders();
            settings.Save();
            var cancellation = new CancellationTokenSource();
            syncCancellation = cancellation;
            try
            {
                SaveSettingsFromControls();
                var result = await Task.Factory.StartNew(
                    delegate
                    {
                        var total = new SyncResult();
                        foreach (var policy in policies)
                        {
                            cancellation.Token.ThrowIfCancellationRequested();
                            if (policy.Components != null && policy.Components.Count > 0)
                            {
                                var configResult = syncEngine.Sync(
                                    primary,
                                    new[] { policy.Path },
                                    new SyncOptions
                                    {
                                        DeleteStaleItems = policy.DeleteStaleItems,
                                        ExcludePythonCache = policy.ExcludePythonCache,
                                        AddonMode = policy.AddonMode,
                                        Components = policy.Components,
                                        CancellationToken = cancellation.Token
                                    }
                                );
                                AddSyncResult(total, configResult);
                            }
                            if (policy.SyncNvdaProgramFiles)
                            {
                                var programResult = PortableNvdaUpdateService.UpdateTarget(PortableNvdaUpdateService.DetectInstalledNvdaProgramFolder(), policy.Path, policy.CreateProgramBackups, cancellation.Token);
                                total.ComponentsSynced += programResult.TargetsUpdated;
                                total.FilesCopied += programResult.FilesAdded + programResult.FilesReplaced;
                                total.FilesSkipped += programResult.FilesSkipped;
                                total.ItemsDeleted += programResult.FilesDeleted + programResult.DirectoriesDeleted;
                                total.UnavailableTargets += programResult.UnavailableTargets;
                                total.Errors += programResult.Errors;
                            }
                        }
                        return total;
                    },
                    cancellation.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                );
                AddLog(string.Format(
                    "Finished. Components {0}, copied {1}, skipped {2}, ignored {3}, deleted {4}, unavailable {5}, errors {6}. Time taken {7}.",
                    result.ComponentsSynced,
                    result.FilesCopied,
                    result.FilesSkipped,
                    result.ItemsIgnored,
                    result.ItemsDeleted,
                    result.UnavailableTargets,
                    result.Errors,
                    FormatDuration(syncStopwatch.Elapsed)
                ));
                if (result.Errors != 0)
                {
                    SetStatus("Finished with errors");
                }
                else if (result.UnavailableTargets != 0)
                {
                    SetStatus("Waiting for drive");
                }
                else
                {
                    SetStatus("Ready");
                }
            }
            catch (OperationCanceledException)
            {
                AddLog("Cancelled.");
                SetStatus("Cancelled");
            }
            catch (Exception ex)
            {
                AddLog("Sync failed: " + ex.Message);
                SetStatus("Sync failed");
            }
            finally
            {
                syncButton.Enabled = true;
                cancelButton.Enabled = false;
                if (syncCancellation == cancellation)
                {
                    syncCancellation = null;
                }
                cancellation.Dispose();
                syncing = false;
                FocusLog();
            }
        }

        private static void AddSyncResult(SyncResult total, SyncResult addition)
        {
            total.ComponentsSynced += addition.ComponentsSynced;
            total.FilesCopied += addition.FilesCopied;
            total.FilesSkipped += addition.FilesSkipped;
            total.ItemsDeleted += addition.ItemsDeleted;
            total.ItemsIgnored += addition.ItemsIgnored;
            total.UnavailableTargets += addition.UnavailableTargets;
            total.Errors += addition.Errors;
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
            {
                return string.Format("{0:0}:{1:00}:{2:00}", Math.Floor(duration.TotalHours), duration.Minutes, duration.Seconds);
            }
            if (duration.TotalMinutes >= 1)
            {
                return string.Format("{0:0} min {1:00} sec", Math.Floor(duration.TotalMinutes), duration.Seconds);
            }
            return string.Format("{0:0.0} sec", duration.TotalSeconds);
        }
        private void CancelSync()
        {
            var cancellation = syncCancellation;
            if (cancellation != null && !cancellation.IsCancellationRequested)
            {
                AddLog("Cancelling current operation...");
                SetStatus("Cancelling");
                cancellation.Cancel();
            }
        }

        private void OpenPreferences()
        {
            using (var dialog = new PreferencesForm(settings, delegate
            {
                settings.Save();
                if (settings.RunAtStartup != lastStartupRegistrationSetting)
                {
                    ApplyStartupRegistration(true);
                    lastStartupRegistrationSetting = settings.RunAtStartup;
                }
                ConfigureWatcher();
                ScheduleUpdateChecks();
            }))
            {
                dialog.ShowDialog(this);
            }
        }

        private void AddLog(string message)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<string>(AddLog), message);
                }
                catch
                {
                }
                return;
            }
            logTextBox.AppendText(message + Environment.NewLine);
            AppendLogFile(message);
        }

        private void SaveLogAs()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "Save NVDA Sync log";
                dialog.Filter = "Text files (*.txt)|*.txt|Log files (*.log)|*.log|All files (*.*)|*.*";
                dialog.FileName = "NVDASync-log.txt";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                File.WriteAllText(dialog.FileName, logTextBox.Text);
                AddLog("Saved log to " + dialog.FileName);
                FocusLog();
            }
        }

        private void ExportAddonPack()
        {
            try
            {
                ResolvePrimaryFolder();
                var primary = SyncEngine.ResolveNvdaConfigDirectory(GetPrimaryFolder(), "Primary folder", true);
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Title = "Export add-on pack";
                    dialog.Filter = "NVDA Sync add-on packs (*.json)|*.json|All files (*.*)|*.*";
                    dialog.FileName = "NVDASync-addon-pack.json";
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                    AddonPackService.ExportPackFile(primary, dialog.FileName);
                    AddLog("Add-on pack export complete.");
                    FocusLog();
                }
            }
            catch (Exception ex)
            {
                AddLog("Add-on pack export failed: " + ex.Message);
                FocusLog();
            }
        }

        private async void InstallLocalAddons()
        {
            if (syncing)
            {
                AddLog("Sync already running.");
                FocusLog();
                return;
            }
            var secondaries = GetSecondaryFolders();
            if (secondaries.Count == 0)
            {
                AddLog("Add at least one secondary folder before installing local add-ons.");
                FocusLog();
                return;
            }
            var source = BrowseFolder("", "Choose folder containing NVDA add-ons");
            if (source == null)
            {
                return;
            }
            var confirmation = MessageBox.Show(
                this,
                "Install valid add-ons from:" + Environment.NewLine + source + Environment.NewLine + Environment.NewLine +
                "Targets: configured secondary folders only." + Environment.NewLine +
                "Existing add-ons with the same manifest name will be replaced.",
                "Install local add-ons",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);
            if (confirmation != DialogResult.OK)
            {
                return;
            }

            syncing = true;
            syncButton.Enabled = false;
            cancelButton.Enabled = true;
            SetStatus("Installing add-ons");
            AddLog("Installing local add-ons.");
            var installStopwatch = Stopwatch.StartNew();
            var cancellation = new CancellationTokenSource();
            syncCancellation = cancellation;
            var excludePythonCache = settings.ExcludePythonCache;
            try
            {
                var result = await Task.Factory.StartNew(
                    delegate
                    {
                        return AddonPackService.InstallFromDirectory(source, secondaries, excludePythonCache, cancellation.Token);
                    },
                    cancellation.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default
                );
                AddLog(string.Format(
                    "Finished installing add-ons. Found {0}, installed {1}, skipped {2}, targets {3}, copied {4}, ignored {5}, unavailable {6}, errors {7}. Time taken {8}.",
                    result.AddonsFound,
                    result.AddonsInstalled,
                    result.AddonsSkipped,
                    result.TargetsUpdated,
                    result.FilesCopied,
                    result.ItemsIgnored,
                    result.UnavailableTargets,
                    result.Errors,
                    FormatDuration(installStopwatch.Elapsed)
                ));
                SetStatus(result.Errors == 0 ? "Ready" : "Finished with errors");
            }
            catch (OperationCanceledException)
            {
                AddLog("Add-on install cancelled.");
                SetStatus("Cancelled");
            }
            catch (Exception ex)
            {
                AddLog("Add-on install failed: " + ex.Message);
                SetStatus("Install failed");
            }
            finally
            {
                syncButton.Enabled = true;
                cancelButton.Enabled = false;
                if (syncCancellation == cancellation)
                {
                    syncCancellation = null;
                }
                cancellation.Dispose();
                syncing = false;
                FocusLog();
            }
        }

        private void CheckForUpdates()
        {
            UpdateService.CheckForUpdates(this, Program.Version, delegate { Close(); });
        }

        private void ApplyStartupRegistration(bool showErrors)
        {
            try
            {
                StartupRegistration.SetEnabled(settings.RunAtStartup);
            }
            catch (Exception ex)
            {
                if (showErrors)
                {
                    MessageBox.Show(this, "Could not update the Windows startup entry." + Environment.NewLine + Environment.NewLine + ex.Message, "NVDA Sync startup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private static void AppendLogFile(string message)
        {
            try
            {
                var logFolder = Path.Combine(AppSettings.AppFolder, "Logs");
                Directory.CreateDirectory(logFolder);
                var logPath = Path.Combine(logFolder, "NVDASync.log");
                RotateLogIfNeeded(logPath);
                File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd ") + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static void RotateLogIfNeeded(string logPath)
        {
            try
            {
                if (!File.Exists(logPath))
                {
                    return;
                }
                var info = new FileInfo(logPath);
                if (info.Length < 1024 * 1024)
                {
                    return;
                }
                var oldPath = Path.Combine(Path.GetDirectoryName(logPath), "NVDASync.old.log");
                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }
                File.Move(logPath, oldPath);
            }
            catch
            {
            }
        }

        private void SetStatus(string status)
        {
            currentStatus = status;
            if (trayIcon != null)
            {
                trayIcon.Text = BuildTrayText(status);
            }
        }

        private static string BuildTrayText(string status)
        {
            var text = Program.ProductName + ": " + status;
            return text.Length <= 63 ? text : text.Substring(0, 63);
        }

        private void ShowMainWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void OpenManual()
        {
            var manual = Path.Combine(AppSettings.AppFolder, "Manual.html");
            if (!File.Exists(manual))
            {
                MessageBox.Show(this, "Manual.html was not found beside the app.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            System.Diagnostics.Process.Start(manual);
        }

        private void ShowAbout()
        {
            MessageBox.Show(
                this,
                Program.ProductName + " " + Program.Version + Environment.NewLine +
                "Author: " + Program.Author + Environment.NewLine +
                "License: MIT" + Environment.NewLine +
                UpdateService.ProjectUrl,
                "About NVDA Sync",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
