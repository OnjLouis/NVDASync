using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace NvdaAddonSync
{
    internal enum PendingAddonWorkKind
    {
        RestartRequired,
        StaleRemoval,
        Uncertain
    }

    internal sealed class PendingAddonWorkItem
    {
        public string StateKey { get; set; }
        public string AddonName { get; set; }
        public string Summary { get; set; }
        public string Details { get; set; }
        public PendingAddonWorkKind Kind { get; set; }

        public bool CanRepair
        {
            get { return Kind == PendingAddonWorkKind.StaleRemoval; }
        }

        public override string ToString()
        {
            return Summary ?? string.Empty;
        }
    }

    internal sealed class PendingAddonStateReport
    {
        public string ConfigFolder { get; set; }
        public string StateFilePath { get; set; }
        public string Error { get; set; }
        public List<PendingAddonWorkItem> Items { get; private set; }

        public PendingAddonStateReport()
        {
            Items = new List<PendingAddonWorkItem>();
        }
    }

    internal sealed class PendingAddonStateRepairResult
    {
        public string BackupPath { get; set; }
        public List<string> RemovedEntries { get; private set; }

        public PendingAddonStateRepairResult()
        {
            RemovedEntries = new List<string>();
        }
    }

    internal static class NvdaAddonStateService
    {
        private const string PendingInstallSuffix = ".pendingInstall";
        private const string DeleteSuffix = ".delete";
        private const int MaximumStateFileBytes = 1024 * 1024;

        public static PendingAddonStateReport Inspect(string configFolder)
        {
            var report = new PendingAddonStateReport
            {
                ConfigFolder = configFolder ?? string.Empty,
                StateFilePath = Path.Combine(configFolder ?? string.Empty, "addonsState.json")
            };
            try
            {
                var installedNames = ReadInstalledAddonNames(configFolder);
                var stagedNames = ReadStagedAddonNames(configFolder, report);
                if (!File.Exists(report.StateFilePath))
                {
                    return report;
                }

                var state = ReadStateFile(report.StateFilePath);
                foreach (var entry in state)
                {
                    var keyKind = GetPendingKeyKind(entry.Key);
                    if (keyKind == PendingKeyKind.None || !HasValues(entry.Value))
                    {
                        continue;
                    }

                    List<string> names;
                    if (!TryReadStringArray(entry.Value, out names))
                    {
                        report.Items.Add(new PendingAddonWorkItem
                        {
                            StateKey = entry.Key,
                            Summary = entry.Key + " - cannot be repaired automatically",
                            Details = "The value in " + report.StateFilePath + " is not a normal list of add-on names. NVDA Sync will not alter an unfamiliar state structure.",
                            Kind = PendingAddonWorkKind.Uncertain
                        });
                        continue;
                    }

                    foreach (var name in names)
                    {
                        if (keyKind == PendingKeyKind.Remove &&
                            !installedNames.Contains(name) &&
                            !stagedNames.Contains(name))
                        {
                            report.Items.Add(new PendingAddonWorkItem
                            {
                                StateKey = entry.Key,
                                AddonName = name,
                                Summary = name + " - stale removal entry; safe to clear",
                                Details = "The " + entry.Key + " entry names " + name + ", but no installed add-on folder or matching .delete/.pendingInstall path exists. The removal has already completed or its state entry was left behind.",
                                Kind = PendingAddonWorkKind.StaleRemoval
                            });
                        }
                        else
                        {
                            report.Items.Add(new PendingAddonWorkItem
                            {
                                StateKey = entry.Key,
                                AddonName = name,
                                Summary = name + " - restart required (" + entry.Key + ")",
                                Details = "NVDA still has evidence of genuine pending add-on work for " + name + ". Restart this NVDA copy and review the item again afterward.",
                                Kind = PendingAddonWorkKind.RestartRequired
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                report.Error = ex.GetType().Name + ": " + ex.Message;
                report.Items.Clear();
                report.Items.Add(new PendingAddonWorkItem
                {
                    Summary = "State could not be verified",
                    Details = "NVDA Sync did not change anything because the add-on state could not be read safely. " + report.Error,
                    Kind = PendingAddonWorkKind.Uncertain
                });
            }
            return report;
        }

        public static PendingAddonStateRepairResult ClearStaleRemovals(string configFolder, IEnumerable<PendingAddonWorkItem> selectedItems)
        {
            var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in selectedItems ?? new PendingAddonWorkItem[0])
            {
                if (item != null && item.CanRepair && !string.IsNullOrWhiteSpace(item.AddonName))
                {
                    requested.Add(RepairKey(item.StateKey, item.AddonName));
                }
            }
            if (requested.Count == 0)
            {
                throw new InvalidOperationException("Select at least one stale removal entry.");
            }

            var freshReport = Inspect(configFolder);
            if (!string.IsNullOrWhiteSpace(freshReport.Error))
            {
                throw new InvalidOperationException("The add-on state changed or could not be verified. Refresh and try again.");
            }
            var stillSafe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in freshReport.Items)
            {
                if (item.CanRepair)
                {
                    stillSafe.Add(RepairKey(item.StateKey, item.AddonName));
                }
            }
            if (!requested.IsSubsetOf(stillSafe))
            {
                throw new InvalidOperationException("The pending add-on state changed. Nothing was altered; refresh the list and review it again.");
            }

            var statePath = Path.Combine(configFolder, "addonsState.json");
            var state = ReadStateFile(statePath);
            var result = new PendingAddonStateRepairResult();
            foreach (var entry in new List<KeyValuePair<string, object>>(state))
            {
                if (GetPendingKeyKind(entry.Key) != PendingKeyKind.Remove)
                {
                    continue;
                }
                var values = entry.Value as object[];
                if (values == null)
                {
                    continue;
                }
                var retained = new List<object>();
                foreach (var value in values)
                {
                    var name = value as string;
                    if (!string.IsNullOrWhiteSpace(name) && requested.Contains(RepairKey(entry.Key, name)))
                    {
                        result.RemovedEntries.Add(entry.Key + ": " + name);
                    }
                    else
                    {
                        retained.Add(value);
                    }
                }
                state[entry.Key] = retained.ToArray();
            }
            if (result.RemovedEntries.Count == 0)
            {
                throw new InvalidOperationException("No matching stale removal entries remained. Nothing was changed.");
            }

            var serializer = CreateSerializer();
            var rewritten = serializer.Serialize(state);
            serializer.DeserializeObject(rewritten);
            var tempPath = statePath + ".nvdasync-" + Guid.NewGuid().ToString("N") + ".tmp";
            result.BackupPath = NextBackupPath(statePath);
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(rewritten);
                    writer.Flush();
                    stream.Flush(true);
                }
                ReplaceWithBackup(tempPath, statePath, result.BackupPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            return result;
        }

        private static IDictionary<string, object> ReadStateFile(string statePath)
        {
            var info = new FileInfo(statePath);
            if (info.Length > MaximumStateFileBytes)
            {
                throw new InvalidDataException("addonsState.json exceeds the 1 MB safety limit.");
            }
            string json;
            using (var stream = new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                json = reader.ReadToEnd();
            }
            var state = CreateSerializer().DeserializeObject(json) as IDictionary<string, object>;
            if (state == null)
            {
                throw new InvalidDataException("addonsState.json does not contain a JSON object.");
            }
            return state;
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = MaximumStateFileBytes };
        }

        private static HashSet<string> ReadInstalledAddonNames(string configFolder)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var addonsFolder = Path.Combine(configFolder, "addons");
            if (!Directory.Exists(addonsFolder))
            {
                return names;
            }
            foreach (var directory in Directory.EnumerateDirectories(addonsFolder, "*", SearchOption.TopDirectoryOnly))
            {
                var folderName = Path.GetFileName(directory);
                if (folderName.EndsWith(PendingInstallSuffix, StringComparison.OrdinalIgnoreCase) ||
                    folderName.EndsWith(DeleteSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                names.Add(folderName);
                var manifestName = ReadManifestName(Path.Combine(directory, "manifest.ini"));
                if (!string.IsNullOrWhiteSpace(manifestName))
                {
                    names.Add(manifestName);
                }
            }
            return names;
        }

        private static HashSet<string> ReadStagedAddonNames(string configFolder, PendingAddonStateReport report)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var addonsFolder = Path.Combine(configFolder, "addons");
            if (!Directory.Exists(addonsFolder))
            {
                return names;
            }
            foreach (var path in Directory.EnumerateFileSystemEntries(addonsFolder, "*", SearchOption.TopDirectoryOnly))
            {
                var entryName = Path.GetFileName(path);
                var addonName = RemoveTransactionSuffix(entryName);
                if (string.Equals(addonName, entryName, StringComparison.Ordinal))
                {
                    continue;
                }
                names.Add(addonName);
                report.Items.Add(new PendingAddonWorkItem
                {
                    AddonName = addonName,
                    Summary = addonName + " - staged path; restart required",
                    Details = path + " is an NVDA add-on transaction path. NVDA Sync will not remove it because it may contain an installation or removal awaiting restart.",
                    Kind = PendingAddonWorkKind.RestartRequired
                });
            }
            return names;
        }

        private static string ReadManifestName(string manifestPath)
        {
            if (!File.Exists(manifestPath))
            {
                return string.Empty;
            }
            foreach (var rawLine in File.ReadLines(manifestPath))
            {
                var line = rawLine.Trim();
                var equals = line.IndexOf('=');
                if (equals <= 0 || !string.Equals(line.Substring(0, equals).Trim(), "name", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                return line.Substring(equals + 1).Trim().Trim('"', '\'');
            }
            return string.Empty;
        }

        private static bool TryReadStringArray(object value, out List<string> names)
        {
            names = new List<string>();
            var values = value as object[];
            if (values == null)
            {
                return false;
            }
            foreach (var item in values)
            {
                var name = item as string;
                if (string.IsNullOrWhiteSpace(name))
                {
                    return false;
                }
                names.Add(name);
            }
            return true;
        }

        private static bool HasValues(object value)
        {
            var array = value as object[];
            if (array != null)
            {
                return array.Length > 0;
            }
            return value != null && !string.IsNullOrWhiteSpace(Convert.ToString(value));
        }

        private static string RemoveTransactionSuffix(string name)
        {
            foreach (var suffix in new[] { PendingInstallSuffix, DeleteSuffix })
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return name.Substring(0, name.Length - suffix.Length);
                }
            }
            return name;
        }

        private static PendingKeyKind GetPendingKeyKind(string key)
        {
            var normalized = new StringBuilder();
            foreach (var character in key ?? string.Empty)
            {
                if (char.IsLetterOrDigit(character))
                {
                    normalized.Append(char.ToLowerInvariant(character));
                }
            }
            var value = normalized.ToString();
            if (!value.StartsWith("pending", StringComparison.Ordinal))
            {
                return PendingKeyKind.None;
            }
            if (value.Contains("remove"))
            {
                return PendingKeyKind.Remove;
            }
            return value.Contains("install") ? PendingKeyKind.Install : PendingKeyKind.None;
        }

        private static string RepairKey(string stateKey, string addonName)
        {
            return (stateKey ?? string.Empty) + "\0" + (addonName ?? string.Empty);
        }

        private static string NextBackupPath(string statePath)
        {
            var folder = Path.GetDirectoryName(statePath);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var path = Path.Combine(folder, "addonsState" + stamp + ".json");
            for (var index = 2; File.Exists(path); index++)
            {
                path = Path.Combine(folder, "addonsState" + stamp + "-" + index + ".json");
            }
            return path;
        }

        private static void ReplaceWithBackup(string tempPath, string statePath, string backupPath)
        {
            try
            {
                File.Replace(tempPath, statePath, backupPath, true);
            }
            catch (PlatformNotSupportedException)
            {
                ReplaceWithPortableFileSystemFallback(tempPath, statePath, backupPath);
            }
            catch (IOException)
            {
                if (!File.Exists(tempPath) || !File.Exists(statePath) || File.Exists(backupPath))
                {
                    throw;
                }
                ReplaceWithPortableFileSystemFallback(tempPath, statePath, backupPath);
            }
        }

        private static void ReplaceWithPortableFileSystemFallback(string tempPath, string statePath, string backupPath)
        {
            File.Copy(statePath, backupPath, false);
            try
            {
                File.Delete(statePath);
                File.Move(tempPath, statePath);
            }
            catch
            {
                if (!File.Exists(statePath) && File.Exists(backupPath))
                {
                    File.Copy(backupPath, statePath, false);
                }
                throw;
            }
        }

        private enum PendingKeyKind
        {
            None,
            Install,
            Remove
        }
    }
}
