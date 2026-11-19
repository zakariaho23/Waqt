using System;
using System.Collections.Generic;

namespace Waqt.Services
{
    public class QiblaResult
    {
        public double BearingDegrees { get; set; }
        public string CardinalDirection { get; set; } = string.Empty;
        public double DistanceKm { get; set; }
        public double UserLatitude { get; set; }
        public double UserLongitude { get; set; }
    }

    public static class QiblaService
    {
        // Holy Kaaba Coordinates in Makkah al-Mukarramah
        public const double KaabaLatitude = 21.422487;
        public const double KaabaLongitude = 39.826206;
        private const double EarthRadiusKm = 6371.0;

        private static readonly Dictionary<string, (double Lat, double Lng)> KnownCityCoordinates = new(StringComparer.OrdinalIgnoreCase)
        {
            // All 81 Provinces of Turkey (with both Turkish and ASCII names)
            { "Sanliurfa", (37.1674, 38.7955) },
            { "Şanlıurfa", (37.1674, 38.7955) },
            { "Urfa", (37.1674, 38.7955) },
            { "Istanbul", (41.0082, 28.9784) },
            { "İstanbul", (41.0082, 28.9784) },
            { "Ankara", (39.9334, 32.8597) },
            { "Izmir", (38.4237, 27.1428) },
            { "İzmir", (38.4237, 27.1428) },
            { "Bursa", (40.1885, 29.0610) },
            { "Antalya", (36.8969, 30.7133) },
            { "Adana", (37.0000, 35.3213) },
            { "Adiyaman", (37.7648, 38.2786) },
            { "Adıyaman", (37.7648, 38.2786) },
            { "Afyonkarahisar", (38.7507, 30.5567) },
            { "Afyon", (38.7507, 30.5567) },
            { "Agri", (39.7191, 43.0503) },
            { "Ağrı", (39.7191, 43.0503) },
            { "Amasya", (40.6533, 35.8331) },
            { "Artvin", (41.1828, 41.8183) },
            { "Aydin", (37.8560, 27.8416) },
            { "Aydın", (37.8560, 27.8416) },
            { "Balikesir", (39.6484, 27.8826) },
            { "Balıkesir", (39.6484, 27.8826) },
            { "Bilecik", (40.1451, 29.9799) },
            { "Bingol", (38.8853, 40.4983) },
            { "Bingöl", (38.8853, 40.4983) },
            { "Bitlis", (38.4006, 42.1095) },
            { "Bolu", (40.7350, 31.6061) },
            { "Burdur", (37.7203, 30.2908) },
            { "Canakkale", (40.1553, 26.4142) },
            { "Çanakkale", (40.1553, 26.4142) },
            { "Cankiri", (40.6013, 33.6134) },
            { "Çankırı", (40.6013, 33.6134) },
            { "Corum", (40.5506, 34.9556) },
            { "Çorum", (40.5506, 34.9556) },
            { "Denizli", (37.7765, 29.0864) },
            { "Diyarbakir", (37.9144, 40.2306) },
            { "Diyarbakır", (37.9144, 40.2306) },
            { "Edirne", (41.6772, 26.5557) },
            { "Elazig", (38.6810, 39.2264) },
            { "Elazığ", (38.6810, 39.2264) },
            { "Erzincan", (39.7500, 39.5000) },
            { "Erzurum", (39.9000, 41.2700) },
            { "Eskisehir", (39.7767, 30.5206) },
            { "Eskişehir", (39.7767, 30.5206) },
            { "Gaziantep", (37.0662, 37.3833) },
            { "Giresun", (40.9128, 38.3895) },
            { "Gumushane", (40.4600, 39.4700) },
            { "Gümüşhane", (40.4600, 39.4700) },
            { "Hakkari", (37.5833, 43.7333) },
            { "Hakkâri", (37.5833, 43.7333) },
            { "Hatay", (36.2023, 36.1606) },
            { "Antakya", (36.2023, 36.1606) },
            { "Isparta", (37.7648, 30.5566) },
            { "Mersin", (36.8121, 34.6415) },
            { "Icel", (36.8121, 34.6415) },
            { "İçel", (36.8121, 34.6415) },
            { "Kars", (40.6167, 43.1000) },
            { "Kastamonu", (41.3887, 33.7827) },
            { "Kayseri", (38.7312, 35.4787) },
            { "Kirklareli", (41.7333, 27.2167) },
            { "Kırklareli", (41.7333, 27.2167) },
            { "Kirsehir", (39.1425, 34.1709) },
            { "Kırşehir", (39.1425, 34.1709) },
            { "Kocaeli", (40.8533, 29.8815) },
            { "Izmit", (40.7654, 29.9408) },
            { "İzmit", (40.7654, 29.9408) },
            { "Konya", (37.8746, 32.4932) },
            { "Kutahya", (39.4167, 29.9833) },
            { "Kütahya", (39.4167, 29.9833) },
            { "Malatya", (38.3552, 38.3095) },
            { "Manisa", (38.6191, 27.4289) },
            { "Kahramanmaras", (37.5858, 36.9371) },
            { "Kahramanmaraş", (37.5858, 36.9371) },
            { "Maras", (37.5858, 36.9371) },
            { "Mardin", (37.3212, 40.7245) },
            { "Mugla", (37.2153, 28.3636) },
            { "Muğla", (37.2153, 28.3636) },
            { "Mus", (38.7432, 41.5064) },
            { "Muş", (38.7432, 41.5064) },
            { "Nevsehir", (38.6244, 34.7144) },
            { "Nevşehir", (38.6244, 34.7144) },
            { "Nigde", (37.9667, 34.6833) },
            { "Niğde", (37.9667, 34.6833) },
            { "Ordu", (40.9839, 37.8764) },
            { "Rize", (41.0201, 40.5234) },
            { "Sakarya", (40.7569, 30.3783) },
            { "Adapazari", (40.7833, 30.4000) },
            { "Adapazarı", (40.7833, 30.4000) },
            { "Samsun", (41.2867, 36.3300) },
            { "Siirt", (37.9333, 41.9500) },
            { "Sinop", (42.0231, 35.1531) },
            { "Sivas", (39.7477, 37.0179) },
            { "Tekirdag", (40.9833, 27.5167) },
            { "Tekirdağ", (40.9833, 27.5167) },
            { "Tokat", (40.3167, 36.5500) },
            { "Trabzon", (41.0027, 39.7168) },
            { "Tunceli", (39.1079, 39.5401) },
            { "Usak", (38.6823, 29.4082) },
            { "Uşak", (38.6823, 29.4082) },
            { "Van", (38.4891, 43.4089) },
            { "Yozgat", (39.8181, 34.8147) },
            { "Zonguldak", (41.4564, 31.7987) },
            { "Aksaray", (38.3687, 34.0370) },
            { "Bayburt", (40.2552, 40.2249) },
            { "Karaman", (37.1759, 33.2287) },
            { "Kirikkale", (39.8468, 33.5153) },
            { "Kırıkkale", (39.8468, 33.5153) },
            { "Batman", (37.8812, 41.1351) },
            { "Sirnak", (37.5164, 42.4594) },
            { "Şırnak", (37.5164, 42.4594) },
            { "Bartin", (41.6344, 32.3375) },
            { "Bartın", (41.6344, 32.3375) },
            { "Ardahan", (41.1105, 42.7022) },
            { "Igdir", (39.9237, 44.0450) },
            { "Iğdır", (39.9237, 44.0450) },
            { "Yalova", (40.6500, 29.2667) },
            { "Karabuk", (41.2061, 32.6204) },
            { "Karabük", (41.2061, 32.6204) },
            { "Kilis", (36.7184, 37.1212) },
            { "Osmaniye", (37.0742, 36.2472) },
            { "Duzce", (40.8438, 31.1565) },
            { "Düzce", (40.8438, 31.1565) },

            // Middle East & Islamic Capitals
            { "Makkah", (21.4225, 39.8262) },
            { "Mecca", (21.4225, 39.8262) },
            { "Madinah", (24.4672, 39.6111) },
            { "Medina", (24.4672, 39.6111) },
            { "Riyadh", (24.7136, 46.6753) },
            { "Jeddah", (21.4858, 39.1925) },
            { "Dubai", (25.2048, 55.2708) },
            { "Abu Dhabi", (24.4539, 54.3773) },
            { "Doha", (25.2854, 51.5310) },
            { "Kuwait City", (29.3759, 47.9774) },
            { "Cairo", (30.0444, 31.2357) },
            { "Alexandria", (31.2001, 29.9187) },
            { "Amman", (31.9454, 35.9284) },
            { "Jerusalem", (31.7683, 35.2137) },
            { "Al-Quds", (31.7683, 35.2137) },
            { "Damascus", (33.5138, 36.2765) },
            { "Aleppo", (36.2021, 37.1343) },
            { "Baghdad", (33.3152, 44.3661) },
            { "Beirut", (33.8938, 35.5018) },
            { "Casablanca", (33.5731, -7.5898) },
            { "Rabat", (34.0209, -6.8416) },
            { "Algiers", (36.7538, 3.0588) },
            { "Tunis", (36.8065, 10.1815) },
            { "Tripoli", (32.8872, 13.1913) },
            { "Muscat", (23.5880, 58.3829) },
            { "Manama", (26.2285, 50.5860) },
            { "Tehran", (35.6892, 51.3890) },

            // World Metropolises
            { "London", (51.5074, -0.1278) },
            { "Paris", (48.8566, 2.3522) },
            { "Berlin", (52.5200, 13.4050) },
            { "New York", (40.7128, -74.0060) },
            { "Los Angeles", (34.0522, -118.2437) },
            { "Chicago", (41.8781, -87.6298) },
            { "Toronto", (43.6532, -79.3832) },
            { "Sydney", (-33.8688, 151.2093) },
            { "Jakarta", (-6.2088, 106.8456) },
            { "Kuala Lumpur", (3.1390, 101.6869) },
            { "Islamabad", (33.6844, 73.0479) },
            { "Lahore", (31.5204, 74.3587) },
            { "Karachi", (24.8607, 67.0011) },
            { "Dhaka", (23.8103, 90.4125) },
            { "Tokyo", (35.6762, 139.6503) },
            { "Amsterdam", (52.3676, 4.9041) },
            { "Brussels", (50.8503, 4.3517) },
            { "Vienna", (48.2082, 16.3738) }
        };

        public static (double Lat, double Lng)? GetKnownCoordinates(string? city)
        {
            if (string.IsNullOrWhiteSpace(city)) return null;

            string clean = city.Trim();
            if (KnownCityCoordinates.TryGetValue(clean, out var coords))
            {
                return coords;
            }

            // Normalization for Turkish/international spellings
            string normalized = clean
                .Replace("ş", "s").Replace("Ş", "S")
                .Replace("ı", "i").Replace("İ", "I")
                .Replace("ğ", "g").Replace("Ğ", "G")
                .Replace("ü", "u").Replace("Ü", "U")
                .Replace("ö", "o").Replace("Ö", "O")
                .Replace("ç", "c").Replace("Ç", "C")
                .Replace("â", "a").Replace("Â", "A")
                .Replace("î", "i").Replace("Î", "I")
                .Replace("û", "u").Replace("Û", "U");

            if (KnownCityCoordinates.TryGetValue(normalized, out var normCoords))
            {
                return normCoords;
            }

            return null;
        }

        /// <summary>
        /// Calculates the Great-Circle Qibla bearing and distance to the Kaaba.
        /// </summary>
        public static QiblaResult Calculate(double userLat, double userLng)
        {
            double lat1 = userLat * Math.PI / 180.0;
            double lon1 = userLng * Math.PI / 180.0;
            double lat2 = KaabaLatitude * Math.PI / 180.0;
            double lon2 = KaabaLongitude * Math.PI / 180.0;

            double deltaLon = lon2 - lon1;

            double y = Math.Sin(deltaLon) * Math.Cos(lat2);
            double x = (Math.Cos(lat1) * Math.Sin(lat2)) - (Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(deltaLon));

            double bearingRad = Math.Atan2(y, x);
            double bearingDeg = (bearingRad * 180.0 / Math.PI + 360.0) % 360.0;

            // Haversine distance
            double deltaLat = lat2 - lat1;
            double a = Math.Sin(deltaLat / 2.0) * Math.Sin(deltaLat / 2.0) +
                       Math.Cos(lat1) * Math.Cos(lat2) *
                       Math.Sin(deltaLon / 2.0) * Math.Sin(deltaLon / 2.0);
            double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
            double distanceKm = EarthRadiusKm * c;

            return new QiblaResult
            {
                BearingDegrees = Math.Round(bearingDeg, 1),
                CardinalDirection = GetCardinalDirection(bearingDeg),
                DistanceKm = Math.Round(distanceKm, 0),
                UserLatitude = Math.Round(userLat, 4),
                UserLongitude = Math.Round(userLng, 4)
            };
        }

        private static string GetCardinalDirection(double degrees)
        {
            string[] directions = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
                                    "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
            int index = (int)Math.Round((degrees % 360) / 22.5) % 16;
            return directions[index];
        }
    }
}
