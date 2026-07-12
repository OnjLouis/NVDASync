using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace NvdaAddonSync
{
    internal static class AddonSyncMode
    {
        public const string All = "All";
        public const string ExistingOnly = "ExistingOnly";
        public const string None = "None";

        public static string Normalize(string value)
        {
            if (string.Equals(value, ExistingOnly, StringComparison.OrdinalIgnoreCase)) return ExistingOnly;
            if (string.Equals(value, None, StringComparison.OrdinalIgnoreCase)) return None;
            return All;
        }

        public static string DisplayName(string value)
        {
            value = Normalize(value);
            if (value == ExistingOnly) return "Update existing add-ons only";
            if (value == None) return "Do not sync add-ons";
            return "Sync all add-ons";
        }
    }

    [DataContract]
    internal sealed class SecondaryFolderProfile
    {
        [DataMember]
        public string Path { get; set; }

        [DataMember]
        public bool UseGlobalDefaults { get; set; }

        [DataMember]
        public bool SyncAddons { get; set; }

        [DataMember]
        public string AddonMode { get; set; }

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
        public bool DeleteStaleItems { get; set; }

        [DataMember]
        public bool ExcludePythonCache { get; set; }

        [DataMember]
        public bool ExcludeLogFiles { get; set; }

        public SecondaryFolderProfile()
        {
            Path = "";
            UseGlobalDefaults = true;
            SyncAddons = true;
            AddonMode = AddonSyncMode.All;
            ExcludePythonCache = true;
            ExcludeLogFiles = true;
            DeleteStaleItems = true;
            CreateProgramBackups = true;
        }

        public static SecondaryFolderProfile FromPath(string path)
        {
            return new SecondaryFolderProfile { Path = path ?? "" };
        }

        public SecondaryFolderProfile Clone()
        {
            return (SecondaryFolderProfile)MemberwiseClone();
        }

        public void Normalize()
        {
            if (Path == null) Path = "";
            AddonMode = AddonSyncMode.Normalize(AddonMode);
        }

        public SecondaryFolderPolicy Resolve(AppSettings settings)
        {
            Normalize();
            if (UseGlobalDefaults)
            {
                return new SecondaryFolderPolicy
                {
                    Path = Path,
                    Components = SyncComponent.FromSettings(settings),
                    AddonMode = settings.SyncAddons ? AddonSyncMode.Normalize(settings.DefaultAddonMode) : AddonSyncMode.None,
                    SyncNvdaProgramFiles = settings.SyncNvdaProgramFiles,
                    CreateProgramBackups = settings.CreateProgramBackups,
                    DeleteStaleItems = settings.DeleteStaleItems,
                    ExcludePythonCache = settings.ExcludePythonCache,
                    ExcludeLogFiles = settings.ExcludeLogFiles
                };
            }

            var components = new List<SyncComponent>();
            if (SyncAddons && AddonSyncMode.Normalize(AddonMode) != AddonSyncMode.None) components.Add(SyncComponent.Addons);
            if (SyncInputGestures) components.Add(SyncComponent.InputGestures);
            if (SyncNvdaIni) components.Add(SyncComponent.NvdaIni);
            if (SyncSpeechDictionaries) components.Add(SyncComponent.SpeechDictionaries);
            if (SyncConfigProfiles) components.Add(SyncComponent.ConfigProfiles);
            if (SyncOtherConfigFiles) components.Add(SyncComponent.OtherConfigFiles);
            if (SyncOtherConfigFolders) components.Add(SyncComponent.OtherConfigFolders);

            return new SecondaryFolderPolicy
            {
                Path = Path,
                Components = components,
                AddonMode = SyncAddons ? AddonSyncMode.Normalize(AddonMode) : AddonSyncMode.None,
                SyncNvdaProgramFiles = SyncNvdaProgramFiles,
                CreateProgramBackups = CreateProgramBackups,
                DeleteStaleItems = DeleteStaleItems,
                ExcludePythonCache = ExcludePythonCache,
                ExcludeLogFiles = ExcludeLogFiles
            };
        }

        public override string ToString()
        {
            var displayPath = string.IsNullOrWhiteSpace(Path) ? "(not set)" : Path;
            return UseGlobalDefaults ? displayPath + " - defaults" : displayPath + " - custom";
        }
    }

    internal sealed class SecondaryFolderPolicy
    {
        public string Path { get; set; }
        public List<SyncComponent> Components { get; set; }
        public string AddonMode { get; set; }
        public bool SyncNvdaProgramFiles { get; set; }
        public bool CreateProgramBackups { get; set; }
        public bool DeleteStaleItems { get; set; }
        public bool ExcludePythonCache { get; set; }
        public bool ExcludeLogFiles { get; set; }

        public bool HasWork
        {
            get { return SyncNvdaProgramFiles || (Components != null && Components.Count > 0); }
        }
    }
}
