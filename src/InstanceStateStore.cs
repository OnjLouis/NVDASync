using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;

namespace NvdaAddonSync
{
    [DataContract]
    internal sealed class InstanceState
    {
        [DataMember]
        public int Schema { get; set; }
        [DataMember]
        public string AppFolder { get; set; }
        [DataMember]
        public string ExecutablePath { get; set; }
        [DataMember]
        public int ProcessId { get; set; }
        [DataMember]
        public long UpdatedAtUtcTicks { get; set; }

        public InstanceState()
        {
            Schema = 1;
            AppFolder = string.Empty;
            ExecutablePath = string.Empty;
            ProcessId = 0;
        }
    }

    internal static class InstanceStateStore
    {
        private static string StatePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NVDASync",
                    "running-instance.json");
            }
        }

        public static InstanceState Load()
        {
            try
            {
                if (!File.Exists(StatePath))
                {
                    return new InstanceState();
                }
                using (var stream = File.OpenRead(StatePath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(InstanceState));
                    return serializer.ReadObject(stream) as InstanceState ?? new InstanceState();
                }
            }
            catch
            {
                return new InstanceState();
            }
        }

        public static void PublishCurrent(string appFolder)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatePath));
                var tempPath = StatePath + ".tmp";
                using (var stream = File.Create(tempPath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(InstanceState));
                    serializer.WriteObject(stream, new InstanceState
                    {
                        Schema = 1,
                        AppFolder = NormalizeFolder(appFolder),
                        ExecutablePath = Application.ExecutablePath,
                        ProcessId = Process.GetCurrentProcess().Id,
                        UpdatedAtUtcTicks = DateTime.UtcNow.Ticks
                    });
                }
                if (File.Exists(StatePath))
                {
                    File.Replace(tempPath, StatePath, null);
                }
                else
                {
                    File.Move(tempPath, StatePath);
                }
            }
            catch
            {
            }
        }

        public static void ClearIfCurrent(string appFolder)
        {
            try
            {
                var state = Load();
                if (state == null || state.ProcessId != Process.GetCurrentProcess().Id)
                {
                    return;
                }
                if (!SameFolder(state.AppFolder, appFolder))
                {
                    return;
                }
                if (File.Exists(StatePath))
                {
                    File.Delete(StatePath);
                }
            }
            catch
            {
            }
        }

        public static bool IsSameRunningFolder(string appFolder)
        {
            var state = Load();
            if (state == null || !IsProcessAlive(state.ProcessId))
            {
                return false;
            }
            return SameFolder(state.AppFolder, appFolder);
        }

        private static bool IsProcessAlive(int processId)
        {
            if (processId <= 0)
            {
                return false;
            }
            try
            {
                using (Process.GetProcessById(processId))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool SameFolder(string left, string right)
        {
            return string.Equals(NormalizeFolder(left), NormalizeFolder(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
