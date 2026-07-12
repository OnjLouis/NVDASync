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
        public List<SecondaryFolderProfile> SecondaryFolderProfiles { get; set; }

        [DataMember]
        public bool AutoSync { get; set; }

        [DataMember]
        public bool DeleteStaleItems { get; set; }

        [DataMember]
        public bool StartMinimized { get; set; }

        [DataMember]
        public bool SyncAddons { get; set; }

        [DataMember]
        public string DefaultAddonMode { get; set; }

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
        public bool SyncNvdaProgramFiles { get; set; }

        [DataMember]
        public bool CreateProgramBackups { get; set; }

        [DataMember]
        public bool ExcludePythonCache { get; set; }

        [DataMember]
        public bool ExcludeLogFiles { get; set; }

        [DataMember]
        public bool WriteLogFile { get; set; }

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
            SecondaryFolderProfiles = new List<SecondaryFolderProfile>();
            PrimaryFolder = GetDefaultPrimaryFolder();
            AutoSync = true;
            DeleteStaleItems = true;
            StartMinimized = false;
            SyncAddons = true;
            DefaultAddonMode = AddonSyncMode.All;
            SyncInputGestures = false;
            SyncNvdaIni = false;
            SyncSpeechDictionaries = false;
            SyncConfigProfiles = false;
            SyncOtherConfigFiles = false;
            SyncOtherConfigFolders = false;
            SyncNvdaProgramFiles = false;
            CreateProgramBackups = true;
            ExcludePythonCache = true;
            ExcludeLogFiles = true;
            WriteLogFile = false;
            SettingsVersion = 4;
            WindowWidth = 820;
            WindowHeight = 560;
            UpdateCheckFrequency = "Startup";
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
                    settings.NormalizeAfterLoad();
                    return settings;
                }
            }
            catch
            {
                return new AppSettings();
            }
        }

        private void NormalizeAfterLoad()
        {
            var loadedSettingsVersion = SettingsVersion;
            if (SecondaryFolders == null)
            {
                SecondaryFolders = new List<string>();
            }
            if (SecondaryFolderProfiles == null)
            {
                SecondaryFolderProfiles = new List<SecondaryFolderProfile>();
            }
            if (SecondaryFolderProfiles.Count == 0)
            {
                foreach (var folder in SecondaryFolders)
                {
                    if (!string.IsNullOrWhiteSpace(folder))
                    {
                        SecondaryFolderProfiles.Add(SecondaryFolderProfile.FromPath(folder));
                    }
                }
            }
            foreach (var profile in SecondaryFolderProfiles)
            {
                if (profile != null)
                {
                    profile.Normalize();
                }
            }
            if (PrimaryFolder == null)
            {
                PrimaryFolder = "";
            }
            if (loadedSettingsVersion < 1)
            {
                SyncAddons = true;
                ExcludePythonCache = true;
            }
            if (string.IsNullOrWhiteSpace(DefaultAddonMode))
            {
                DefaultAddonMode = AddonSyncMode.All;
            }
            DefaultAddonMode = AddonSyncMode.Normalize(DefaultAddonMode);
            if (loadedSettingsVersion < 2)
            {
                CreateProgramBackups = true;
            }
            if (loadedSettingsVersion < 3)
            {
                ExcludeLogFiles = true;
                foreach (var profile in SecondaryFolderProfiles)
                {
                    if (profile != null)
                    {
                        profile.ExcludeLogFiles = true;
                    }
                }
            }
            if (loadedSettingsVersion < 4)
            {
                WriteLogFile = false;
            }
            SettingsVersion = 4;
            if (!SyncAddons
                && !SyncInputGestures
                && !SyncNvdaIni
                && !SyncSpeechDictionaries
                && !SyncConfigProfiles
                && !SyncOtherConfigFiles
                && !SyncOtherConfigFolders
                && !SyncNvdaProgramFiles)
            {
                SyncAddons = true;
            }
            if (WindowWidth < 500)
            {
                WindowWidth = 820;
            }
            if (WindowHeight < 360)
            {
                WindowHeight = 560;
            }
            UpdateCheckFrequency = NormalizeUpdateCheckFrequency(UpdateCheckFrequency);
            SyncLegacySecondaryFolders();
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
            SyncLegacySecondaryFolders();
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

        public void SyncLegacySecondaryFolders()
        {
            if (SecondaryFolderProfiles == null)
            {
                SecondaryFolderProfiles = new List<SecondaryFolderProfile>();
            }
            SecondaryFolders = new List<string>();
            foreach (var profile in SecondaryFolderProfiles)
            {
                if (profile == null)
                {
                    continue;
                }
                profile.Normalize();
                if (!string.IsNullOrWhiteSpace(profile.Path))
                {
                    SecondaryFolders.Add(profile.Path);
                }
            }
        }
    }
}
