using System;
using Microsoft.Win32;

namespace Waqt.Services
{
    public static class StartupService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "Waqt";
        private const string LegacyAppName = "PrayerTray";

        public static bool IsStartupEnabled()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            if (key == null)
            {
                return false;
            }

            object? value = key.GetValue(AppName);
            return value != null;
        }

        public static void SetStartupEnabled(bool enabled)
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key == null)
            {
                return;
            }

            // Always clean up the legacy "PrayerTray" registry value if it exists
            try
            {
                if (key.GetValue(LegacyAppName) != null)
                {
                    key.DeleteValue(LegacyAppName, false);
                }
            }
            catch
            {
                // Ignore registry cleanup errors
            }

            if (enabled)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppName, exePath);
                }
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
    }
}
