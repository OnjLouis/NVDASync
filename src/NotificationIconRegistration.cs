using System;
using Microsoft.Win32;

namespace NvdaAddonSync
{
    internal static class NotificationIconRegistration
    {
        private const string NotifyIconSettingsPath = @"Control Panel\NotifyIconSettings";

        public static void EnsurePromotedCurrentExecutablePath(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return;
            }
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(NotifyIconSettingsPath, true))
                {
                    if (root == null)
                    {
                        return;
                    }
                    if (PromoteExistingPath(root, executablePath))
                    {
                        return;
                    }
                    using (var subKey = root.CreateSubKey(StableKeyName(executablePath)))
                    {
                        if (subKey == null)
                        {
                            return;
                        }
                        subKey.SetValue("UID", 1, RegistryValueKind.DWord);
                        subKey.SetValue("ExecutablePath", executablePath, RegistryValueKind.String);
                        subKey.SetValue("InitialTooltip", Program.ProductName + ": Starting", RegistryValueKind.String);
                        subKey.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                    }
                }
            }
            catch
            {
            }
        }

        public static void MigrateLegacyExecutablePath(string oldExecutablePath, string newExecutablePath)
        {
            if (string.IsNullOrWhiteSpace(oldExecutablePath) || string.IsNullOrWhiteSpace(newExecutablePath))
            {
                return;
            }
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(NotifyIconSettingsPath, true))
                {
                    if (root == null)
                    {
                        return;
                    }
                    foreach (var name in root.GetSubKeyNames())
                    {
                        using (var subKey = root.OpenSubKey(name, true))
                        {
                            if (subKey == null)
                            {
                                continue;
                            }
                            var value = subKey.GetValue("ExecutablePath") as string;
                            if (!string.Equals(value, oldExecutablePath, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            subKey.SetValue("ExecutablePath", newExecutablePath, RegistryValueKind.String);
                            if (string.IsNullOrWhiteSpace(subKey.GetValue("InitialTooltip") as string))
                            {
                                subKey.SetValue("InitialTooltip", Program.ProductName, RegistryValueKind.String);
                            }
                            subKey.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static bool PromoteExistingPath(RegistryKey root, string executablePath)
        {
            var promoted = false;
            foreach (var name in root.GetSubKeyNames())
            {
                using (var subKey = root.OpenSubKey(name, true))
                {
                    if (subKey == null)
                    {
                        continue;
                    }
                    var value = subKey.GetValue("ExecutablePath") as string;
                    if (!string.Equals(value, executablePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(subKey.GetValue("InitialTooltip") as string))
                    {
                        subKey.SetValue("InitialTooltip", Program.ProductName + ": Starting", RegistryValueKind.String);
                    }
                    subKey.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                    promoted = true;
                }
            }
            return promoted;
        }

        private static string StableKeyName(string executablePath)
        {
            unchecked
            {
                const ulong offset = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;
                var hash = offset;
                foreach (var ch in executablePath.ToLowerInvariant())
                {
                    hash ^= ch;
                    hash *= prime;
                }
                return hash.ToString();
            }
        }
    }
}
