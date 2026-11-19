using System;

namespace Waqt.Services
{
    public class QuranVerse
    {
        public string ArabicText { get; set; } = string.Empty;
        public string EnglishText { get; set; } = string.Empty;
        public string SurahName { get; set; } = string.Empty;
        public int SurahNumber { get; set; }
        public string AyahNumber { get; set; } = string.Empty;
        public string Theme { get; set; } = string.Empty;

        /// <summary>
        /// Explicit formatted reference stating both the Surah name and Ayah number.
        /// e.g. "Surah Ar-Ra'd • Ayah 28 (13:28)"
        /// </summary>
        public string Reference => $"Surah {SurahName} • Ayah {AyahNumber} ({SurahNumber}:{AyahNumber})";
    }

    public static class DailyVerseService
    {
        private static readonly QuranVerse[] Verses = new[]
        {
            new QuranVerse
            {
                ArabicText = "أَلَا بِذِكْرِ اللَّهِ تَطْمَئِنُّ الْقُلُوبُ",
                EnglishText = "Verily, in the remembrance of Allah do hearts find rest.",
                SurahName = "Ar-Ra'd",
                SurahNumber = 13,
                AyahNumber = "28",
                Theme = "Peace of Mind"
            },
            new QuranVerse
            {
                ArabicText = "إِنَّ الصَّلَاةَ كَانَتْ عَلَى الْمُؤْمِنِينَ كِتَابًا مَّوْقُوتًا",
                EnglishText = "Indeed, prayer has been decreed upon the believers at prescribed times.",
                SurahName = "An-Nisa",
                SurahNumber = 4,
                AyahNumber = "103",
                Theme = "Prayer"
            },
            new QuranVerse
            {
                ArabicText = "فَإِنَّ مَعَ الْعُسْرِ يُسْرًا • إِنَّ مَعَ الْعُسْرِ يُسْرًا",
                EnglishText = "For indeed, with hardship [will be] ease. Indeed, with hardship [will be] ease.",
                SurahName = "Ash-Sharh",
                SurahNumber = 94,
                AyahNumber = "5-6",
                Theme = "Hope & Relief"
            },
            new QuranVerse
            {
                ArabicText = "وَقَالَ رَبُّكُمُ ادْعُونِي أَسْتَجِبْ لَكُمْ",
                EnglishText = "And your Lord says: Call upon Me; I will respond to you.",
                SurahName = "Ghafir",
                SurahNumber = 40,
                AyahNumber = "60",
                Theme = "Supplication"
            },
            new QuranVerse
            {
                ArabicText = "وَاسْتَعِينُوا بِالصَّبْرِ وَالصَّلَاةِ",
                EnglishText = "And seek help through patience and prayer.",
                SurahName = "Al-Baqarah",
                SurahNumber = 2,
                AyahNumber = "45",
                Theme = "Patience"
            },
            new QuranVerse
            {
                ArabicText = "وَمَن يَتَّقِ اللَّهَ يَجْعَل لَّهُ مَخْرَجًا وَيَرْزُقْهُ مِنْ حَيْثُ لَا يَحْتَسِبُ",
                EnglishText = "And whoever is mindful of Allah, He will make for him a way out and provide for him from where he does not expect.",
                SurahName = "At-Talaq",
                SurahNumber = 65,
                AyahNumber = "2-3",
                Theme = "Tawakkul"
            },
            new QuranVerse
            {
                ArabicText = "حَافِظُوا عَلَى الصَّلَوَاتِ وَالصَّلَاةِ الْوُسْطَىٰ وَقُومُوا لِلَّهِ قَانِتِينَ",
                EnglishText = "Maintain with care the prayers and [in particular] the middle prayer and stand before Allah, devoutly obedient.",
                SurahName = "Al-Baqarah",
                SurahNumber = 2,
                AyahNumber = "238",
                Theme = "Steadfastness"
            },
            new QuranVerse
            {
                ArabicText = "رَبَّنَا آتِنَا فِي الدُّنْيَا حَسَنَةً وَفِي الْآخِرَةِ حَسَنَةً وَقِنَا عَذَابَ النَّارِ",
                EnglishText = "Our Lord, give us in this world that which is good and in the Hereafter that which is good and protect us from the punishment of the Fire.",
                SurahName = "Al-Baqarah",
                SurahNumber = 2,
                AyahNumber = "201",
                Theme = "Supplication"
            },
            new QuranVerse
            {
                ArabicText = "إِنَّ اللَّهَ مَعَ الصَّابِرِينَ",
                EnglishText = "Indeed, Allah is with the patient.",
                SurahName = "Al-Baqarah",
                SurahNumber = 2,
                AyahNumber = "153",
                Theme = "Patience"
            },
            new QuranVerse
            {
                ArabicText = "وَأَقِمِ الصَّلَاةَ طَرَفَيِ النَّهَارِ وَزُلَفًا مِّنَ اللَّيْلِ ۚ إِنَّ الْحَسَنَاتِ يُذْهِبْنَ السَّيِّئَاتِ",
                EnglishText = "And establish prayer at the two ends of the day and at the approach of the night. Indeed, good deeds do away with misdeeds.",
                SurahName = "Hud",
                SurahNumber = 11,
                AyahNumber = "114",
                Theme = "Good Deeds"
            },
            new QuranVerse
            {
                ArabicText = "فَاذْكُرُونِي أَذْكُرْكُمْ وَاشْكُرُوا لِي وَلَا تَكْفُرُونِ",
                EnglishText = "So remember Me; I will remember you. And be grateful to Me and do not deny Me.",
                SurahName = "Al-Baqarah",
                SurahNumber = 2,
                AyahNumber = "152",
                Theme = "Gratitude"
            },
            new QuranVerse
            {
                ArabicText = "قُلْ يَا عِبَادِيَ الَّذِينَ أَسْرَفُوا عَلَىٰ أَنفُسِهِمْ لَا تَقْنَطُوا مِن رَّحْمَةِ اللَّهِ",
                EnglishText = "Say, O My servants who have transgressed against themselves, do not despair of the mercy of Allah. Indeed, Allah forgives all sins.",
                SurahName = "Az-Zumar",
                SurahNumber = 39,
                AyahNumber = "53",
                Theme = "Mercy & Forgiveness"
            },
            new QuranVerse
            {
                ArabicText = "وَرَحْمَتِي وَسِعَتْ كُلَّ شَيْءٍ",
                EnglishText = "And My mercy encompasses all things.",
                SurahName = "Al-A'raf",
                SurahNumber = 7,
                AyahNumber = "156",
                Theme = "Divine Mercy"
            },
            new QuranVerse
            {
                ArabicText = "قَدْ أَفْلَحَ الْمُؤْمِنُونَ • الَّذِينَ هُمْ فِي صَلَاتِهِمْ خَاشِعُونَ",
                EnglishText = "Certainly will the believers have succeeded: They who are during their prayer humbly submissive.",
                SurahName = "Al-Mu'minun",
                SurahNumber = 23,
                AyahNumber = "1-2",
                Theme = "Humility in Prayer"
            },
            new QuranVerse
            {
                ArabicText = "وَتَوَكَّلْ عَلَى الْحَيِّ الَّذِي لَا يَمُوتُ",
                EnglishText = "And put your trust in the Ever-Living who does not die.",
                SurahName = "Al-Furqan",
                SurahNumber = 25,
                AyahNumber = "58",
                Theme = "Reliance on Allah"
            },
            new QuranVerse
            {
                ArabicText = "وَإِذَا سَأَلَكَ عِبَادِي عَنِّي فَإِنِّي قَرِيبٌ ۖ أُجِيبُ دَعْوَةَ الدَّاعِ إِذَا دَعَانِ",
                EnglishText = "And when My servants ask you concerning Me, indeed I am near. I respond to the invocation of the supplicant when he calls upon Me.",
                SurahName = "Al-Baqarah",
                SurahNumber = 2,
                AyahNumber = "186",
                Theme = "Closeness to Allah"
            },
            new QuranVerse
            {
                ArabicText = "وَلَسَوْفَ يُعْطِيكَ رَبُّكَ فَتَرْضَىٰ",
                EnglishText = "And your Lord is going to give you, and you will be satisfied.",
                SurahName = "Ad-Duha",
                SurahNumber = 93,
                AyahNumber = "5",
                Theme = "Hope & Contentment"
            },
            new QuranVerse
            {
                ArabicText = "لَئِن شَكَرْتُمْ لَأَزِيدَنَّكُمْ",
                EnglishText = "If you are grateful, I will surely increase you [in favor].",
                SurahName = "Ibrahim",
                SurahNumber = 14,
                AyahNumber = "7",
                Theme = "Gratitude"
            },
            new QuranVerse
            {
                ArabicText = "حَسْبُنَا اللَّهُ وَنِعْمَ الْوَكِيلُ",
                EnglishText = "Sufficient for us is Allah, and [He is] the best Disposer of affairs.",
                SurahName = "Ali 'Imran",
                SurahNumber = 3,
                AyahNumber = "173",
                Theme = "Trust in Allah"
            },
            new QuranVerse
            {
                ArabicText = "إِنَّ مَعِيَ رَبِّي سَيَهْدِينِ",
                EnglishText = "Indeed, with me is my Lord; He will guide me.",
                SurahName = "Ash-Shu'ara",
                SurahNumber = 26,
                AyahNumber = "62",
                Theme = "Guidance"
            }
        };

        private static int _customIndex = -1;

        public static QuranVerse GetTodayVerse()
        {
            if (_customIndex >= 0)
            {
                return Verses[_customIndex % Verses.Length];
            }

            int dayOfYear = DateTime.Now.DayOfYear;
            return Verses[dayOfYear % Verses.Length];
        }

        public static QuranVerse GetNextVerse()
        {
            if (_customIndex < 0)
            {
                _customIndex = DateTime.Now.DayOfYear;
            }
            _customIndex = (_customIndex + 1) % Verses.Length;
            return Verses[_customIndex];
        }
    }
}
