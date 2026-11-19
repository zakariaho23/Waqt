using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Waqt.Models;

namespace Waqt.Services
{
    public class AlAdhanService
    {
        private readonly HttpClient _httpClient;

        private static readonly JsonSerializerOptions CacheSerializerOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly JsonSerializerOptions ReadSerializerOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        public AlAdhanService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
        }

        public async Task<MonthlySchedule?> FetchMonthlyScheduleAsync(
            string city, string country, int method, int school, int year, int month)
        {
            try
            {
                string url =
                    $"https://api.aladhan.com/v1/calendarByCity/{year}/{month}" +
                    $"?city={Uri.EscapeDataString(city)}" +
                    $"&country={Uri.EscapeDataString(country)}" +
                    $"&method={method}" +
                    $"&school={school}";

                Debug.WriteLine($"[AlAdhanService] Fetching: {url}");

                string json = await _httpClient.GetStringAsync(url);

                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                if (!root.TryGetProperty("data", out JsonElement dataArray) ||
                    dataArray.ValueKind != JsonValueKind.Array)
                {
                    Debug.WriteLine("[AlAdhanService] Invalid API response: missing 'data' array.");
                    return null;
                }

                var schedule = new MonthlySchedule
                {
                    City = city,
                    Country = country,
                    Year = year,
                    Month = month
                };

                string timezone = string.Empty;

                foreach (JsonElement dayElement in dataArray.EnumerateArray())
                {
                    var daily = new DailySchedule();

                    // --- Parse Gregorian date ---
                    if (dayElement.TryGetProperty("date", out JsonElement dateEl) &&
                        dateEl.TryGetProperty("gregorian", out JsonElement gregEl) &&
                        gregEl.TryGetProperty("date", out JsonElement gregDateEl))
                    {
                        string gregDateStr = gregDateEl.GetString() ?? string.Empty; // "DD-MM-YYYY"
                        if (DateTime.TryParseExact(gregDateStr, "dd-MM-yyyy",
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                        {
                            daily.Date = parsedDate;
                        }
                    }

                    // --- Parse Hijri date ---
                    if (dayElement.TryGetProperty("date", out JsonElement dateEl2) &&
                        dateEl2.TryGetProperty("hijri", out JsonElement hijriEl))
                    {
                        string hijriDay = hijriEl.TryGetProperty("day", out JsonElement dEl) ? dEl.GetString() ?? "" : "";
                        string hijriYear = hijriEl.TryGetProperty("year", out JsonElement yEl) ? yEl.GetString() ?? "" : "";
                        string hijriMonthName = "";
                        if (hijriEl.TryGetProperty("month", out JsonElement mEl) &&
                            mEl.TryGetProperty("en", out JsonElement mNameEl))
                        {
                            hijriMonthName = mNameEl.GetString() ?? "";
                        }

                        string designation = "";
                        if (hijriEl.TryGetProperty("designation", out JsonElement desEl) &&
                            desEl.TryGetProperty("abbreviated", out JsonElement abbrEl))
                        {
                            designation = abbrEl.GetString() ?? "";
                        }

                        daily.Hijri = new HijriDateInfo
                        {
                            Day = hijriDay,
                            Month = hijriMonthName,
                            Year = hijriYear,
                            Designation = designation,
                            FormattedDate = $"{hijriDay} {hijriMonthName} {hijriYear}"
                        };
                    }

                    // --- Parse timezone & coordinates (from meta) ---
                    if (dayElement.TryGetProperty("meta", out JsonElement metaEl))
                    {
                        if (string.IsNullOrEmpty(timezone) && metaEl.TryGetProperty("timezone", out JsonElement tzEl))
                        {
                            timezone = tzEl.GetString() ?? "";
                        }
                        if (!schedule.Latitude.HasValue && metaEl.TryGetProperty("latitude", out JsonElement latEl) && latEl.TryGetDouble(out double lat))
                        {
                            schedule.Latitude = lat;
                        }
                        if (!schedule.Longitude.HasValue && metaEl.TryGetProperty("longitude", out JsonElement lngEl) && lngEl.TryGetDouble(out double lng))
                        {
                            schedule.Longitude = lng;
                        }
                    }

                    daily.Timezone = timezone;

                    // --- Parse timings ---
                    if (dayElement.TryGetProperty("timings", out JsonElement timingsEl))
                    {
                        daily.Prayers = ParseTimings(timingsEl);
                    }

                    schedule.Days.Add(daily);
                }

                schedule.Timezone = timezone;

                Debug.WriteLine($"[AlAdhanService] Fetched {schedule.Days.Count} days for {city}, {country} ({year}/{month}).");
                return schedule;
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"[AlAdhanService] HTTP error fetching schedule: {ex.Message}");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine($"[AlAdhanService] Request timed out: {ex.Message}");
                return null;
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"[AlAdhanService] JSON parse error: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AlAdhanService] Unexpected error fetching schedule: {ex.Message}");
                return null;
            }
        }

        public MonthlySchedule? LoadCachedSchedule(string city, string country, int year, int month)
        {
            try
            {
                string cachePath = GetCachePath(city, country, year, month);

                if (!File.Exists(cachePath))
                {
                    Debug.WriteLine($"[AlAdhanService] Cache miss: {cachePath}");
                    return null;
                }

                string json = File.ReadAllText(cachePath);
                var schedule = JsonSerializer.Deserialize<MonthlySchedule>(json, ReadSerializerOptions);

                if (schedule != null)
                {
                    Debug.WriteLine($"[AlAdhanService] Cache hit: {cachePath} ({schedule.Days.Count} days).");
                }

                return schedule;
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"[AlAdhanService] Cache deserialize error: {ex.Message}");
                return null;
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"[AlAdhanService] Cache read IO error: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AlAdhanService] Unexpected cache read error: {ex.Message}");
                return null;
            }
        }

        public void SaveScheduleToCache(MonthlySchedule schedule)
        {
            try
            {
                string cachePath = GetCachePath(schedule.City, schedule.Country, schedule.Year, schedule.Month);
                string json = JsonSerializer.Serialize(schedule, CacheSerializerOptions);
                File.WriteAllText(cachePath, json);

                Debug.WriteLine($"[AlAdhanService] Saved cache: {cachePath}");
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"[AlAdhanService] Cache write IO error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AlAdhanService] Unexpected cache write error: {ex.Message}");
            }
        }

        public async Task PrefetchNextMonthIfNeeded(AppSettings settings)
        {
            try
            {
                DateTime today = DateTime.Now;
                int daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
                int daysRemaining = daysInMonth - today.Day;

                if (daysRemaining > 3)
                {
                    Debug.WriteLine("[AlAdhanService] Not near end of month, skipping prefetch.");
                    return;
                }

                DateTime nextMonthDate = today.AddMonths(1);
                int nextYear = nextMonthDate.Year;
                int nextMonth = nextMonthDate.Month;

                Debug.WriteLine($"[AlAdhanService] Near end of month ({daysRemaining} days left). Prefetching {nextYear}/{nextMonth}...");

                var cached = LoadCachedSchedule(settings.City, settings.Country, nextYear, nextMonth);
                if (cached != null)
                {
                    Debug.WriteLine("[AlAdhanService] Next month already cached.");
                    return;
                }

                var fetched = await FetchMonthlyScheduleAsync(
                    settings.City, settings.Country,
                    settings.CalculationMethod, settings.AsrJuristic,
                    nextYear, nextMonth);

                if (fetched != null)
                {
                    SaveScheduleToCache(fetched);
                    Debug.WriteLine("[AlAdhanService] Next month prefetched and cached.");
                }
                else
                {
                    Debug.WriteLine("[AlAdhanService] Failed to prefetch next month.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AlAdhanService] Prefetch error: {ex.Message}");
            }
        }

        public async Task<MonthlySchedule?> GetOrFetchScheduleAsync(AppSettings settings, int year, int month)
        {
            try
            {
                var cached = LoadCachedSchedule(settings.City, settings.Country, year, month);
                if (cached != null)
                {
                    Debug.WriteLine($"[AlAdhanService] Returning cached schedule for {year}/{month}.");
                    return cached;
                }

                Debug.WriteLine($"[AlAdhanService] No cache for {year}/{month}, fetching from API...");

                var fetched = await FetchMonthlyScheduleAsync(
                    settings.City, settings.Country,
                    settings.CalculationMethod, settings.AsrJuristic,
                    year, month);

                if (fetched != null)
                {
                    SaveScheduleToCache(fetched);
                    Debug.WriteLine($"[AlAdhanService] Fetched and cached schedule for {year}/{month}.");
                }

                return fetched;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AlAdhanService] GetOrFetch error: {ex.Message}");
                return null;
            }
        }

        private static List<PrayerTime> ParseTimings(JsonElement timingsEl)
        {
            var prayers = new List<PrayerTime>();
            var prayerMappings = new (string JsonKey, PrayerType Type)[]
            {
                ("Fajr", PrayerType.Fajr),
                ("Sunrise", PrayerType.Sunrise),
                ("Dhuhr", PrayerType.Dhuhr),
                ("Asr", PrayerType.Asr),
                ("Maghrib", PrayerType.Maghrib),
                ("Isha", PrayerType.Isha)
            };

            foreach (var (jsonKey, prayerType) in prayerMappings)
            {
                if (timingsEl.TryGetProperty(jsonKey, out JsonElement timeEl))
                {
                    string rawTime = timeEl.GetString() ?? "";

                    // CRITICAL: Strip timezone suffix (e.g., " (+03)" or " (EET)")
                    string cleanTime = rawTime.Split(' ')[0];

                    if (TimeOnly.TryParse(cleanTime, out TimeOnly parsedTime))
                    {
                        prayers.Add(new PrayerTime
                        {
                            Type = prayerType,
                            Time = parsedTime
                        });
                    }
                    else
                    {
                        Debug.WriteLine($"[AlAdhanService] Failed to parse time for {jsonKey}: '{rawTime}'");
                    }
                }
            }

            return prayers;
        }

        private static string GetCachePath(string city, string country, int year, int month)
        {
            string appDataPath = AppSettings.GetAppDataPath();
            return Path.Combine(appDataPath, $"cache_{city}_{country}_{year}_{month}.json");
        }
    }
}
