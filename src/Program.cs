using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace NvdaAddonSync
{
    internal static partial class Program
    {
        internal const string ProductName = "NVDA Sync";
        internal const string Version = "1.5.0";
        internal const string Author = "Andre Louis";

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "--apply-update", StringComparison.OrdinalIgnoreCase))
            {
                ApplyUpdateFromCommandLine(args);
                return;
            }

            var commandLine = CommandLineOptions.Parse(args);
            if (commandLine.ShowHelp)
            {
                MessageBox.Show(CommandLineOptions.HelpText, ProductName + " " + Version, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (commandLine.ShowVersion)
            {
                MessageBox.Show(ProductName + " " + Version + Environment.NewLine + "Author: " + Author, ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MigrateLegacyExecutableIfNeeded(args))
            {
                return;
            }
            MigrateLegacyNotificationIconRegistration();
            NotificationIconRegistration.EnsurePromotedCurrentExecutablePath(Application.ExecutablePath);
            TryDeleteLegacyExecutable(commandLine.DeleteOldExePath);

            if (commandLine.CloseRunning)
            {
                Environment.ExitCode = AppCommands.Signal(AppCommand.Close) ? 0 : 1;
                return;
            }
            if (commandLine.ShowRunning)
            {
                Environment.ExitCode = AppCommands.Signal(AppCommand.Show) ? 0 : 1;
                return;
            }
            if (commandLine.SyncRunning)
            {
                Environment.ExitCode = AppCommands.Signal(AppCommand.Sync) ? 0 : 1;
                return;
            }
            if (commandLine.SyncOnce)
            {
                Environment.ExitCode = RunCommandLineSync(commandLine);
                return;
            }
            if (!string.IsNullOrWhiteSpace(commandLine.ExportAddonPackFile))
            {
                Environment.ExitCode = RunCommandLineAddonPackExport(commandLine);
                return;
            }
            if (!string.IsNullOrWhiteSpace(commandLine.InstallAddonsFolder))
            {
                Environment.ExitCode = RunCommandLineAddonInstall(commandLine);
                return;
            }
            TryDeleteBundledLegacyExecutable();

            Mutex mutex;
            if (!AcquireSingleInstanceMutex(out mutex))
            {
                return;
            }
            using (mutex)
            {
                InstanceStateStore.PublishCurrent(AppSettings.AppFolder);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    Application.Run(new MainForm(commandLine.StartupLaunch));
                }
                finally
                {
                    InstanceStateStore.ClearIfCurrent(AppSettings.AppFolder);
                }
                GC.KeepAlive(mutex);
            }
        }

        private static bool AcquireSingleInstanceMutex(out Mutex mutex)
        {
            mutex = null;
            if (TryAcquireMutex(out mutex))
            {
                return true;
            }

            var appFolder = AppSettings.AppFolder;
            if (InstanceStateStore.IsSameRunningFolder(appFolder))
            {
                AppCommands.Signal(AppCommand.Show);
                return false;
            }

            var previous = InstanceStateStore.Load();
            if (previous != null && !string.IsNullOrWhiteSpace(previous.AppFolder))
            {
                AppCommands.Signal(AppCommand.Close, previous.AppFolder);
            }
            WaitForPreviousInstanceToExit(previous == null ? 0 : previous.ProcessId);

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                if (TryAcquireMutex(out mutex))
                {
                    return true;
                }
                Thread.Sleep(250);
            }

            AppCommands.Signal(AppCommand.Show, previous == null ? appFolder : previous.AppFolder);
            return false;
        }

        private static bool TryAcquireMutex(out Mutex mutex)
        {
            bool created;
            mutex = new Mutex(true, AppCommands.MutexName, out created);
            if (created)
            {
                return true;
            }
            mutex.Dispose();
            mutex = null;
            return false;
        }

        private static void WaitForPreviousInstanceToExit(int processId)
        {
            if (processId <= 0)
            {
                return;
            }
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    process.WaitForExit(10000);
                }
            }
            catch
            {
            }
        }

        private static bool MigrateLegacyExecutableIfNeeded(string[] originalArgs)
        {
            var currentExe = Application.ExecutablePath;
            if (!string.Equals(Path.GetFileName(currentExe), "NvdaAddonSync.exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            var preferredExe = Path.Combine(AppSettings.AppFolder, "NVDASync.exe");
            if (!File.Exists(preferredExe))
            {
                return false;
            }
            try
            {
                var arguments = new List<string>(originalArgs ?? new string[0]);
                arguments.Add("--delete-old-exe");
                arguments.Add(currentExe);
                if (!arguments.Exists(arg => string.Equals(arg, "--startup-minimized", StringComparison.OrdinalIgnoreCase)))
                {
                    arguments.Add("--startup-minimized");
                }
                Process.Start(new ProcessStartInfo
                {
                    FileName = preferredExe,
                    Arguments = CommandLineOptions.JoinArguments(arguments),
                    WorkingDirectory = AppSettings.AppFolder,
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryDeleteLegacyExecutable(string oldExePath)
        {
            if (string.IsNullOrWhiteSpace(oldExePath))
            {
                return;
            }
            if (string.Equals(oldExePath, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (!string.Equals(Path.GetFileName(oldExePath), "NvdaAddonSync.exe", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (File.Exists(oldExePath))
                    {
                        File.Delete(oldExePath);
                    }
                    return;
                }
                catch
                {
                    Thread.Sleep(250);
                }
            }
        }

        private static void MigrateLegacyNotificationIconRegistration()
        {
            var currentExe = Application.ExecutablePath;
            if (string.Equals(Path.GetFileName(currentExe), "NvdaAddonSync.exe", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            NotificationIconRegistration.MigrateLegacyExecutablePath(
                Path.Combine(AppSettings.AppFolder, "NvdaAddonSync.exe"),
                currentExe);
        }

        private static void TryDeleteBundledLegacyExecutable()
        {
            if (string.Equals(Path.GetFileName(Application.ExecutablePath), "NvdaAddonSync.exe", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            TryDeleteLegacyExecutable(Path.Combine(AppSettings.AppFolder, "NvdaAddonSync.exe"));
        }

        private static int RunCommandLineSync(CommandLineOptions options)
        {
            var settings = AppSettings.Load();
            if (!string.IsNullOrWhiteSpace(options.PrimaryFolder))
            {
                settings.PrimaryFolder = SyncEngine.ResolveNvdaConfigDirectory(options.PrimaryFolder, "Primary folder", true);
            }
            if (options.SecondaryFolders.Count > 0)
            {
                settings.SecondaryFolders.Clear();
                settings.SecondaryFolderProfiles.Clear();
                foreach (var folder in options.SecondaryFolders)
                {
                    if (settings.SecondaryFolders.Count >= 5)
                    {
                        break;
                    }
                    var resolved = SyncEngine.ResolveNvdaConfigDirectory(folder, "Secondary folder", false);
                    settings.SecondaryFolders.Add(resolved);
                    settings.SecondaryFolderProfiles.Add(SecondaryFolderProfile.FromPath(resolved));
                }
            }
            if (options.DeleteStaleOverride.HasValue)
            {
                settings.DeleteStaleItems = options.DeleteStaleOverride.Value;
            }
            if (options.ExcludePythonCacheOverride.HasValue)
            {
                settings.ExcludePythonCache = options.ExcludePythonCacheOverride.Value;
            }
            if (options.ExcludeLogFilesOverride.HasValue)
            {
                settings.ExcludeLogFiles = options.ExcludeLogFilesOverride.Value;
            }
            var components = options.GetComponentsOrDefault(settings);
            ApplyComponentsToSettings(settings, components);
            if (options.SaveSettings)
            {
                settings.Save();
            }
            var engine = new SyncEngine();
            var result = engine.Sync(
                settings.PrimaryFolder,
                settings.SecondaryFolders,
                new SyncOptions
                {
                    DeleteStaleItems = settings.DeleteStaleItems,
                    ExcludePythonCache = settings.ExcludePythonCache,
                    ExcludeLogFiles = settings.ExcludeLogFiles,
                    Components = components
                }
            );
            return result.Errors == 0 ? 0 : 2;
        }

        private static void ApplyComponentsToSettings(AppSettings settings, List<SyncComponent> components)
        {
            settings.SyncAddons = components.Contains(SyncComponent.Addons);
            settings.SyncInputGestures = components.Contains(SyncComponent.InputGestures);
            settings.SyncNvdaIni = components.Contains(SyncComponent.NvdaIni);
            settings.SyncSpeechDictionaries = components.Contains(SyncComponent.SpeechDictionaries);
            settings.SyncConfigProfiles = components.Contains(SyncComponent.ConfigProfiles);
            settings.SyncOtherConfigFiles = components.Contains(SyncComponent.OtherConfigFiles);
            settings.SyncOtherConfigFolders = components.Contains(SyncComponent.OtherConfigFolders);
        }

        private static int RunCommandLineAddonPackExport(CommandLineOptions options)
        {
            var settings = AppSettings.Load();
            var primary = string.IsNullOrWhiteSpace(options.PrimaryFolder) ? settings.PrimaryFolder : options.PrimaryFolder;
            AddonPackService.ExportPackFile(primary, options.ExportAddonPackFile);
            return 0;
        }

        private static int RunCommandLineAddonInstall(CommandLineOptions options)
        {
            var settings = AppSettings.Load();
            var secondaries = new List<string>();
            if (options.SecondaryFolders.Count > 0)
            {
                secondaries.AddRange(options.SecondaryFolders);
            }
            else
            {
                secondaries.AddRange(settings.SecondaryFolders);
            }
            if (secondaries.Count == 0)
            {
                throw new InvalidOperationException("Add at least one secondary folder for --install-addons.");
            }
            var excludePythonCache = options.ExcludePythonCacheOverride.HasValue ? options.ExcludePythonCacheOverride.Value : settings.ExcludePythonCache;
            var result = AddonPackService.InstallFromDirectory(options.InstallAddonsFolder, secondaries, excludePythonCache, CancellationToken.None);
            return result.Errors == 0 ? 0 : 2;
        }
    }

    internal enum AppCommand
    {
        Show,
        Close,
        Sync
    }

    internal static class AppCommands
    {
        public static string MutexName
        {
            get { return @"Local\NVDASyncSingleInstance"; }
        }

        public static string GetEventName(AppCommand command)
        {
            return GetEventName(command, AppSettings.AppFolder);
        }

        public static string GetEventName(AppCommand command, string appFolder)
        {
            return "NvdaAddonSync-" + command + "-" + EventSuffix(appFolder);
        }

        public static EventWaitHandle CreateEvent(AppCommand command)
        {
            return new EventWaitHandle(false, EventResetMode.AutoReset, GetEventName(command));
        }

        public static bool Signal(AppCommand command)
        {
            return Signal(command, AppSettings.AppFolder);
        }

        public static bool Signal(AppCommand command, string appFolder)
        {
            try
            {
                EventWaitHandle existing;
                if (!EventWaitHandle.TryOpenExisting(GetEventName(command, appFolder), out existing))
                {
                    return false;
                }
                using (existing)
                {
                    return existing.Set();
                }
            }
            catch
            {
                return false;
            }
        }

        private static string EventSuffix(string appFolder)
        {
            var folder = string.IsNullOrWhiteSpace(appFolder) ? AppSettings.AppFolder : appFolder;
            folder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Math.Abs(folder.ToLowerInvariant().GetHashCode()).ToString("X");
        }
    }

    internal sealed class CommandLineOptions
    {
        public bool ShowHelp { get; private set; }
        public bool ShowVersion { get; private set; }
        public bool CloseRunning { get; private set; }
        public bool ShowRunning { get; private set; }
        public bool SyncRunning { get; private set; }
        public bool StartupLaunch { get; private set; }
        public bool SyncOnce { get; private set; }
        public bool SaveSettings { get; private set; }
        public string PrimaryFolder { get; private set; }
        public string DeleteOldExePath { get; private set; }
        public string ExportAddonPackFile { get; private set; }
        public string InstallAddonsFolder { get; private set; }
        public bool? DeleteStaleOverride { get; private set; }
        public bool? ExcludePythonCacheOverride { get; private set; }
        public bool? ExcludeLogFilesOverride { get; private set; }
        public bool AllComponents { get; private set; }
        public List<string> ComponentIds { get; private set; }
        public List<string> SecondaryFolders { get; private set; }

        public CommandLineOptions()
        {
            ComponentIds = new List<string>();
            SecondaryFolders = new List<string>();
        }

        public static CommandLineOptions Parse(string[] args)
        {
            var options = new CommandLineOptions();
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i].Trim();
                switch (arg.ToLowerInvariant())
                {
                    case "--help":
                    case "-?":
                    case "/?":
                        options.ShowHelp = true;
                        break;
                    case "--version":
                        options.ShowVersion = true;
                        break;
                    case "--close":
                        options.CloseRunning = true;
                        break;
                    case "--show":
                        options.ShowRunning = true;
                        break;
                    case "--sync-running":
                        options.SyncRunning = true;
                        break;
                    case "--startup-minimized":
                    case "--startup":
                        options.StartupLaunch = true;
                        break;
                    case "--delete-old-exe":
                        options.DeleteOldExePath = RequireValue(args, ref i, arg);
                        break;
                    case "--sync":
                        options.SyncOnce = true;
                        break;
                    case "--save":
                        options.SaveSettings = true;
                        break;
                    case "--delete-stale":
                        options.DeleteStaleOverride = true;
                        break;
                    case "--no-delete-stale":
                        options.DeleteStaleOverride = false;
                        break;
                    case "--exclude-python-cache":
                        options.ExcludePythonCacheOverride = true;
                        break;
                    case "--include-python-cache":
                        options.ExcludePythonCacheOverride = false;
                        break;
                    case "--exclude-log-files":
                        options.ExcludeLogFilesOverride = true;
                        break;
                    case "--include-log-files":
                        options.ExcludeLogFilesOverride = false;
                        break;
                    case "--all-components":
                        options.AllComponents = true;
                        break;
                    case "--component":
                        options.ComponentIds.Add(RequireValue(args, ref i, arg));
                        break;
                    case "--primary":
                        options.PrimaryFolder = RequireValue(args, ref i, arg);
                        break;
                    case "--secondary":
                        options.SecondaryFolders.Add(RequireValue(args, ref i, arg));
                        break;
                    case "--export-addon-pack":
                        options.ExportAddonPackFile = RequireValue(args, ref i, arg);
                        break;
                    case "--install-addons":
                        options.InstallAddonsFolder = RequireValue(args, ref i, arg);
                        break;
                }
            }
            return options;
        }

        private static string RequireValue(string[] args, ref int index, string option)
        {
            if (index + 1 >= args.Length)
            {
                throw new InvalidOperationException(option + " requires a value.");
            }
            index++;
            return args[index];
        }

        public List<SyncComponent> GetComponentsOrDefault(AppSettings settings)
        {
            if (AllComponents)
            {
                return SyncComponent.All;
            }
            if (ComponentIds.Count == 0)
            {
                return SyncComponent.FromSettings(settings);
            }
            var components = new List<SyncComponent>();
            foreach (var id in ComponentIds)
            {
                var component = SyncComponent.FromId(id);
                if (!components.Contains(component))
                {
                    components.Add(component);
                }
            }
            return components;
        }

        public static string HelpText
        {
            get
            {
                return string.Join(Environment.NewLine, new[]
                {
                    "NVDA Sync command line",
                    "",
                    "--show                 Show the running app from this folder.",
                    "--close                Close the running app from this folder.",
                    "--sync-running         Ask the running app to sync now.",
                    "--startup-minimized    Start quietly in the notification area.",
                    "--sync                 Run one sync without showing the UI.",
                    "--primary <folder>     Primary NVDA or add-ons folder for --sync.",
                    "--secondary <folder>   Secondary NVDA or add-ons folder. May be repeated up to five times.",
                    "--delete-stale         Delete stale files in secondary folders for this --sync.",
                    "--no-delete-stale      Do not delete stale files for this --sync.",
                    "--component <id>       Sync one component. May be repeated. IDs: addons, inputGestures, nvdaIni, speechDictionaries, configProfiles, otherConfigFiles, otherConfigFolders.",
                    "--all-components       Sync every supported component.",
                    "--export-addon-pack <file> Export installed add-on metadata from the primary folder.",
                    "--install-addons <folder> Install local add-ons from a folder into secondary folders only.",
                    "--exclude-python-cache Exclude __pycache__, .pyc, and .pyo files.",
                    "--include-python-cache Include Python cache files.",
                    "--exclude-log-files   Exclude .log files.",
                    "--include-log-files   Include .log files.",
                    "--save                 Save command-line folders/options to app settings.",
                    "--version              Show version information.",
                    "--help                 Show this help."
                });
            }
        }

        public static string JoinArguments(IEnumerable<string> arguments)
        {
            var parts = new List<string>();
            foreach (var argument in arguments)
            {
                parts.Add(QuoteArgument(argument));
            }
            return string.Join(" ", parts.ToArray());
        }

        private static string QuoteArgument(string value)
        {
            if (value == null)
            {
                return "\"\"";
            }
            if (value.Length == 0 || value.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0)
            {
                return "\"" + value.Replace("\"", "\\\"") + "\"";
            }
            return value;
        }
    }
}
