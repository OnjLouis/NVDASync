using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace NvdaAddonSync
{
    [DataContract]
    internal sealed class AppSettings
    {
        [DataMember]
        public string PrimaryFolder { get; set; }

        [DataMember]
        public List<string> SecondaryFolders { get; set; }

        [DataMember]
        public bool AutoSync { get; set; }

        [DataMember]
        public bool DeleteStaleItems { get; set; }

        [DataMember]
        public bool StartMinimized { get; set; }

        [DataMember]
        public bool SyncAddons { get; set; }

        [DataMember]
        public bool SyncInputGestures { get; set; }

        [DataMember]
        public bool SyncNvdaIni { get; set; }

        [DataMember]
        public bool SyncSpeechDictionaries { get; set; }

        [DataMember]
        public bool SyncConfigProfiles { get; set; }

        [DataMember]
        public bool SyncOtherConfigFiles { get; set; }

        [DataMember]
        public bool SyncOtherConfigFolders { get; set; }

        [DataMember]
        public bool ExcludePythonCache { get; set; }

        [DataMember]
        public int SettingsVersion { get; set; }

        [DataMember]
        public int WindowLeft { get; set; }

        [DataMember]
        public int WindowTop { get; set; }

        [DataMember]
        public int WindowWidth { get; set; }

        [DataMember]
        public int WindowHeight { get; set; }

        [DataMember]
        public int LastPreferencesTab { get; set; }

        [DataMember]
        public string UpdateCheckFrequency { get; set; }

        [DataMember]
        public bool InstallUpdatesSilently { get; set; }

        [DataMember]
        public bool RunAtStartup { get; set; }

        public AppSettings()
        {
            SecondaryFolders = new List<string>();
            PrimaryFolder = GetDefaultPrimaryFolder();
            AutoSync = true;
            DeleteStaleItems = true;
            StartMinimized = false;
            SyncAddons = true;
            SyncInputGestures = false;
            SyncNvdaIni = false;
            SyncSpeechDictionaries = false;
            SyncConfigProfiles = false;
            SyncOtherConfigFiles = false;
            SyncOtherConfigFolders = false;
            ExcludePythonCache = true;
            SettingsVersion = 1;
            WindowWidth = 820;
            WindowHeight = 560;
            UpdateCheckFrequency = "Never";
            InstallUpdatesSilently = false;
            RunAtStartup = false;
        }

        private static string GetDefaultPrimaryFolder()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var nvdaFolder = Path.Combine(appData, "nvda");
            return Directory.Exists(nvdaFolder) ? nvdaFolder : "";
        }

        public static string AppFolder
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string SettingsFolder
        {
            get { return Path.Combine(AppFolder, "Settings"); }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(SettingsFolder, "settings.json"); }
        }

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return new AppSettings();
                }
                using (var stream = File.OpenRead(SettingsPath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    var settings = serializer.ReadObject(stream) as AppSettings;
                    if (settings == null)
                    {
                        return new AppSettings();
                    }
                    if (settings.SecondaryFolders == null)
                    {
                        settings.SecondaryFolders = new List<string>();
                    }
                    if (settings.PrimaryFolder == null)
                    {
                        settings.PrimaryFolder = "";
                    }
                    if (settings.SettingsVersion < 1)
                    {
                        settings.SyncAddons = true;
                        settings.ExcludePythonCache = true;
                        settings.SettingsVersion = 1;
                    }
                    if (!settings.SyncAddons
                        && !settings.SyncInputGestures
                        && !settings.SyncNvdaIni
                        && !settings.SyncSpeechDictionaries
                        && !settings.SyncConfigProfiles
                        && !settings.SyncOtherConfigFiles
                        && !settings.SyncOtherConfigFolders)
                    {
                        settings.SyncAddons = true;
                    }
                    if (settings.WindowWidth < 500)
                    {
                        settings.WindowWidth = 820;
                    }
                    if (settings.WindowHeight < 360)
                    {
                        settings.WindowHeight = 560;
                    }
                    settings.UpdateCheckFrequency = NormalizeUpdateCheckFrequency(settings.UpdateCheckFrequency);
                    return settings;
                }
            }
            catch
            {
                return new AppSettings();
            }
        }

        public static string NormalizeUpdateCheckFrequency(string value)
        {
            if (string.Equals(value, "At startup", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "Startup", StringComparison.OrdinalIgnoreCase))
            {
                return "Startup";
            }
            if (string.Equals(value, "Hourly", StringComparison.OrdinalIgnoreCase)) return "Hourly";
            if (string.Equals(value, "Daily", StringComparison.OrdinalIgnoreCase)) return "Daily";
            return "Never";
        }

        public void Save()
        {
            Directory.CreateDirectory(SettingsFolder);
            var tempPath = SettingsPath + ".tmp";
            using (var stream = File.Create(tempPath))
            {
                var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                serializer.WriteObject(stream, this);
            }
            if (File.Exists(SettingsPath))
            {
                File.Replace(tempPath, SettingsPath, null);
            }
            else
            {
                File.Move(tempPath, SettingsPath);
            }
        }
    }
}
