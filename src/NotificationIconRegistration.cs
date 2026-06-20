using System;
using Microsoft.Win32;

namespace NvdaAddonSync
{
    internal static class NotificationIconRegistration
    {
        private const string NotifyIconSettingsPath = @"Control Panel\NotifyIconSettings";

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
    }
}
