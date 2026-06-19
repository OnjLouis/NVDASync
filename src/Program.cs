using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace NvdaAddonSync
{
    internal static partial class Program
    {
        internal const string ProductName = "NVDA Sync";
        internal const string Version = "1.2.0";
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

            bool created;
            using (var mutex = new Mutex(true, AppCommands.MutexName, out created))
            {
                if (!created)
                {
                    AppCommands.Signal(AppCommand.Show);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
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
        private static readonly string Suffix = Math.Abs(AppSettings.AppFolder.ToLowerInvariant().GetHashCode()).ToString("X");

        public static string MutexName
        {
            get { return "NvdaAddonSync-" + Suffix; }
        }

        public static string GetEventName(AppCommand command)
        {
            return "NvdaAddonSync-" + command + "-" + Suffix;
        }

        public static EventWaitHandle CreateEvent(AppCommand command)
        {
            return new EventWaitHandle(false, EventResetMode.AutoReset, GetEventName(command));
        }

        public static bool Signal(AppCommand command)
        {
            try
            {
                EventWaitHandle existing;
                if (!EventWaitHandle.TryOpenExisting(GetEventName(command), out existing))
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
    }

    internal sealed class CommandLineOptions
    {
        public bool ShowHelp { get; private set; }
        public bool ShowVersion { get; private set; }
        public bool CloseRunning { get; private set; }
        public bool ShowRunning { get; private set; }
        public bool SyncRunning { get; private set; }
        public bool SyncOnce { get; private set; }
        public bool SaveSettings { get; private set; }
        public string PrimaryFolder { get; private set; }
        public string ExportAddonPackFile { get; private set; }
        public string InstallAddonsFolder { get; private set; }
        public bool? DeleteStaleOverride { get; private set; }
        public bool? ExcludePythonCacheOverride { get; private set; }
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
                    "--sync                 Run one sync without showing the UI.",
                    "--primary <folder>     Primary NVDA or add-ons folder for --sync.",
                    "--secondary <folder>   Secondary NVDA or add-ons folder. May be repeated up to five times.",
                    "--delete-stale         Delete stale files in secondaries for this --sync.",
                    "--no-delete-stale      Do not delete stale files for this --sync.",
                    "--component <id>       Sync one component. May be repeated. IDs: addons, inputGestures, nvdaIni, speechDictionaries, configProfiles, otherConfigFiles, otherConfigFolders.",
                    "--all-components       Sync every supported component.",
                    "--export-addon-pack <file> Export installed add-on metadata from the primary folder.",
                    "--install-addons <folder> Install local add-ons from a folder into secondary folders only.",
                    "--exclude-python-cache Exclude __pycache__, .pyc, and .pyo files.",
                    "--include-python-cache Include Python cache files.",
                    "--save                 Save command-line folders/options to app settings.",
                    "--version              Show version information.",
                    "--help                 Show this help."
                });
            }
        }
    }
}
