using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Waqt.Models;

namespace Waqt.Services
{
    public class LocationDatabaseService
    {
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, List<string>> _worldLocations = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private bool _isInitialized = false;

        private static string CacheFilePath =>
            Path.Combine(AppSettings.GetAppDataPath(), "world_locations.json");

        public LocationDatabaseService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };

            // Immediately populate curated fallback so memory is never empty on launch
            LoadCuratedFallback();

            // Try loading cached world locations synchronously from disk if available
            LoadDiskCacheSync();
        }

        private void LoadDiskCacheSync()
        {
            try
            {
                string cachePath = CacheFilePath;
                if (File.Exists(cachePath))
                {
                    string json = File.ReadAllText(cachePath);
                    var cachedData = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                    if (cachedData != null && cachedData.Count > 0)
                    {
                        lock (_lock)
                        {
                            foreach (var kvp in cachedData)
                            {
                                if (kvp.Value != null && kvp.Value.Count > 0)
                                {
                                    _worldLocations[kvp.Key] = kvp.Value.Distinct().OrderBy(c => c).ToList();
                                }
                            }

                            // Keep Turkey and Türkiye aliases in sync with all 81 provinces
                            EnsureProvincesAndAliases();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LocationDatabaseService] Sync disk cache read: {ex.Message}");
            }
        }

        private void EnsureProvincesAndAliases()
        {
            var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_worldLocations.TryGetValue("Turkey", out var tk))
            {
                foreach (var c in tk) list.Add(c);
            }
            if (_worldLocations.TryGetValue("Türkiye", out var tr))
            {
                foreach (var c in tr) list.Add(c);
            }
            foreach (var p in AllTurkishProvinces)
            {
                list.Add(p);
            }
            var sorted = list.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
            _worldLocations["Turkey"] = sorted;
            _worldLocations["Türkiye"] = sorted;
        }

        public IReadOnlyList<string> GetCountries()
        {
            lock (_lock)
            {
                return _worldLocations.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public IReadOnlyList<string> GetCitiesForCountry(string? country)
        {
            if (string.IsNullOrWhiteSpace(country))
            {
                return Array.Empty<string>();
            }

            lock (_lock)
            {
                if (_worldLocations.TryGetValue(country.Trim(), out var cities))
                {
                    return cities;
                }

                // Handle Turkey / Türkiye alias
                if (country.Equals("Turkey", StringComparison.OrdinalIgnoreCase) && _worldLocations.TryGetValue("Türkiye", out var trCities1))
                {
                    return trCities1;
                }
                if (country.Equals("Türkiye", StringComparison.OrdinalIgnoreCase) && _worldLocations.TryGetValue("Turkey", out var trCities2))
                {
                    return trCities2;
                }

                // Partial match
                var matchedKey = _worldLocations.Keys.FirstOrDefault(k => k.Equals(country.Trim(), StringComparison.OrdinalIgnoreCase)
                    || k.StartsWith(country.Trim(), StringComparison.OrdinalIgnoreCase));

                if (matchedKey != null && _worldLocations.TryGetValue(matchedKey, out var matchedCities))
                {
                    return matchedCities;
                }

                return Array.Empty<string>();
            }
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            // 1. Load curated fallback first so memory is never empty
            LoadCuratedFallback();

            // 2. Check local disk cache
            string cachePath = CacheFilePath;
            bool cacheExists = File.Exists(cachePath);
            bool isCacheFresh = false;

            if (cacheExists)
            {
                try
                {
                    FileInfo fi = new FileInfo(cachePath);
                    isCacheFresh = (DateTime.Now - fi.LastWriteTime).TotalDays < 30;

                    string json = await File.ReadAllTextAsync(cachePath);
                    var cachedData = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);

                    if (cachedData != null && cachedData.Count > 0)
                    {
                        lock (_lock)
                        {
                            foreach (var kvp in cachedData)
                            {
                                if (kvp.Value != null && kvp.Value.Count > 0)
                                {
                                    _worldLocations[kvp.Key] = kvp.Value.Distinct().OrderBy(c => c).ToList();
                                }
                            }
                            EnsureProvincesAndAliases();
                        }
                        Debug.WriteLine($"[LocationDatabaseService] Loaded {cachedData.Count} countries from disk cache in <50ms.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LocationDatabaseService] Failed to read disk cache: {ex.Message}");
                }
            }

            _isInitialized = true;

            // 3. If cache is missing or stale (>30 days), fetch fresh global dataset silently in background
            if (!cacheExists || !isCacheFresh)
            {
                _ = Task.Run(async () =>
                {
                    await FetchAndCacheGlobalDatabaseAsync();
                });
            }
        }

        private async Task FetchAndCacheGlobalDatabaseAsync()
        {
            try
            {
                Debug.WriteLine("[LocationDatabaseService] Fetching global dataset from countriesnow.space...");
                string url = "https://countriesnow.space/api/v0.1/countries";

                string responseJson = await _httpClient.GetStringAsync(url);
                using JsonDocument doc = JsonDocument.Parse(responseJson);

                if (doc.RootElement.TryGetProperty("data", out JsonElement dataElement) &&
                    dataElement.ValueKind == JsonValueKind.Array)
                {
                    var fetchedDict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                    foreach (JsonElement item in dataElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("country", out JsonElement countryProp))
                        {
                            string? countryName = countryProp.GetString()?.Trim();
                            if (string.IsNullOrEmpty(countryName)) continue;

                            var cityList = new List<string>();
                            if (item.TryGetProperty("cities", out JsonElement citiesProp) &&
                                citiesProp.ValueKind == JsonValueKind.Array)
                            {
                                foreach (JsonElement cityElement in citiesProp.EnumerateArray())
                                {
                                    string? cityName = cityElement.GetString()?.Trim();
                                    if (!string.IsNullOrEmpty(cityName))
                                    {
                                        cityList.Add(cityName);
                                    }
                                }
                            }

                            // Ensure Turkey has all 81 provinces if missing from API
                            if (countryName.Equals("Turkey", StringComparison.OrdinalIgnoreCase) ||
                                countryName.Equals("Türkiye", StringComparison.OrdinalIgnoreCase))
                            {
                                foreach (var trProvince in AllTurkishProvinces)
                                {
                                    if (!cityList.Contains(trProvince, StringComparer.OrdinalIgnoreCase))
                                    {
                                        cityList.Add(trProvince);
                                    }
                                }
                            }

                            fetchedDict[countryName] = cityList.Distinct().OrderBy(c => c).ToList();
                        }
                    }

                    if (fetchedDict.Count > 0)
                    {
                        lock (_lock)
                        {
                            foreach (var kvp in fetchedDict)
                            {
                                _worldLocations[kvp.Key] = kvp.Value;
                            }

                            // Keep both Turkey and Türkiye accessible with all 81 provinces
                            EnsureProvincesAndAliases();
                        }

                        // Write to disk cache
                        string cachePath = CacheFilePath;
                        string serialized = JsonSerializer.Serialize(_worldLocations, new JsonSerializerOptions { WriteIndented = false });
                        await File.WriteAllTextAsync(cachePath, serialized);
                        Debug.WriteLine($"[LocationDatabaseService] Saved {fetchedDict.Count} countries to {cachePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LocationDatabaseService] Background world fetch failed (offline fallback remains active): {ex.Message}");
            }
        }

        public async Task<(string Country, string City)?> AutoDetectLocationAsync()
        {
            try
            {
                // Query ip-api.com (free, high-speed, standard location endpoint)
                string json = await _httpClient.GetStringAsync("http://ip-api.com/json/");
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("status", out JsonElement statusEl) &&
                    statusEl.GetString() == "success")
                {
                    string country = root.TryGetProperty("country", out var cEl) ? cEl.GetString() ?? "" : "";
                    string city = root.TryGetProperty("city", out var ctEl) ? ctEl.GetString() ?? "" : "";

                    if (!string.IsNullOrEmpty(country) && !string.IsNullOrEmpty(city))
                    {
                        return (country, city);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LocationDatabaseService] Auto-detect location failed: {ex.Message}");
            }

            return null;
        }

        private void LoadCuratedFallback()
        {
            lock (_lock)
            {
                // Turkey (All 81 Provinces)
                _worldLocations["Turkey"] = new List<string>(AllTurkishProvinces);
                _worldLocations["Türkiye"] = new List<string>(AllTurkishProvinces);

                // Saudi Arabia
                _worldLocations["Saudi Arabia"] = new List<string>
                {
                    "Makkah", "Madinah", "Riyadh", "Jeddah", "Dammam", "Khobar", "Taif", "Tabuk",
                    "Buraidah", "Khamis Mushait", "Abha", "Hail", "Najran", "Jizan", "Yanbu", "Al Jubail", "Al Ahsa"
                }.OrderBy(c => c).ToList();

                // United Arab Emirates
                _worldLocations["United Arab Emirates"] = new List<string>
                {
                    "Abu Dhabi", "Dubai", "Sharjah", "Ajman", "Ras Al Khaimah", "Fujairah", "Umm Al Quwain", "Al Ain"
                }.OrderBy(c => c).ToList();

                // Qatar
                _worldLocations["Qatar"] = new List<string>
                {
                    "Doha", "Al Rayyan", "Al Wakrah", "Al Khor", "Umm Salal", "Al Daayen", "Lusail"
                }.OrderBy(c => c).ToList();

                // Kuwait
                _worldLocations["Kuwait"] = new List<string>
                {
                    "Kuwait City", "Al Ahmadi", "Hawalli", "As Salimiyah", "Sabah Al Salem", "Al Farwaniyah", "Al Jahra"
                }.OrderBy(c => c).ToList();

                // Egypt
                _worldLocations["Egypt"] = new List<string>
                {
                    "Cairo", "Alexandria", "Giza", "Shubra El Kheima", "Port Said", "Suez", "El Mahalla El Kubra",
                    "Luxor", "Mansoura", "Tanta", "Asyut", "Ismailia", "Faiyum", "Zagazig", "Damietta", "Aswan"
                }.OrderBy(c => c).ToList();

                // Jordan
                _worldLocations["Jordan"] = new List<string>
                {
                    "Amman", "Zarqa", "Irbid", "Russeifa", "Al-Quwaysimah", "Wadi as-Sir", "Tila al-Ali", "Aqaba", "Salt", "Madaba"
                }.OrderBy(c => c).ToList();

                // Syria
                _worldLocations["Syria"] = new List<string>
                {
                    "Damascus", "Aleppo", "Homs", "Hama", "Latakia", "Deir ez-Zor", "Raqqa", "Idlib", "Daraa", "Tartus", "Qamishli"
                }.OrderBy(c => c).ToList();

                // Iraq
                _worldLocations["Iraq"] = new List<string>
                {
                    "Baghdad", "Basra", "Mosul", "Erbil", "Najaf", "Karbala", "Sulaymaniyah", "Kirkuk", "Nasiriyah", "Hillah", "Ramadi"
                }.OrderBy(c => c).ToList();

                // Palestine
                _worldLocations["Palestine"] = new List<string>
                {
                    "Jerusalem", "Gaza", "Ramallah", "Hebron", "Nablus", "Bethlehem", "Jenin", "Tulkarm", "Qalqilya", "Jericho", "Rafah", "Khan Yunis"
                }.OrderBy(c => c).ToList();

                // Morocco
                _worldLocations["Morocco"] = new List<string>
                {
                    "Casablanca", "Rabat", "Fes", "Marrakech", "Tangier", "Agadir", "Meknes", "Oujda", "Kenitra", "Tetouan", "Safi"
                }.OrderBy(c => c).ToList();

                // Algeria
                _worldLocations["Algeria"] = new List<string>
                {
                    "Algiers", "Oran", "Constantine", "Annaba", "Blida", "Batna", "Djelfa", "Setif", "Sidi Bel Abbes", "Biskra", "Tebessa"
                }.OrderBy(c => c).ToList();

                // Pakistan
                _worldLocations["Pakistan"] = new List<string>
                {
                    "Karachi", "Lahore", "Faisalabad", "Rawalpindi", "Gujranwala", "Peshawar", "Multan", "Islamabad", "Quetta", "Sialkot"
                }.OrderBy(c => c).ToList();

                // Indonesia
                _worldLocations["Indonesia"] = new List<string>
                {
                    "Jakarta", "Surabaya", "Bandung", "Medan", "Bekasi", "Palembang", "Tangerang", "Makassar", "South Tangerang", "Semarang"
                }.OrderBy(c => c).ToList();

                // Malaysia
                _worldLocations["Malaysia"] = new List<string>
                {
                    "Kuala Lumpur", "George Town", "Ipoh", "Shah Alam", "Petaling Jaya", "Johor Bahru", "Melaka", "Kota Kinabalu", "Kuantan", "Kuching"
                }.OrderBy(c => c).ToList();

                // United Kingdom
                _worldLocations["United Kingdom"] = new List<string>
                {
                    "London", "Birmingham", "Manchester", "Glasgow", "Leeds", "Liverpool", "Newcastle", "Sheffield", "Bristol", "Edinburgh", "Leicester", "Bradford"
                }.OrderBy(c => c).ToList();

                // United States
                _worldLocations["United States"] = new List<string>
                {
                    "New York", "Los Angeles", "Chicago", "Houston", "Phoenix", "Philadelphia", "San Antonio", "San Diego", "Dallas",
                    "San Jose", "Austin", "Jacksonville", "Fort Worth", "Columbus", "Charlotte", "San Francisco", "Indianapolis", "Seattle", "Denver", "Boston", "Detroit"
                }.OrderBy(c => c).ToList();

                // Canada
                _worldLocations["Canada"] = new List<string>
                {
                    "Toronto", "Montreal", "Vancouver", "Calgary", "Edmonton", "Ottawa", "Winnipeg", "Quebec City", "Hamilton", "Kitchener", "London", "Halifax"
                }.OrderBy(c => c).ToList();

                // Germany
                _worldLocations["Germany"] = new List<string>
                {
                    "Berlin", "Hamburg", "Munich", "Cologne", "Frankfurt", "Stuttgart", "Düsseldorf", "Leipzig", "Dortmund", "Essen", "Bremen", "Dresden", "Hannover", "Nuremberg"
                }.OrderBy(c => c).ToList();

                // France
                _worldLocations["France"] = new List<string>
                {
                    "Paris", "Marseille", "Lyon", "Toulouse", "Nice", "Nantes", "Montpellier", "Strasbourg", "Bordeaux", "Lille", "Rennes", "Reims"
                }.OrderBy(c => c).ToList();

                // Netherlands
                _worldLocations["Netherlands"] = new List<string>
                {
                    "Amsterdam", "Rotterdam", "The Hague", "Utrecht", "Eindhoven", "Groningen", "Tilburg", "Almere", "Breda", "Nijmegen"
                }.OrderBy(c => c).ToList();
            }
        }

        public static readonly string[] AllTurkishProvinces = new[]
        {
            "Adana", "Adıyaman", "Afyonkarahisar", "Ağrı", "Aksaray", "Amasya", "Ankara", "Antalya",
            "Ardahan", "Artvin", "Aydın", "Balıkesir", "Bartın", "Batman", "Bayburt", "Bilecik",
            "Bingöl", "Bitlis", "Bolu", "Burdur", "Bursa", "Çanakkale", "Çankırı", "Çorum",
            "Denizli", "Diyarbakır", "Düzce", "Edirne", "Elazığ", "Erzincan", "Erzurum", "Eskişehir",
            "Gaziantep", "Giresun", "Gümüşhane", "Hakkâri", "Hatay", "Iğdır", "Isparta", "Istanbul", "İstanbul",
            "Izmir", "İzmir", "Kahramanmaraş", "Karabük", "Karaman", "Kars", "Kastamonu", "Kayseri", "Kilis",
            "Kırıkkale", "Kırklareli", "Kırşehir", "Kocaeli", "Konya", "Kütahya", "Malatya", "Manisa",
            "Mardin", "Mersin", "Muğla", "Muş", "Nevşehir", "Niğde", "Ordu", "Osmaniye",
            "Rize", "Sakarya", "Samsun", "Şanlıurfa", "Sanliurfa", "Siirt", "Sinop", "Sivas", "Şırnak",
            "Tekirdağ", "Tokat", "Trabzon", "Tunceli", "Uşak", "Van", "Yalova", "Yozgat", "Zonguldak"
        };
    }
}
