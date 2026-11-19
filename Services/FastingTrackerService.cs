using System;
using Waqt.Models;

namespace Waqt.Services
{
    public class FastingInfo
    {
        public bool IsDaytimeFast { get; set; }
        public string StatusTitle { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
        public string TargetTimeFormatted { get; set; } = string.Empty;
        public TimeSpan TimeRemaining { get; set; }
        public string CountdownFormatted { get; set; } = string.Empty;
        public string MidnightTimeFormatted { get; set; } = string.Empty;
        public string TahajjudTimeFormatted { get; set; } = string.Empty;
        public bool IsSunnahFastingDay { get; set; }
        public string SunnahNote { get; set; } = string.Empty;
    }

    public static class FastingTrackerService
    {
        public static FastingInfo Calculate(DailySchedule? today, DailySchedule? tomorrow, bool use12Hour)
        {
            var info = new FastingInfo();
            if (today == null) return info;

            DateTime now = DateTime.Now;
            DateTime fajrToday = today.GetPrayerDateTime(PrayerType.Fajr);
            DateTime maghribToday = today.GetPrayerDateTime(PrayerType.Maghrib);
            DateTime fajrTomorrow = tomorrow?.GetPrayerDateTime(PrayerType.Fajr) ?? fajrToday.AddDays(1);

            // Islamic Midnight & Tahajjud Calculation
            // Night spans from Maghrib today to Fajr tomorrow
            TimeSpan nightSpan = fajrTomorrow - maghribToday;
            if (nightSpan > TimeSpan.Zero)
            {
                DateTime islamicMidnight = maghribToday + TimeSpan.FromTicks(nightSpan.Ticks / 2);
                DateTime tahajjudStart = maghribToday + TimeSpan.FromTicks((nightSpan.Ticks * 2) / 3);

                info.MidnightTimeFormatted = FormatTime(islamicMidnight.TimeOfDay, use12Hour);
                info.TahajjudTimeFormatted = FormatTime(tahajjudStart.TimeOfDay, use12Hour);
            }

            // Check Sunnah fasting
            DayOfWeek dow = now.DayOfWeek;
            if (dow == DayOfWeek.Monday)
            {
                info.IsSunnahFastingDay = true;
                info.SunnahNote = "Monday Sunnah Fast";
            }
            else if (dow == DayOfWeek.Thursday)
            {
                info.IsSunnahFastingDay = true;
                info.SunnahNote = "Thursday Sunnah Fast";
            }
            else if (int.TryParse(today.Hijri?.Day, out int hDay) && (hDay == 13 || hDay == 14 || hDay == 15))
            {
                info.IsSunnahFastingDay = true;
                info.SunnahNote = $"White Day Fast ({hDay} {today.Hijri?.Month})";
            }
            else if (today.Hijri?.Month != null && today.Hijri.Month.Contains("Rama", StringComparison.OrdinalIgnoreCase))
            {
                info.IsSunnahFastingDay = true;
                info.SunnahNote = "Holy Month of Ramadan";
            }

            // Determine if we are currently in Fasting hours (between Fajr and Maghrib)
            if (now >= fajrToday && now < maghribToday)
            {
                // Day fasting: countdown to Iftar (Maghrib)
                info.IsDaytimeFast = true;
                info.StatusTitle = "Fasting in Progress";
                info.TargetName = "Iftar (Maghrib)";
                info.TargetTimeFormatted = FormatTime(maghribToday.TimeOfDay, use12Hour);
                info.TimeRemaining = maghribToday - now;
            }
            else if (now < fajrToday)
            {
                // Night before Fajr: countdown to Suhoor End (Fajr)
                info.IsDaytimeFast = false;
                info.StatusTitle = "Suhoor Time";
                info.TargetName = "Suhoor Ends (Fajr)";
                info.TargetTimeFormatted = FormatTime(fajrToday.TimeOfDay, use12Hour);
                info.TimeRemaining = fajrToday - now;
            }
            else
            {
                // After Maghrib: countdown to tomorrow's Suhoor End (Fajr)
                info.IsDaytimeFast = false;
                info.StatusTitle = "Iftar Completed";
                info.TargetName = "Tomorrow's Suhoor";
                info.TargetTimeFormatted = FormatTime(fajrTomorrow.TimeOfDay, use12Hour);
                info.TimeRemaining = fajrTomorrow - now;
            }

            info.CountdownFormatted = $"{(int)info.TimeRemaining.TotalHours:D2}:{info.TimeRemaining.Minutes:D2}:{info.TimeRemaining.Seconds:D2}";
            return info;
        }

        private static string FormatTime(TimeSpan ts, bool use12Hour)
        {
            var t = TimeOnly.FromTimeSpan(ts);
            return use12Hour ? t.ToString("hh:mm tt") : t.ToString("HH:mm");
        }
    }
}
