using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Waqt.Models
{
    /// <summary>
    /// Persistent application settings for Waqt.
    /// Serialized to / deserialized from %LocalAppData%\Waqt\settings.json.
    /// </summary>
    public class AppSettings
    {
        // ── Location & calculation ───────────────────────────────────────
        public string City { get; set; } = "Sanliurfa";
        public string Country { get; set; } = "Turkey";
        public double? Latitude { get; set; } = 37.1674;
        public double? Longitude { get; set; } = 38.7955;

        /// <summary>
        /// Al-Adhan calculation method ID.  13 = Diyanet İşleri Başkanlığı (Turkey).
        /// </summary>
        public int CalculationMethod { get; set; } = 13;

        /// <summary>
        /// Asr juristic school.  0 = Shafi'i (standard), 1 = Hanafi.
        /// </summary>
        public int AsrJuristic { get; set; } = 0;

        // ── Display & Formatting ─────────────────────────────────────────
        public bool Use12HourFormat { get; set; } = false;
        public bool ShowFastingCard { get; set; } = false;
        public bool ShowDailyVerse { get; set; } = true;

        /// <summary>
        /// Manual minute offsets (+/- 15 mins) for local mosque calibration.
        /// </summary>
        public Dictionary<PrayerType, int> PrayerOffsets { get; set; } = new()
        {
            { PrayerType.Fajr,    0 },
            { PrayerType.Sunrise, 0 },
            { PrayerType.Dhuhr,   0 },
            { PrayerType.Asr,     0 },
            { PrayerType.Maghrib, 0 },
            { PrayerType.Isha,    0 }
        };

        // ── Notifications & audio ────────────────────────────────────────
        public bool ToastNotificationsEnabled { get; set; } = true;
        public bool PlayAthanEnabled { get; set; } = true;
        public int PreReminderMinutes { get; set; } = 0;
        public string? CustomAthanFilePath { get; set; }
        public double Volume { get; set; } = 0.8;

        // ── Behaviour ────────────────────────────────────────────────────
        public bool LaunchOnStartup { get; set; } = false;
        public bool IsMuted { get; set; } = false;

        /// <summary>
        /// Per-prayer alert toggles.  Sunrise is off by default.
        /// </summary>
        public Dictionary<PrayerType, bool> PrayerAlerts { get; set; } = new()
        {
            { PrayerType.Fajr,    true  },
            { PrayerType.Sunrise, false },
            { PrayerType.Dhuhr,   true  },
            { PrayerType.Asr,     true  },
            { PrayerType.Maghrib, true  },
            { PrayerType.Isha,    true  }
        };

        // ── Persistence helpers ──────────────────────────────────────────

        /// <summary>
        /// Shared serializer options: indented JSON with enum-as-string support.
        /// </summary>
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Full path to the settings file on disk.
        /// </summary>
        private static string SettingsFilePath =>
            Path.Combine(GetAppDataPath(), "settings.json");

        /// <summary>
        /// Returns (and creates if necessary) the per-user data directory
        /// at %LocalAppData%\Waqt.
        /// </summary>
        public static string GetAppDataPath()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Waqt");

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // Migrate files from legacy PrayerTray folder if present
            try
            {
                string oldFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PrayerTray");

                if (Directory.Exists(oldFolder))
                {
                    foreach (string file in Directory.GetFiles(oldFolder))
                    {
                        string fileName = Path.GetFileName(file);
                        string destFile = Path.Combine(folder, fileName);
                        if (!File.Exists(destFile))
                        {
                            File.Copy(file, destFile, overwrite: false);
                        }
                    }
                }
            }
            catch
            {
                // Ignore transient copy errors
            }

            return folder;
        }

        /// <summary>
        /// Loads settings from disk.  Returns a fresh <see cref="AppSettings"/>
        /// instance with defaults if the file is missing or corrupt.
        /// </summary>
        public static AppSettings Load()
        {
            try
            {
                string path = SettingsFilePath;
                if (!File.Exists(path))
                {
                    return new AppSettings();
                }

                string json = File.ReadAllText(path);
                AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);

                if (settings is null)
                {
                    return new AppSettings();
                }

                // Ensure every PrayerType key exists in the dictionary
                // (guards against settings saved by an older version).
                foreach (PrayerType pt in Enum.GetValues<PrayerType>())
                {
                    settings.PrayerAlerts.TryAdd(pt, pt != PrayerType.Sunrise);
                    settings.PrayerOffsets.TryAdd(pt, 0);
                }

                return settings;
            }
            catch (Exception)
            {
                // File corrupt or schema changed — fall back to defaults.
                return new AppSettings();
            }
        }

        /// <summary>
        /// Persists the current settings to disk as indented JSON.
        /// </summary>
        public void Save()
        {
            try
            {
                string path = SettingsFilePath;
                string directory = Path.GetDirectoryName(path)!;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(this, _jsonOptions);
                File.WriteAllText(path, json);
            }
            catch (Exception)
            {
                // Silently swallow — the app can still run with in-memory settings.
                // A production version could log this via Debug/Trace.
            }
        }
    }
}
