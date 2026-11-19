using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using Waqt.Models;
using Waqt.Services;
using DailyVerseService = Waqt.Services.DailyVerseService;

namespace Waqt.Views;

public partial class MainWindow : Window
{
    private enum AppTab
        {
            Today,
            Qibla,
            Monthly,
            Settings
        }

        // ───────────────────────── UI Element Accessors ─────────────────────────
        private Slider? SliderHeading => (Slider?)FindName("sliderHeading");
        private Border? BadgeQiblaAlignment => (Border?)FindName("badgeQiblaAlignment");
        private TextBlock? TxtTurnGuidance => (TextBlock?)FindName("txtTurnGuidance");
        private TextBlock? TxtCurrentHeading => (TextBlock?)FindName("txtCurrentHeading");
        private TextBlock? TxtVerseCopyFeedback => (TextBlock?)FindName("txtVerseCopyFeedback");

        // ───────────────────────── Fields ─────────────────────────
        private readonly AlAdhanService _alAdhanService = new();
        private readonly PrayerEngineService _prayerEngine = new();
        private readonly NotificationAndAudioService _notificationService = new();
        private readonly LocationDatabaseService _locationDb = new();
        private readonly AppSettings _settings;

        private List<string> _allCountries = [];
        private List<string> _currentCountryCities = [];
        private MonthlySchedule? _currentMonth;
        private bool _isFilteringLocation = false;

        private bool _isPinned = false;
        private AppTab _currentTab = AppTab.Today;
        private bool _isFileDialogOpen = false;
        private bool _isSyncing = false;

        private double _calculatedQiblaBearing = 176.5;
        private double _currentHeading = 0.0;
        private Windows.Devices.Sensors.Compass? _hardwareCompass;

        private readonly DispatcherTimer _tooltipTimer;
        private readonly DispatcherTimer _iconTimer;

        // ───────────────────────── Constructor ─────────────────────────
        public MainWindow()
        {
            InitializeComponent();

            _settings = AppSettings.Load();

            // Set up tooltip timer (ticking every 1s)
            _tooltipTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _tooltipTimer.Tick += (s, e) => UpdateTooltip();

            // Set up dynamic tray icon timer (ticking every 60s)
            _iconTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(60)
            };
            _iconTimer.Tick += (s, e) => UpdateTrayIcon();

            // Hook up engine events
            _prayerEngine.OnStateUpdated += OnEngineStateUpdated;
            _prayerEngine.OnPrayerTimeReached += OnPrayerTimeReached;
            _prayerEngine.OnPreReminderReached += OnPreReminderReached;

            // Hook up audio playback state change
            _notificationService.OnPlaybackStateChanged += OnAudioPlaybackStateChanged;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PositionNearTaskbar();

            // Wire up cascading country & city dropdown events
            cmbCountry.SelectionChanged += CmbCountry_SelectionChanged;
            cmbCountry.DropDownClosed += CmbCountry_DropDownClosed;
            cmbCity.DropDownOpened += CmbCity_DropDownOpened;

            SyncSettingsToUI();
            UpdateNavButtonsVisuals();
            InitializeCompassSensor();
        }

        // ───────────────────────── Initialization ─────────────────────────
        public async Task InitializeAsync()
        {
            try
            {
                _prayerEngine.SetPreReminderMinutes(_settings.PreReminderMinutes);
                _prayerEngine.SetPrayerOffsets(_settings.PrayerOffsets);

                // Initialize world locations database
                await _locationDb.InitializeAsync();
                _allCountries = [.. _locationDb.GetCountries()];
                Dispatcher.Invoke(SyncSettingsToUI);

                // Fetch or load cached schedule for today
                DateTime now = DateTime.Now;
                _currentMonth = await _alAdhanService.GetOrFetchScheduleAsync(_settings, now.Year, now.Month);

                // Check if next month is needed for boundary
                MonthlySchedule? nextMonth = null;
                if (now.Day >= DateTime.DaysInMonth(now.Year, now.Month) - 3)
                {
                    DateTime nextMonthDate = now.AddMonths(1);
                    nextMonth = await _alAdhanService.GetOrFetchScheduleAsync(_settings, nextMonthDate.Year, nextMonthDate.Month);
                }

                if (_currentMonth != null)
                {
                    _prayerEngine.LoadSchedules(_currentMonth, nextMonth);
                    _prayerEngine.Start();
                }

                // Start periodic timers
                _tooltipTimer.Start();
                _iconTimer.Start();

                // Initial icon and UI update
                UpdateTrayIcon();
                UpdateUI();

                // Update online/cached indicator
                UpdateSyncStatusBadge(_currentMonth != null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Initialization error: {ex.Message}");
            }
        }

        // ───────────────────────── Positioning & Flyout Animations ─────────────────────────
        public void ShowFlyout()
        {
            PositionNearTaskbar();

            Visibility = Visibility.Visible;
            Activate();
            Focus();

            var opacityAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180));
            var slideAnim = new DoubleAnimation(18.0, 0.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            MainContainer.BeginAnimation(OpacityProperty, opacityAnim);
            FlyoutTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
        }

        public void HideFlyout()
        {
            if (Visibility != Visibility.Visible) return;

            var opacityAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(120));
            var slideAnim = new DoubleAnimation(0.0, 12.0, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            slideAnim.Completed += (_, _) => Visibility = Visibility.Collapsed;

            MainContainer.BeginAnimation(OpacityProperty, opacityAnim);
            FlyoutTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
        }

        private void PositionNearTaskbar()
        {
            Rect workArea = SystemParameters.WorkArea;
            double left = workArea.Right - Width - 12;
            double top = workArea.Bottom - Height - 12;

            Left = Math.Max(workArea.Left, left);
            Top = Math.Max(workArea.Top, top);
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // Close flyout on blur unless pinned, in settings, or a file dialog is open
            if (!_isPinned && !_isFileDialogOpen && _currentTab == AppTab.Today)
            {
                HideFlyout();
            }
        }

        // ───────────────────────── Navigation Tabs ─────────────────────────
        private void NavToday_Click(object sender, RoutedEventArgs e) => SwitchToTab(AppTab.Today);
        private void NavQibla_Click(object sender, RoutedEventArgs e) => SwitchToTab(AppTab.Qibla);
        private void NavMonthly_Click(object sender, RoutedEventArgs e) => SwitchToTab(AppTab.Monthly);
        private void NavSettings_Click(object sender, RoutedEventArgs e) => SwitchToTab(AppTab.Settings);

        private void SwitchToTab(AppTab tab)
        {
            _currentTab = tab;

            DashboardPanel.Visibility = tab == AppTab.Today ? Visibility.Visible : Visibility.Collapsed;
            QiblaPanel.Visibility = tab == AppTab.Qibla ? Visibility.Visible : Visibility.Collapsed;
            MonthlyPanel.Visibility = tab == AppTab.Monthly ? Visibility.Visible : Visibility.Collapsed;
            SettingsPanel.Visibility = tab == AppTab.Settings ? Visibility.Visible : Visibility.Collapsed;

            UpdateNavButtonsVisuals();

            if (tab == AppTab.Today)
            {
                UpdateUI();
            }
            else if (tab == AppTab.Qibla)
            {
                UpdateQiblaUI();
            }
            else if (tab == AppTab.Monthly)
            {
                UpdateMonthlyTimetableUI();
            }
            else if (tab == AppTab.Settings)
            {
                SyncSettingsToUI();
            }
        }

        private void UpdateNavButtonsVisuals()
        {
            SetNavButtonStyle(navBtnToday, _currentTab == AppTab.Today);
            SetNavButtonStyle(navBtnQibla, _currentTab == AppTab.Qibla);
            SetNavButtonStyle(navBtnMonthly, _currentTab == AppTab.Monthly);
            SetNavButtonStyle(navBtnSettings, _currentTab == AppTab.Settings);
        }

        private static void SetNavButtonStyle(Button btn, bool isActive)
        {
            if (isActive)
            {
                btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F2830"));
                btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D2B4"));
            }
            else
            {
                btn.Background = Brushes.Transparent;
                btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
            }
        }

        // ───────────────────────── UI Updates ─────────────────────────
        private void OnEngineStateUpdated()
        {
            Dispatcher.Invoke(UpdateUI);
        }

        private void UpdateUI()
        {
            var today = _prayerEngine.TodaySchedule;
            if (today != null)
            {
                txtLocation.Text = $"{_settings.City}, {_settings.Country}";
                txtTimezoneBadge.Text = string.IsNullOrEmpty(today.Timezone) ? "Detected Timezone" : today.Timezone;

                string gregDate = today.Date.ToString("ddd, d MMM yyyy", CultureInfo.InvariantCulture);
                string hijriDate = !string.IsNullOrEmpty(today.Hijri.FormattedDate)
                    ? today.Hijri.FormattedDate
                    : $"{today.Hijri.Day} {today.Hijri.Month} {today.Hijri.Year}";
                txtDualDate.Text = $"{gregDate} • {hijriDate} {today.Hijri.Designation}".Trim();

                // Next prayer hero countdown
                if (_prayerEngine.NextPrayerType.HasValue && _prayerEngine.NextPrayerDateTime.HasValue)
                {
                    txtHeroPrayerName.Text = _prayerEngine.NextPrayerType.Value.ToString();
                    txtHeroPrayerTime.Text = FormatTime(TimeOnly.FromDateTime(_prayerEngine.NextPrayerDateTime.Value));

                    TimeSpan remaining = _prayerEngine.TimeUntilNext ?? TimeSpan.Zero;
                    txtCountdown.Text = remaining.ToString(@"hh\:mm\:ss");

                    const double cardInnerWidth = 324.0;
                    pbProgressFill.Width = Math.Clamp(_prayerEngine.ProgressPercentage * cardInnerWidth, 0.0, cardInnerWidth);
                }
                else
                {
                    txtHeroPrayerName.Text = "--";
                    txtHeroPrayerTime.Text = "--:--";
                    txtCountdown.Text = "--:--:--";
                    pbProgressFill.Width = 0;
                }

                // 6 Timetable Rows
                UpdatePrayerRow(PrayerType.Fajr, RowFajr, TimeFajr, TagFajr, BellIconFajr, today);
                UpdatePrayerRow(PrayerType.Sunrise, RowSunrise, TimeSunrise, TagSunrise, BellIconSunrise, today);
                UpdatePrayerRow(PrayerType.Dhuhr, RowDhuhr, TimeDhuhr, TagDhuhr, BellIconDhuhr, today);
                UpdatePrayerRow(PrayerType.Asr, RowAsr, TimeAsr, TagAsr, BellIconAsr, today);
                UpdatePrayerRow(PrayerType.Maghrib, RowMaghrib, TimeMaghrib, TagMaghrib, BellIconMaghrib, today);
                UpdatePrayerRow(PrayerType.Isha, RowIsha, TimeIsha, TagIsha, BellIconIsha, today);

                // Verse of the day card
                if (_settings.ShowDailyVerse)
                {
                    cardDailyVerse.Visibility = Visibility.Visible;
                    var verse = DailyVerseService.GetTodayVerse();
                    txtVerseArabic.Text = verse.ArabicText;
                    txtVerseEnglish.Text = $"\"{verse.EnglishText}\"";
                    txtVerseRef.Text = verse.Reference;
                    txtVerseTheme.Text = verse.Theme.ToUpperInvariant();
                }
                else
                {
                    cardDailyVerse.Visibility = Visibility.Collapsed;
                }
            }

            UpdateMuteUI();
        }

        private string FormatTime(TimeOnly time)
        {
            return _settings.Use12HourFormat ? time.ToString("hh:mm tt") : time.ToString("HH:mm");
        }

        private void UpdatePrayerRow(PrayerType type, Border rowBorder, TextBlock timeText, Border tagBorder, System.Windows.Shapes.Path bellIcon, DailySchedule today)
        {
            var prayer = today.GetPrayer(type);
            if (prayer != null)
            {
                TimeOnly t = prayer.Time;
                if (_settings.PrayerOffsets.TryGetValue(type, out int offset) && offset != 0)
                {
                    t = t.AddMinutes(offset);
                }
                timeText.Text = FormatTime(t);
            }

            bool isNext = _prayerEngine.NextPrayerType == type;
            DateTime now = DateTime.Now;
            bool isPassed = false;

            if (prayer != null)
            {
                DateTime prayerDt = today.Date.Date + prayer.Time.ToTimeSpan();
                if (_settings.PrayerOffsets.TryGetValue(type, out int offset) && offset != 0)
                {
                    prayerDt = prayerDt.AddMinutes(offset);
                }
                isPassed = now > prayerDt && !isNext;
            }

            if (isNext)
            {
                rowBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0E232B"));
                rowBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D2B4"));
                rowBorder.Opacity = 1.0;
                tagBorder.Visibility = Visibility.Visible;
            }
            else if (isPassed)
            {
                rowBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#121824"));
                rowBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E2638"));
                rowBorder.Opacity = 0.45;
                tagBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                rowBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#121824"));
                rowBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E2638"));
                rowBorder.Opacity = 1.0;
                tagBorder.Visibility = Visibility.Collapsed;
            }

            bool alertEnabled = _settings.PrayerAlerts.TryGetValue(type, out bool en) && en;
            bellIcon.Fill = alertEnabled
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D2B4"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"));
        }

        // ───────────────────────── Qibla Compass Logic ─────────────────────────
        private void InitializeCompassSensor()
        {
            try
            {
                _hardwareCompass = Windows.Devices.Sensors.Compass.GetDefault();
                if (_hardwareCompass != null)
                {
                    _hardwareCompass.ReportInterval = 100;
                    _hardwareCompass.ReadingChanged += (_, args) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            double? heading = args.Reading.HeadingTrueNorth ?? args.Reading.HeadingMagneticNorth;
                            if (heading.HasValue && SliderHeading is { } slider)
                            {
                                slider.Value = Math.Round(heading.Value, 0);
                            }
                        });
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Compass sensor init: {ex.Message}");
            }
        }

        private void UpdateQiblaUI()
        {
            double lat = 37.1674;
            double lon = 38.7955;

            var known = QiblaService.GetKnownCoordinates(_settings.City);
            if (known.HasValue)
            {
                lat = known.Value.Lat;
                lon = known.Value.Lng;
            }
            else if (_currentMonth?.Latitude != null && _currentMonth?.Longitude != null)
            {
                lat = _currentMonth.Latitude.Value;
                lon = _currentMonth.Longitude.Value;
            }
            else if (_settings.Latitude.HasValue && _settings.Longitude.HasValue)
            {
                lat = _settings.Latitude.Value;
                lon = _settings.Longitude.Value;
            }

            var qibla = QiblaService.Calculate(lat, lon);
            _calculatedQiblaBearing = qibla.BearingDegrees;

            txtQiblaDegrees.Text = $"{qibla.BearingDegrees:0.0}°";
            txtQiblaCardinal.Text = qibla.CardinalDirection;
            txtQiblaDistance.Text = $"{qibla.DistanceKm:N0} km to Holy Kaaba, Makkah";
            txtQiblaCoordinates.Text = $"{_settings.City}, {_settings.Country} ({lat:0.0000}° N, {lon:0.0000}° E)";

            ApplyCompassRotation(animate: true);
        }

        private void ApplyCompassRotation(bool animate = true)
        {
            // Relative angle from user heading to Kaaba
            double relativeAngle = (_calculatedQiblaBearing - _currentHeading + 360.0) % 360.0;

            // Check alignment within ±6 degrees
            double diff = Math.Abs(relativeAngle);
            if (diff > 180.0) diff = 360.0 - diff;
            bool isAligned = diff <= 6.0;

            if (isAligned)
            {
                if (BadgeQiblaAlignment is { } badge) badge.Visibility = Visibility.Visible;
                if (TxtTurnGuidance is { } guidance)
                {
                    guidance.Text = "You are directly facing the Holy Kaaba! ✓";
                    guidance.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                }
            }
            else
            {
                if (BadgeQiblaAlignment is { } badge) badge.Visibility = Visibility.Collapsed;
                if (TxtTurnGuidance is { } guidance)
                {
                    guidance.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                    if (relativeAngle <= 180.0)
                    {
                        guidance.Text = $"Turn {relativeAngle:0}° to your right to face the Kaaba";
                    }
                    else
                    {
                        double turnLeft = 360.0 - relativeAngle;
                        guidance.Text = $"Turn {turnLeft:0}° to your left to face the Kaaba";
                    }
                }
            }

            if (animate)
            {
                var anim = new DoubleAnimation(qiblaNeedleRotation.Angle, relativeAngle, TimeSpan.FromMilliseconds(350))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                qiblaNeedleRotation.BeginAnimation(RotateTransform.AngleProperty, anim);
            }
            else
            {
                qiblaNeedleRotation.Angle = relativeAngle;
            }
        }

        private void SliderHeading_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _currentHeading = e.NewValue;
            if (TxtCurrentHeading is { } txt)
            {
                txt.Text = $"{_currentHeading:0}° ({GetHeadingCardinalText(_currentHeading)})";
            }
            ApplyCompassRotation(animate: false);
        }

        private void BtnPresetHeading_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr && double.TryParse(tagStr, out double heading) && SliderHeading is { } slider)
            {
                slider.Value = heading;
            }
        }

        private void BtnPresetFaceQibla_Click(object sender, RoutedEventArgs e)
        {
            if (SliderHeading is { } slider)
            {
                slider.Value = Math.Round(_calculatedQiblaBearing, 0);
            }
        }

        private static string GetHeadingCardinalText(double heading)
        {
            heading = ((heading % 360.0) + 360.0) % 360.0;
            if (heading >= 337.5 || heading < 22.5) return "Facing North";
            if (heading < 67.5) return "Facing NE";
            if (heading < 112.5) return "Facing East";
            if (heading < 157.5) return "Facing SE";
            if (heading < 202.5) return "Facing South";
            if (heading < 247.5) return "Facing SW";
            if (heading < 292.5) return "Facing West";
            return "Facing NW";
        }

        // ───────────────────────── Monthly Timetable Logic ─────────────────────────
        private void UpdateMonthlyTimetableUI()
        {
            if (_currentMonth == null || _currentMonth.Days == null) return;

            DateTime now = DateTime.Now;
            txtMonthlyTitle.Text = $"{now:MMMM yyyy} Imsakiyah";

            var firstDay = _currentMonth.Days.FirstOrDefault();
            if (firstDay != null && !string.IsNullOrEmpty(firstDay.Hijri?.Month))
            {
                txtMonthlyHijri.Text = $"{firstDay.Hijri.Month} {firstDay.Hijri.Year} AH";
            }

            var rows = new List<MonthlyDayRow>();
            foreach (var d in _currentMonth.Days)
            {
                var row = new MonthlyDayRow
                {
                    DayNumber = d.Date.Day,
                    DateString = d.Date.ToString("dd MMM ddd"),
                    HijriString = $"{d.Hijri?.Day} {d.Hijri?.Month}",
                    Fajr = FormatPrayerTime(d, PrayerType.Fajr),
                    Sunrise = FormatPrayerTime(d, PrayerType.Sunrise),
                    Dhuhr = FormatPrayerTime(d, PrayerType.Dhuhr),
                    Asr = FormatPrayerTime(d, PrayerType.Asr),
                    Maghrib = FormatPrayerTime(d, PrayerType.Maghrib),
                    Isha = FormatPrayerTime(d, PrayerType.Isha),
                    IsToday = d.Date.Date == now.Date
                };
                rows.Add(row);
            }

            icMonthlyDays.ItemsSource = rows;
        }

        private string FormatPrayerTime(DailySchedule day, PrayerType type)
        {
            var p = day.GetPrayer(type);
            if (p == null) return "--:--";
            TimeOnly t = p.Time;
            if (_settings.PrayerOffsets.TryGetValue(type, out int offset) && offset != 0)
            {
                t = t.AddMinutes(offset);
            }
            return FormatTime(t);
        }

        private void BtnCopyMonth_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMonth == null || _currentMonth.Days == null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"Waqt Monthly Prayer Timetable — {DateTime.Now:MMMM yyyy}");
            sb.AppendLine($"{_settings.City}, {_settings.Country}");
            sb.AppendLine(new string('-', 72));
            sb.AppendFormat("{0,-14} {1,-10} {2,-7} {3,-7} {4,-7} {5,-7} {6,-7}", "Date", "Fajr", "Sunrise", "Dhuhr", "Asr", "Maghrib", "Isha").AppendLine();
            sb.AppendLine(new string('-', 72));

            foreach (var d in _currentMonth.Days)
            {
                sb.AppendFormat("{0,-14} {1,-10} {2,-7} {3,-7} {4,-7} {5,-7} {6,-7}",
                    d.Date.ToString("dd MMM (ddd)"),
                    FormatPrayerTime(d, PrayerType.Fajr),
                    FormatPrayerTime(d, PrayerType.Sunrise),
                    FormatPrayerTime(d, PrayerType.Dhuhr),
                    FormatPrayerTime(d, PrayerType.Asr),
                    FormatPrayerTime(d, PrayerType.Maghrib),
                    FormatPrayerTime(d, PrayerType.Isha)).AppendLine();
            }

            Clipboard.SetText(sb.ToString());

            txtCopyFeedback.Visibility = Visibility.Visible;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
            timer.Tick += (s, ev) =>
            {
                txtCopyFeedback.Visibility = Visibility.Collapsed;
                timer.Stop();
            };
            timer.Start();
        }

        private void MenuCopyTimetable_Click(object sender, RoutedEventArgs e)
        {
            var today = _prayerEngine.TodaySchedule;
            if (today == null) return;

            var sb = new StringBuilder();
            sb.AppendLine($"🕌 Waqt • Today's Prayer Timings ({today.Date:dd MMM yyyy})");
            sb.AppendLine($"📍 Location: {_settings.City}, {_settings.Country}");
            sb.AppendLine($"• Fajr:    {FormatPrayerTime(today, PrayerType.Fajr)}");
            sb.AppendLine($"• Sunrise: {FormatPrayerTime(today, PrayerType.Sunrise)}");
            sb.AppendLine($"• Dhuhr:   {FormatPrayerTime(today, PrayerType.Dhuhr)}");
            sb.AppendLine($"• Asr:     {FormatPrayerTime(today, PrayerType.Asr)}");
            sb.AppendLine($"• Maghrib: {FormatPrayerTime(today, PrayerType.Maghrib)}");
            sb.AppendLine($"• Isha:    {FormatPrayerTime(today, PrayerType.Isha)}");

            Clipboard.SetText(sb.ToString());
            System.Windows.MessageBox.Show("Today's prayer timetable has been copied to your clipboard!", "Waqt", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ───────────────────────── Audio Handlers ─────────────────────────
        private void OnAudioPlaybackStateChanged(bool isPlaying)
        {
            Dispatcher.Invoke(() => bannerAudioPlaying.Visibility = isPlaying ? Visibility.Visible : Visibility.Collapsed);
        }

        private void BtnStopAudio_Click(object sender, RoutedEventArgs e)
        {
            _notificationService.StopAudio();
        }

        private void BtnShuffleVerse_Click(object sender, RoutedEventArgs e)
        {
            var verse = DailyVerseService.GetNextVerse();
            txtVerseArabic.Text = verse.ArabicText;
            txtVerseEnglish.Text = $"\"{verse.EnglishText}\"";
            txtVerseRef.Text = verse.Reference;
            txtVerseTheme.Text = verse.Theme.ToUpperInvariant();
        }

        private void BtnCopyVerse_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var arabic = txtVerseArabic.Text;
                var english = txtVerseEnglish.Text;
                var reference = txtVerseRef.Text;

                var sb = new StringBuilder();
                sb.AppendLine(arabic);
                sb.AppendLine(english);
                sb.AppendLine($"— {reference}");

                Clipboard.SetText(sb.ToString().Trim());

                var feedback = TxtVerseCopyFeedback;
                if (feedback != null) feedback.Visibility = Visibility.Visible;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
                timer.Tick += (_, _) =>
                {
                    if (feedback != null) feedback.Visibility = Visibility.Collapsed;
                    timer.Stop();
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Could not copy verse: {ex.Message}");
            }
        }

        private void UpdateTrayIcon()
        {
            try
            {
                int remainingMinutes = _prayerEngine.RemainingMinutes;
                var icon = TrayIconRenderer.RenderMinuteIcon(remainingMinutes);
                TrayIcon.Icon = icon;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Tray icon render error: {ex.Message}");
            }
        }

        private void UpdateTooltip()
        {
            try
            {
                if (_prayerEngine.NextPrayerType.HasValue && _prayerEngine.NextPrayerDateTime.HasValue)
                {
                    string prayer = _prayerEngine.NextPrayerType.Value.ToString();
                    TimeSpan rem = _prayerEngine.TimeUntilNext ?? TimeSpan.Zero;
                    string remStr = rem.ToString(@"hh\:mm\:ss");
                    string timeStr = FormatTime(TimeOnly.FromDateTime(_prayerEngine.NextPrayerDateTime.Value));

                    TrayIcon.ToolTipText = $"Waqt • {prayer} in {remStr} ({timeStr}) | {_settings.City}, {_settings.Country}";
                    trayMenuNextPrayer.Header = $"🕌 Next: {prayer} in {remStr} ({timeStr})";
                }
                else
                {
                    TrayIcon.ToolTipText = $"Waqt • {_settings.City}, {_settings.Country}";
                    trayMenuNextPrayer.Header = $"🕌 Waqt • {_settings.City}";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Tooltip update: {ex.Message}");
            }
        }

        private void UpdateMuteUI()
        {
            bool muted = _settings.IsMuted;
            iconMasterMute.Fill = muted
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D2B4"));

            menuMuteToggle.Header = muted ? "Unmute Athan" : "Mute Athan";
            sliderVolume.Value = _settings.Volume;
        }

        private void UpdateSyncStatusBadge(bool isOnline)
        {
            dotSyncStatus.Fill = isOnline
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            txtSyncStatus.Text = isOnline ? "Online" : "Cached";
        }

        // ───────────────────────── Audio & Notification Handlers ─────────────────────────
        private void OnPrayerTimeReached(PrayerType prayer)
        {
            Dispatcher.Invoke(() =>
            {
                if (!_settings.PrayerAlerts.TryGetValue(prayer, out bool alertEnabled) || !alertEnabled)
                {
                    return;
                }

                string timeStr = DateTime.Now.ToString("HH:mm");

                if (_settings.ToastNotificationsEnabled)
                {
                    _notificationService.SendPrayerToast(prayer, timeStr, _settings.City);
                }

                if (_settings.PlayAthanEnabled && !_settings.IsMuted)
                {
                    _notificationService.PlayAthanAudio(_settings.CustomAthanFilePath, _settings.Volume);
                }

                UpdateTrayIcon();
            });
        }

        private void OnPreReminderReached(PrayerType prayer)
        {
            Dispatcher.Invoke(() =>
            {
                if (!_settings.ToastNotificationsEnabled) return;
                if (!_settings.PrayerAlerts.TryGetValue(prayer, out bool alertEnabled) || !alertEnabled) return;

                _notificationService.SendPreReminderToast(prayer, _settings.PreReminderMinutes, _settings.City);
            });
        }

        // ───────────────────────── Tray Icon & Menu Clicks ─────────────────────────
        private void TrayIcon_TrayLeftMouseDown(object sender, RoutedEventArgs e)
        {
            if (Visibility == Visibility.Visible)
            {
                HideFlyout();
            }
            else
            {
                ShowFlyout();
            }
        }

        private void MenuOpen_Click(object sender, RoutedEventArgs e) => ShowFlyout();
        private async void MenuRefresh_Click(object sender, RoutedEventArgs e) => await TriggerSyncAsync();

        private void MenuMuteToggle_Click(object sender, RoutedEventArgs e)
        {
            _settings.IsMuted = !_settings.IsMuted;
            _settings.Save();
            UpdateMuteUI();
        }

        private void MenuTestAlert_Click(object sender, RoutedEventArgs e) => TriggerTestAlert();
        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowFlyout();
            SwitchToTab(AppTab.Settings);
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            _notificationService.StopAudio();
            System.Windows.Application.Current.Shutdown();
        }

        // ───────────────────────── Controls & Clicks ─────────────────────────
        private void BtnClose_Click(object sender, RoutedEventArgs e) => HideFlyout();

        private void BtnPin_Click(object sender, RoutedEventArgs e)
        {
            _isPinned = !_isPinned;
            iconPin.Fill = _isPinned
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00D2B4"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
            btnPin.ToolTip = _isPinned ? "Unpin flyout (auto-hide)" : "Pin flyout (keep open)";
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e) => SwitchToTab(AppTab.Settings);
        private void BtnLocation_Click(object sender, RoutedEventArgs e) => SwitchToTab(AppTab.Settings);

        private void Bell_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagName && Enum.TryParse<PrayerType>(tagName, out var prayerType))
            {
                bool current = _settings.PrayerAlerts.TryGetValue(prayerType, out bool val) && val;
                _settings.PrayerAlerts[prayerType] = !current;
                _settings.Save();
                UpdateUI();
            }
        }

        private void BtnMasterMute_Click(object sender, RoutedEventArgs e)
        {
            _settings.IsMuted = !_settings.IsMuted;
            _settings.Save();
            UpdateMuteUI();
        }

        private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_settings != null)
            {
                _settings.Volume = e.NewValue;
                _settings.Save();
            }
        }

        private async void BtnSync_Click(object sender, RoutedEventArgs e) => await TriggerSyncAsync();

        private async Task TriggerSyncAsync()
        {
            if (_isSyncing) return;
            _isSyncing = true;

            var rotateAnim = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(700))
            {
                RepeatBehavior = new RepeatBehavior(2)
            };
            syncRotation.BeginAnimation(RotateTransform.AngleProperty, rotateAnim);

            try
            {
                DateTime now = DateTime.Now;
                _currentMonth = await _alAdhanService.FetchMonthlyScheduleAsync(
                    _settings.City, _settings.Country, _settings.CalculationMethod, _settings.AsrJuristic, now.Year, now.Month);

                if (_currentMonth != null)
                {
                    _alAdhanService.SaveScheduleToCache(_currentMonth);
                    _prayerEngine.LoadSchedules(_currentMonth, null);
                    UpdateSyncStatusBadge(true);
                }
                else
                {
                    UpdateSyncStatusBadge(false);
                }

                UpdateTrayIcon();
                UpdateUI();

                if (_currentTab == AppTab.Qibla) UpdateQiblaUI();
                if (_currentTab == AppTab.Monthly) UpdateMonthlyTimetableUI();
            }
            finally
            {
                _isSyncing = false;
            }
        }

        private void BtnAttribution_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/zakariaho23",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainWindow] Could not launch browser: {ex.Message}");
            }
        }

        // ───────────────────────── Settings Logic ─────────────────────────
        private void SyncSettingsToUI()
        {
            _isFilteringLocation = true;
            try
            {
                _allCountries = [.. _locationDb.GetCountries()];
                cmbCountry.ItemsSource = _allCountries;

                var matchedCountry = _allCountries.FirstOrDefault(c => c.Equals(_settings.Country, StringComparison.OrdinalIgnoreCase));
                if (matchedCountry != null)
                {
                    cmbCountry.SelectedItem = matchedCountry;
                }
                else if (_allCountries.Count > 0)
                {
                    cmbCountry.SelectedItem = _allCountries[0];
                }

                string currentCountry = cmbCountry.SelectedItem as string ?? _settings.Country;
                _currentCountryCities = [.. _locationDb.GetCitiesForCountry(currentCountry)];

                if (!string.IsNullOrWhiteSpace(_settings.City) &&
                    !_currentCountryCities.Any(c => c.Equals(_settings.City, StringComparison.OrdinalIgnoreCase)))
                {
                    _currentCountryCities.Insert(0, _settings.City);
                }

                cmbCity.ItemsSource = _currentCountryCities;
                var matchedCity = _currentCountryCities.FirstOrDefault(c => c.Equals(_settings.City, StringComparison.OrdinalIgnoreCase));
                if (matchedCity != null)
                {
                    cmbCity.SelectedItem = matchedCity;
                }
                else if (_currentCountryCities.Count > 0)
                {
                    cmbCity.SelectedItem = _currentCountryCities[0];
                }
            }
            finally
            {
                _isFilteringLocation = false;
            }

            // Calculation method
            foreach (ComboBoxItem item in cmbMethod.Items)
            {
                if (int.TryParse(item.Tag?.ToString(), out int methodId) && methodId == _settings.CalculationMethod)
                {
                    cmbMethod.SelectedItem = item;
                    break;
                }
            }

            // Asr school
            foreach (ComboBoxItem item in cmbAsrSchool.Items)
            {
                if (int.TryParse(item.Tag?.ToString(), out int schoolId) && schoolId == _settings.AsrJuristic)
                {
                    cmbAsrSchool.SelectedItem = item;
                    break;
                }
            }

            // Display toggles
            chk12HourFormat.IsChecked = _settings.Use12HourFormat;
            chkShowDailyVerse.IsChecked = _settings.ShowDailyVerse;

            // Notification toggles
            chkToastEnabled.IsChecked = _settings.ToastNotificationsEnabled;
            chkAthanEnabled.IsChecked = _settings.PlayAthanEnabled;

            // Pre-reminder
            foreach (ComboBoxItem item in cmbPreReminder.Items)
            {
                if (int.TryParse(item.Tag?.ToString(), out int mins) && mins == _settings.PreReminderMinutes)
                {
                    cmbPreReminder.SelectedItem = item;
                    break;
                }
            }

            // Audio path
            txtAudioPath.Text = string.IsNullOrEmpty(_settings.CustomAthanFilePath)
                ? "Built-in Harmonic Chime"
                : Path.GetFileName(_settings.CustomAthanFilePath);

            // Minute calibration offsets
            SetSliderOffset(sliderOffsetFajr, txtOffsetFajr, PrayerType.Fajr);
            SetSliderOffset(sliderOffsetDhuhr, txtOffsetDhuhr, PrayerType.Dhuhr);
            SetSliderOffset(sliderOffsetAsr, txtOffsetAsr, PrayerType.Asr);
            SetSliderOffset(sliderOffsetMaghrib, txtOffsetMaghrib, PrayerType.Maghrib);
            SetSliderOffset(sliderOffsetIsha, txtOffsetIsha, PrayerType.Isha);

            // Startup
            chkLaunchStartup.IsChecked = StartupService.IsStartupEnabled();
        }

        private void SetSliderOffset(Slider slider, TextBlock text, PrayerType type)
        {
            int val = _settings.PrayerOffsets.TryGetValue(type, out int o) ? o : 0;
            slider.Value = val;
            text.Text = $"{val:+#;-#;0}m";
        }

        private void SliderOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (sender == sliderOffsetFajr && txtOffsetFajr != null)
                txtOffsetFajr.Text = $"{(int)e.NewValue:+#;-#;0}m";
            else if (sender == sliderOffsetDhuhr && txtOffsetDhuhr != null)
                txtOffsetDhuhr.Text = $"{(int)e.NewValue:+#;-#;0}m";
            else if (sender == sliderOffsetAsr && txtOffsetAsr != null)
                txtOffsetAsr.Text = $"{(int)e.NewValue:+#;-#;0}m";
            else if (sender == sliderOffsetMaghrib && txtOffsetMaghrib != null)
                txtOffsetMaghrib.Text = $"{(int)e.NewValue:+#;-#;0}m";
            else if (sender == sliderOffsetIsha && txtOffsetIsha != null)
                txtOffsetIsha.Text = $"{(int)e.NewValue:+#;-#;0}m";
        }

        private void CmbCountry_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isFilteringLocation) return;
            string? country = cmbCountry.SelectedItem as string ?? cmbCountry.Text;
            UpdateCitiesForCountry(country, selectFirst: true);
        }

        private void CmbCountry_DropDownClosed(object? sender, EventArgs e)
        {
            if (_isFilteringLocation) return;
            string country = (cmbCountry.SelectedItem as string ?? cmbCountry.Text).Trim();
            UpdateCitiesForCountry(country, selectFirst: false);
        }

        private void CmbCity_DropDownOpened(object? sender, EventArgs e)
        {
            string country = (cmbCountry.SelectedItem as string ?? cmbCountry.Text).Trim();
            if (_currentCountryCities == null || _currentCountryCities.Count == 0)
            {
                UpdateCitiesForCountry(country, selectFirst: false);
            }
        }

        private void UpdateCitiesForCountry(string? country, bool selectFirst)
        {
            if (string.IsNullOrWhiteSpace(country)) return;

            var cities = _locationDb.GetCitiesForCountry(country).ToList();
            if (cities.Count > 0)
            {
                _currentCountryCities = cities;
                cmbCity.ItemsSource = _currentCountryCities;

                string currentCity = (cmbCity.SelectedItem as string ?? cmbCity.Text).Trim();
                var matched = cities.FirstOrDefault(c => c.Equals(currentCity, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    cmbCity.SelectedItem = matched;
                }
                else if (selectFirst)
                {
                    cmbCity.SelectedItem = cities[0];
                }
            }
        }

        private async void BtnAutoDetect_Click(object sender, RoutedEventArgs e)
        {
            btnAutoDetect.IsEnabled = false;
            try
            {
                var detected = await _locationDb.AutoDetectLocationAsync();
                if (detected.HasValue)
                {
                    _isFilteringLocation = true;
                    try
                    {
                        var matchedCountry = _allCountries.FirstOrDefault(c => c.Equals(detected.Value.Country, StringComparison.OrdinalIgnoreCase));
                        if (matchedCountry != null)
                        {
                            cmbCountry.SelectedItem = matchedCountry;
                        }
                        else
                        {
                            _allCountries.Insert(0, detected.Value.Country);
                            cmbCountry.ItemsSource = null;
                            cmbCountry.ItemsSource = _allCountries;
                            cmbCountry.SelectedItem = detected.Value.Country;
                        }

                        _currentCountryCities = [.. _locationDb.GetCitiesForCountry(detected.Value.Country)];
                        if (!_currentCountryCities.Any(c => c.Equals(detected.Value.City, StringComparison.OrdinalIgnoreCase)))
                        {
                            _currentCountryCities.Insert(0, detected.Value.City);
                        }
                        cmbCity.ItemsSource = _currentCountryCities;

                        var matchedCity = _currentCountryCities.FirstOrDefault(c => c.Equals(detected.Value.City, StringComparison.OrdinalIgnoreCase)) ?? detected.Value.City;
                        cmbCity.SelectedItem = matchedCity;
                    }
                    finally
                    {
                        _isFilteringLocation = false;
                    }

                    BtnVerifyLocation_Click(sender, e);
                }
                else
                {
                    System.Windows.MessageBox.Show("Could not auto-detect location. Please select your country and city manually.", "Waqt", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            finally
            {
                btnAutoDetect.IsEnabled = true;
            }
        }

        private async void BtnVerifyLocation_Click(object sender, RoutedEventArgs e)
        {
            string city = (cmbCity.SelectedItem as string ?? cmbCity.Text).Trim();
            string country = (cmbCountry.SelectedItem as string ?? cmbCountry.Text).Trim();

            if (string.IsNullOrEmpty(city) || string.IsNullOrEmpty(country))
            {
                System.Windows.MessageBox.Show("Please enter or select both City and Country.", "Waqt", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            btnVerifyLocation.IsEnabled = false;
            btnVerifyLocation.Content = "Verifying...";

            try
            {
                int method = GetSelectedMethodId();
                int school = GetSelectedSchoolId();
                DateTime now = DateTime.Now;

                var result = await _alAdhanService.FetchMonthlyScheduleAsync(city, country, method, school, now.Year, now.Month);
                if (result != null && !string.IsNullOrEmpty(result.Timezone))
                {
                    lblResolvedTimezone.Text = result.Timezone;
                    lblResolvedTimezone.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                }
                else
                {
                    lblResolvedTimezone.Text = "Not found / Offline";
                    lblResolvedTimezone.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                }
            }
            finally
            {
                btnVerifyLocation.IsEnabled = true;
                btnVerifyLocation.Content = "Fetch & Verify";
            }
        }

        private void BtnBrowseAudio_Click(object sender, RoutedEventArgs e)
        {
            _isFileDialogOpen = true;
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Select Custom Athan Audio",
                    Filter = "Audio Files (*.mp3;*.wav)|*.mp3;*.wav|MP3 Files (*.mp3)|*.mp3|WAV Files (*.wav)|*.wav|All Files (*.*)|*.*"
                };

                if (dialog.ShowDialog() is true)
                {
                    _settings.CustomAthanFilePath = dialog.FileName;
                    txtAudioPath.Text = Path.GetFileName(dialog.FileName);
                }
            }
            finally
            {
                _isFileDialogOpen = false;
            }
        }

        private void BtnResetAudio_Click(object sender, RoutedEventArgs e)
        {
            _settings.CustomAthanFilePath = null;
            txtAudioPath.Text = "Built-in Harmonic Chime";
        }

        private void BtnTestAlert_Click(object sender, RoutedEventArgs e) => TriggerTestAlert();

        private void TriggerTestAlert()
        {
            _notificationService.SendPrayerToast(PrayerType.Dhuhr, "12:24", _settings.City);
            _notificationService.PlayAthanAudio(_settings.CustomAthanFilePath, _settings.Volume);
        }

        private async void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            string city = (cmbCity.SelectedItem as string ?? cmbCity.Text).Trim();
            string country = (cmbCountry.SelectedItem as string ?? cmbCountry.Text).Trim();

            if (string.IsNullOrEmpty(city) || string.IsNullOrEmpty(country))
            {
                System.Windows.MessageBox.Show("Please enter or select both City and Country.", "Waqt", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _settings.City = city;
            _settings.Country = country;
            _settings.CalculationMethod = GetSelectedMethodId();
            _settings.AsrJuristic = GetSelectedSchoolId();
            _settings.Use12HourFormat = chk12HourFormat.IsChecked is true;
            _settings.ShowFastingCard = false;
            _settings.ShowDailyVerse = chkShowDailyVerse.IsChecked is true;
            _settings.ToastNotificationsEnabled = chkToastEnabled.IsChecked is true;
            _settings.PlayAthanEnabled = chkAthanEnabled.IsChecked is true;

            if (cmbPreReminder.SelectedItem is ComboBoxItem reminderItem &&
                int.TryParse(reminderItem.Tag?.ToString(), out int preMins))
            {
                _settings.PreReminderMinutes = preMins;
            }

            // Save offsets
            _settings.PrayerOffsets[PrayerType.Fajr] = (int)sliderOffsetFajr.Value;
            _settings.PrayerOffsets[PrayerType.Dhuhr] = (int)sliderOffsetDhuhr.Value;
            _settings.PrayerOffsets[PrayerType.Asr] = (int)sliderOffsetAsr.Value;
            _settings.PrayerOffsets[PrayerType.Maghrib] = (int)sliderOffsetMaghrib.Value;
            _settings.PrayerOffsets[PrayerType.Isha] = (int)sliderOffsetIsha.Value;

            bool startup = chkLaunchStartup.IsChecked is true;
            _settings.LaunchOnStartup = startup;
            StartupService.SetStartupEnabled(startup);

            _settings.Save();
            _prayerEngine.SetPreReminderMinutes(_settings.PreReminderMinutes);
            _prayerEngine.SetPrayerOffsets(_settings.PrayerOffsets);

            await TriggerSyncAsync();

            lblSaveStatus.Visibility = Visibility.Visible;
            var statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
            statusTimer.Tick += (s, ev) =>
            {
                lblSaveStatus.Visibility = Visibility.Collapsed;
                statusTimer.Stop();
                SwitchToTab(AppTab.Today);
            };
            statusTimer.Start();
        }

        private int GetSelectedMethodId()
        {
            if (cmbMethod.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int id))
            {
                return id;
            }
            return 13;
        }

        private int GetSelectedSchoolId()
        {
            if (cmbAsrSchool.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int id))
            {
                return id;
            }
            return 0;
        }
    }
