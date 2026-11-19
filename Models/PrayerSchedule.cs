using System;
using System.Collections.Generic;
using System.Linq;

namespace Waqt.Models
{
    /// <summary>
    /// Represents the six standard Islamic prayer/time types.
    /// </summary>
    public enum PrayerType
    {
        Fajr,
        Sunrise,
        Dhuhr,
        Asr,
        Maghrib,
        Isha
    }

    /// <summary>
    /// Holds the type and time for a single prayer.
    /// </summary>
    public class PrayerTime
    {
        public PrayerType Type { get; set; }
        public TimeOnly Time { get; set; }

        /// <summary>
        /// Human-readable name derived from the enum value.
        /// </summary>
        public string Name => Type.ToString();
    }

    /// <summary>
    /// Hijri (Islamic calendar) date information returned by the Al-Adhan API.
    /// </summary>
    public class HijriDateInfo
    {
        public string Day { get; set; } = string.Empty;
        public string Month { get; set; } = string.Empty;
        public string Year { get; set; } = string.Empty;

        /// <summary>
        /// Calendar designation, typically "AH" (Anno Hegirae).
        /// </summary>
        public string Designation { get; set; } = "AH";

        /// <summary>
        /// Pre-formatted display string, e.g. "8 Jumada II 1448".
        /// </summary>
        public string FormattedDate { get; set; } = string.Empty;
    }

    /// <summary>
    /// All prayer times and metadata for a single Gregorian day.
    /// </summary>
    public class DailySchedule
    {
        public DateTime Date { get; set; }
        public HijriDateInfo Hijri { get; set; } = new();
        public List<PrayerTime> Prayers { get; set; } = new();
        public string Timezone { get; set; } = string.Empty;

        /// <summary>
        /// Returns the <see cref="PrayerTime"/> for the requested type, or null if not present.
        /// </summary>
        public PrayerTime? GetPrayer(PrayerType type) =>
            Prayers.FirstOrDefault(p => p.Type == type);

        /// <summary>
        /// Returns a full <see cref="DateTime"/> (date + prayer time) for the requested type.
        /// Throws if the prayer type is not found in the schedule.
        /// </summary>
        public DateTime GetPrayerDateTime(PrayerType type)
        {
            var prayer = GetPrayer(type);
            if (prayer is null)
            {
                throw new InvalidOperationException(
                    $"Prayer type '{type}' not found in the schedule for {Date:yyyy-MM-dd}.");
            }
            return Date.Date + prayer.Time.ToTimeSpan();
        }
    }

    /// <summary>
    /// A full month of daily prayer schedules for a specific city.
    /// </summary>
    public class MonthlySchedule
    {
        public string City { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string Timezone { get; set; } = string.Empty;
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public List<DailySchedule> Days { get; set; } = new();
    }

    /// <summary>
    /// Formatted display row for the Monthly Timetable UI.
    /// </summary>
    public class MonthlyDayRow
    {
        public int DayNumber { get; set; }
        public string DateString { get; set; } = string.Empty;
        public string HijriString { get; set; } = string.Empty;
        public string Fajr { get; set; } = string.Empty;
        public string Sunrise { get; set; } = string.Empty;
        public string Dhuhr { get; set; } = string.Empty;
        public string Asr { get; set; } = string.Empty;
        public string Maghrib { get; set; } = string.Empty;
        public string Isha { get; set; } = string.Empty;
        public bool IsToday { get; set; }
    }
}
