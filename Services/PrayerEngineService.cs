using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using Waqt.Models;

namespace Waqt.Services
{
    /// <summary>
    /// The core prayer timing engine. Ticks every 250ms, determines current/next prayer,
    /// calculates countdown and progress, and fires events for UI updates and notifications.
    /// </summary>
    public class PrayerEngineService
    {
        // ───────────────────────── Events ─────────────────────────

        /// <summary>Fires every tick (250ms) so the UI can refresh.</summary>
        public event Action? OnStateUpdated;

        /// <summary>Fires exactly once when a prayer time is reached.</summary>
        public event Action<PrayerType>? OnPrayerTimeReached;

        /// <summary>Fires exactly once at the configured pre-reminder time before a prayer.</summary>
        public event Action<PrayerType>? OnPreReminderReached;

        // ───────────────────────── Public Properties ─────────────────────────

        public DailySchedule? TodaySchedule { get; private set; }
        public DailySchedule? TomorrowSchedule { get; private set; }

        /// <summary>The most recent prayer that has already passed.</summary>
        public PrayerType? CurrentPrayerType { get; private set; }

        /// <summary>The upcoming prayer.</summary>
        public PrayerType? NextPrayerType { get; private set; }

        /// <summary>The exact DateTime of the next prayer.</summary>
        public DateTime? NextPrayerDateTime { get; private set; }

        /// <summary>Time remaining until the next prayer.</summary>
        public TimeSpan? TimeUntilNext { get; private set; }

        /// <summary>Progress from 0.0 to 1.0 between the previous prayer and the next.</summary>
        public double ProgressPercentage { get; private set; }

        /// <summary>Whole remaining minutes until the next prayer (for tray icon).</summary>
        public int RemainingMinutes { get; private set; }

        // ───────────────────────── Private Fields ─────────────────────────

        private readonly DispatcherTimer _timer;
        private MonthlySchedule? _currentMonth;
        private MonthlySchedule? _nextMonth;

        /// <summary>Tracks fired prayer alerts as "yyyy-MM-dd_PrayerType" to avoid duplicates.</summary>
        private readonly HashSet<string> _firedAlerts = new();

        /// <summary>Tracks fired pre-reminders as "yyyy-MM-dd_PrayerType_pre" to avoid duplicates.</summary>
        private readonly HashSet<string> _firedReminders = new();

        /// <summary>Pre-reminder lead time in minutes (read from settings on each tick).</summary>
        private int _preReminderMinutes;

        /// <summary>Per-prayer minute calibration offsets.</summary>
        private Dictionary<PrayerType, int>? _prayerOffsets;

        // The ordered list of alertable prayer types (Sunrise excluded from alerts but included in timeline).
        private static readonly PrayerType[] AllPrayerTypesOrdered =
        {
            PrayerType.Fajr,
            PrayerType.Sunrise,
            PrayerType.Dhuhr,
            PrayerType.Asr,
            PrayerType.Maghrib,
            PrayerType.Isha
        };

        // ───────────────────────── Constructor ─────────────────────────

        public PrayerEngineService()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _timer.Tick += Tick;
        }

        // ───────────────────────── Public Methods ─────────────────────────

        /// <summary>
        /// Loads the monthly schedules and refreshes today/tomorrow references.
        /// </summary>
        /// <param name="current">The current month's schedule (required).</param>
        /// <param name="next">The next month's schedule (may be null; needed for month-boundary rollover).</param>
        public void LoadSchedules(MonthlySchedule current, MonthlySchedule? next)
        {
            _currentMonth = current;
            _nextMonth = next;

            // Reset fired-event trackers
            _firedAlerts.Clear();
            _firedReminders.Clear();

            RefreshDaySchedules();

            // Mark all past prayers for today as already fired so we NEVER alert for previous salah upon launch/reload
            DateTime now = DateTime.Now;
            string dateKey = now.ToString("yyyy-MM-dd");
            if (TodaySchedule != null)
            {
                var timeline = BuildTimeline(TodaySchedule);
                foreach (var (type, prayerDt) in timeline)
                {
                    if (now >= prayerDt)
                    {
                        _firedAlerts.Add($"{dateKey}_{type}");
                        _firedReminders.Add($"{dateKey}_{type}_pre");
                    }
                    else if (_preReminderMinutes > 0 && now >= prayerDt.AddMinutes(-_preReminderMinutes))
                    {
                        // Past the pre-reminder threshold at launch time: mark pre-reminder as already fired
                        _firedReminders.Add($"{dateKey}_{type}_pre");
                    }
                }
            }
        }

        /// <summary>Starts the 250ms tick timer.</summary>
        public void Start()
        {
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
        }

        /// <summary>Stops the tick timer.</summary>
        public void Stop()
        {
            if (_timer.IsEnabled)
            {
                _timer.Stop();
            }
        }

        /// <summary>
        /// Allows the caller to push an updated pre-reminder setting without reloading schedules.
        /// </summary>
        public void SetPreReminderMinutes(int minutes)
        {
            _preReminderMinutes = Math.Max(0, minutes);
        }

        public void SetPrayerOffsets(Dictionary<PrayerType, int>? offsets)
        {
            _prayerOffsets = offsets;
        }

        // ───────────────────────── Core Tick ─────────────────────────

        private void Tick(object? sender, EventArgs e)
        {
            DateTime now = DateTime.Now;

            // ── 1. Ensure we have a schedule for today ──
            RefreshDaySchedulesIfNeeded(now);

            if (TodaySchedule == null)
            {
                // No data at all — zero out state and still fire the UI update.
                CurrentPrayerType = null;
                NextPrayerType = null;
                NextPrayerDateTime = null;
                TimeUntilNext = null;
                ProgressPercentage = 0.0;
                RemainingMinutes = 0;
                OnStateUpdated?.Invoke();
                return;
            }

            // ── 2. Build a time-line of today's prayers as absolute DateTimes ──
            List<(PrayerType Type, DateTime Time)> todayTimeline = BuildTimeline(TodaySchedule);

            if (todayTimeline.Count == 0)
            {
                OnStateUpdated?.Invoke();
                return;
            }

            // ── 3. Determine current and next prayer ──
            PrayerType? currentPrayer = null;
            PrayerType? nextPrayer = null;
            DateTime? nextPrayerDt = null;
            DateTime? previousPrayerDt = null;

            // Find the last prayer that has already passed and the first upcoming one.
            for (int i = todayTimeline.Count - 1; i >= 0; i--)
            {
                if (now >= todayTimeline[i].Time)
                {
                    currentPrayer = todayTimeline[i].Type;
                    previousPrayerDt = todayTimeline[i].Time;

                    // The next prayer is the one after this index, if it exists today.
                    if (i + 1 < todayTimeline.Count)
                    {
                        nextPrayer = todayTimeline[i + 1].Type;
                        nextPrayerDt = todayTimeline[i + 1].Time;
                    }
                    break;
                }
            }

            // ── 4. Handle edge cases ──

            // 4a. Before Fajr — no prayer has passed today yet.
            if (currentPrayer == null)
            {
                // Conceptually the last prayer is Isha from yesterday.
                currentPrayer = PrayerType.Isha;

                // Try to get yesterday's Isha as the "previous" anchor for progress.
                DailySchedule? yesterdaySchedule = FindDaySchedule(now.Date.AddDays(-1));
                if (yesterdaySchedule != null)
                {
                    PrayerTime? yesterdayIsha = yesterdaySchedule.GetPrayer(PrayerType.Isha);
                    if (yesterdayIsha != null)
                    {
                        previousPrayerDt = now.Date.AddDays(-1) + yesterdayIsha.Time.ToTimeSpan();
                    }
                }

                // Next prayer is today's first (Fajr).
                nextPrayer = todayTimeline[0].Type;
                nextPrayerDt = todayTimeline[0].Time;
            }

            // 4b. Post-Isha rollover — all of today's prayers have passed.
            if (nextPrayer == null || nextPrayerDt == null)
            {
                // We're past the last prayer of the day (Isha).
                currentPrayer = PrayerType.Isha;
                previousPrayerDt ??= todayTimeline.Last().Time;

                // Next prayer is tomorrow's Fajr.
                nextPrayer = PrayerType.Fajr;
                nextPrayerDt = GetTomorrowFajrDateTime(now);
            }

            // ── 5. Calculate countdown & progress ──
            CurrentPrayerType = currentPrayer;
            NextPrayerType = nextPrayer;
            NextPrayerDateTime = nextPrayerDt;

            if (nextPrayerDt.HasValue)
            {
                TimeSpan remaining = nextPrayerDt.Value - now;
                if (remaining < TimeSpan.Zero)
                    remaining = TimeSpan.Zero;

                TimeUntilNext = remaining;
                RemainingMinutes = Math.Max(0, (int)Math.Ceiling(remaining.TotalMinutes));
            }
            else
            {
                TimeUntilNext = TimeSpan.Zero;
                RemainingMinutes = 0;
            }

            // Progress: how far we are between previousPrayerDt and nextPrayerDt.
            if (previousPrayerDt.HasValue && nextPrayerDt.HasValue && nextPrayerDt.Value > previousPrayerDt.Value)
            {
                double totalSpan = (nextPrayerDt.Value - previousPrayerDt.Value).TotalSeconds;
                double elapsed = (now - previousPrayerDt.Value).TotalSeconds;
                ProgressPercentage = Math.Clamp(elapsed / totalSpan, 0.0, 1.0);
            }
            else
            {
                ProgressPercentage = 0.0;
            }

            // ── 6. Fire one-shot events ──
            FirePrayerEvents(now, todayTimeline);

            // ── 7. Notify UI ──
            OnStateUpdated?.Invoke();
        }

        // ───────────────────────── Event Firing ─────────────────────────

        /// <summary>
        /// Checks every prayer in today's timeline and fires OnPrayerTimeReached / OnPreReminderReached
        /// exactly once per prayer per day.
        /// </summary>
        private void FirePrayerEvents(DateTime now, List<(PrayerType Type, DateTime Time)> timeline)
        {
            string dateKey = now.ToString("yyyy-MM-dd");

            foreach (var (type, prayerDt) in timeline)
            {
                // Sunrise is not an alertable prayer time
                if (type == PrayerType.Sunrise) continue;

                // ── Prayer-time alert ──
                string alertKey = $"{dateKey}_{type}";
                if (!_firedAlerts.Contains(alertKey))
                {
                    if (now >= prayerDt)
                    {
                        _firedAlerts.Add(alertKey);

                        // Only notify if the prayer time arrived in real-time (within the last 2 minutes).
                        // Never fire notifications or athan for previous prayers that already passed.
                        if (now <= prayerDt.AddMinutes(2))
                        {
                            OnPrayerTimeReached?.Invoke(type);
                        }
                    }
                }

                // ── Pre-reminder alert ──
                if (_preReminderMinutes > 0)
                {
                    string reminderKey = $"{dateKey}_{type}_pre";
                    DateTime reminderThreshold = prayerDt.AddMinutes(-_preReminderMinutes);

                    if (!_firedReminders.Contains(reminderKey))
                    {
                        if (now >= reminderThreshold)
                        {
                            _firedReminders.Add(reminderKey);

                            if (now < prayerDt)
                            {
                                OnPreReminderReached?.Invoke(type);
                            }
                        }
                    }
                }
            }

            // Also check tomorrow's Fajr for pre-reminder when we're in post-Isha period.
            if (_preReminderMinutes > 0 && TomorrowSchedule != null)
            {
                DateTime? tomorrowFajrDt = GetTomorrowFajrDateTime(now);
                if (tomorrowFajrDt.HasValue)
                {
                    string tomorrowDateKey = now.Date.AddDays(1).ToString("yyyy-MM-dd");
                    string reminderKey = $"{tomorrowDateKey}_{PrayerType.Fajr}_pre";
                    DateTime reminderThreshold = tomorrowFajrDt.Value.AddMinutes(-_preReminderMinutes);

                    if (!_firedReminders.Contains(reminderKey))
                    {
                        if (now >= reminderThreshold)
                        {
                            _firedReminders.Add(reminderKey);

                            if (now < tomorrowFajrDt.Value)
                            {
                                OnPreReminderReached?.Invoke(PrayerType.Fajr);
                            }
                        }
                    }
                }
            }
        }

        // ───────────────────────── Day Schedule Helpers ─────────────────────────

        /// <summary>
        /// Updates TodaySchedule and TomorrowSchedule from the loaded monthly data.
        /// </summary>
        public void RefreshDaySchedules()
        {
            DateTime now = DateTime.Now;
            TodaySchedule = FindDaySchedule(now.Date);
            TomorrowSchedule = FindDaySchedule(now.Date.AddDays(1));
        }

        /// <summary>
        /// Only refreshes if the current TodaySchedule date no longer matches the actual today
        /// (i.e., midnight has passed since the last refresh).
        /// </summary>
        private void RefreshDaySchedulesIfNeeded(DateTime now)
        {
            if (TodaySchedule == null || TodaySchedule.Date.Date != now.Date)
            {
                RefreshDaySchedules();

                // A new day means we can prune stale entries from fired-event sets
                // to prevent unbounded growth (keep only today and tomorrow keys).
                PruneFiredSets(now);
            }
        }

        /// <summary>
        /// Finds the DailySchedule for a given date across the current and next month caches.
        /// </summary>
        private DailySchedule? FindDaySchedule(DateTime date)
        {
            DailySchedule? found = _currentMonth?.Days.FirstOrDefault(d => d.Date.Date == date.Date);
            if (found != null)
                return found;

            return _nextMonth?.Days.FirstOrDefault(d => d.Date.Date == date.Date);
        }

        /// <summary>
        /// Gets tomorrow's Fajr as an absolute DateTime, handling the month-boundary case
        /// where tomorrow's data lives in _nextMonth.
        /// </summary>
        private DateTime? GetTomorrowFajrDateTime(DateTime now)
        {
            if (TomorrowSchedule == null)
            {
                // Try to find it again — it may have become available.
                TomorrowSchedule = FindDaySchedule(now.Date.AddDays(1));
            }

            if (TomorrowSchedule == null)
                return null;

            PrayerTime? fajr = TomorrowSchedule.GetPrayer(PrayerType.Fajr);
            if (fajr == null)
                return null;

            DateTime dt = TomorrowSchedule.Date.Date + fajr.Time.ToTimeSpan();
            if (_prayerOffsets != null && _prayerOffsets.TryGetValue(PrayerType.Fajr, out int offset) && offset != 0)
            {
                dt = dt.AddMinutes(offset);
            }
            return dt;
        }

        // ───────────────────────── Timeline Builder ─────────────────────────

        /// <summary>
        /// Builds a chronologically-ordered list of (PrayerType, absolute DateTime) for a given day.
        /// </summary>
        private List<(PrayerType Type, DateTime Time)> BuildTimeline(DailySchedule day)
        {
            var timeline = new List<(PrayerType Type, DateTime Time)>();

            foreach (PrayerType pt in AllPrayerTypesOrdered)
            {
                PrayerTime? prayer = day.GetPrayer(pt);
                if (prayer != null)
                {
                    DateTime dt = day.Date.Date + prayer.Time.ToTimeSpan();
                    if (_prayerOffsets != null && _prayerOffsets.TryGetValue(pt, out int offset) && offset != 0)
                    {
                        dt = dt.AddMinutes(offset);
                    }
                    timeline.Add((pt, dt));
                }
            }

            // Ensure chronological order (should already be, but safety first).
            timeline.Sort((a, b) => a.Time.CompareTo(b.Time));
            return timeline;
        }

        // ───────────────────────── Housekeeping ─────────────────────────

        /// <summary>
        /// Removes fired-event keys that are older than yesterday to prevent unbounded set growth.
        /// </summary>
        private void PruneFiredSets(DateTime now)
        {
            string todayPrefix = now.ToString("yyyy-MM-dd");
            string tomorrowPrefix = now.AddDays(1).ToString("yyyy-MM-dd");

            _firedAlerts.RemoveWhere(key => !key.StartsWith(todayPrefix) && !key.StartsWith(tomorrowPrefix));
            _firedReminders.RemoveWhere(key => !key.StartsWith(todayPrefix) && !key.StartsWith(tomorrowPrefix));
        }
    }
}
