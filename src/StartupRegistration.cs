using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NvdaAddonSync
{
    internal static class StartupRegistration
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "NVDA Sync";

        public static void SetEnabled(bool enabled)
        {
            SetEnabledForPath(enabled, Application.ExecutablePath);
        }

        public static void SetEnabledForPath(bool enabled, string executablePath)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null) return;
                if (enabled)
                {
                    key.SetValue(ValueName, Quote(executablePath) + " --startup-minimized", RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }

        public static bool IsEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
            {
                var value = key == null ? null : key.GetValue(ValueName) as string;
                return string.Equals(CommandPath(value), Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static bool IsEnabledForDirectory(string folder)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
            {
                var value = key == null ? null : key.GetValue(ValueName) as string;
                var path = CommandPath(value);
                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(folder))
                {
                    return false;
                }
                var registeredFolder = System.IO.Path.GetDirectoryName(path);
                return string.Equals(
                    (registeredFolder ?? string.Empty).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
                    folder.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        private static string Unquote(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var trimmed = value.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
            {
                return trimmed.Substring(1, trimmed.Length - 2);
            }
            return trimmed;
        }

        private static string CommandPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var trimmed = value.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"')
            {
                var end = trimmed.IndexOf('"', 1);
                if (end > 1)
                {
                    return trimmed.Substring(1, end - 1);
                }
            }
            var space = trimmed.IndexOf(' ');
            return Unquote(space > 0 ? trimmed.Substring(0, space) : trimmed);
        }
    }
}
