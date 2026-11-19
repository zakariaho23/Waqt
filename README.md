# 🕌 Waqt (وقت)

> A modern, lightweight Windows 11 Islamic Prayer Times, Athan, and Qibla utility that lives seamlessly in your system tray.

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Windows 11 Fluent 2](https://img.shields.io/badge/UI-Windows%2011%20Fluent-00D2B4)](https://github.com/lepoco/wpfui)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

---

## ✨ Overview

**Waqt** is crafted to provide a distraction-free, elegant, and native Windows 11 experience for tracking daily Islamic prayers. Designed with an **Obsidian & Celestial Mint** aesthetic, it delivers accurate prayer calculations, custom Athan playback, native Windows notifications, and an interactive Qibla compass right from your taskbar.

---

## 🌟 Key Features

### ⏱️ Dynamic System Tray Integration
- **Live Minute Counter**: The tray icon dynamically renders the exact minutes remaining until the next prayer (e.g. `15m`, `2h`).
- **Quick-Access Context Menu**: Right-click the tray icon to see the next prayer, copy today's timetable, mute/unmute Athan, trigger sync, or launch Settings.
- **Background Flyout**: Click the tray icon to slide open the flyout; automatically hides when clicking away (or pin it to stay open).

### 🕌 Prayer Timings & Dashboard
- **Next Prayer Hero Card**: Displays live countdown timer (`HH:mm:ss`), prayer name, and smooth progress bar.
- **6 Daily Rows**: Fajr, Sunrise, Dhuhr, Asr, Maghrib, and Isha.
- **Visual Status**: Past prayers are subtly dimmed; the next upcoming prayer is highlighted with an emerald badge.
- **Per-Prayer Alerts**: Individual bell icons to enable or silence notifications for specific prayers.
- **Dual Calendar**: Displays both Gregorian and Hijri dates simultaneously.

### 🧭 Interactive Qibla Compass
- **Kaaba Alignment**: Mathematically computed great-circle bearing and distance to the Holy Kaaba in Makkah.
- **Provincial Accuracy**: Built-in high-precision coordinates for all 81 provinces of Turkey, Middle East capitals, and global metropolises.
- **Room / Desk Heading Calibration**: Interactive slider (`0°` to `359°`) and quick presets (`N`, `E`, `S`, `W`, `🎯 Align to Qibla`) to orient the compass relative to your desk or room.
- **Live Turning Guidance**: Dynamic directional cues (e.g., *"Turn 14° to your right to face the Kaaba"*).
- **Alignment Badge**: Lights up with a glowing `YOU ARE FACING THE QIBLA ✓` banner when facing within $\pm 6^\circ$.
- **Hardware Sensor Support**: Automatically reads physical compass sensors (`Windows.Devices.Sensors.Compass`) on 2-in-1 convertible laptops and tablets.

### 📅 Monthly Imsakiyah Timetable
- View the full 30-day schedule for the current month.
- 7 comprehensive columns: `Date`, `Fajr`, `Sunrise`, `Dhuhr`, `Asr`, `Maghrib`, and `Isha`.
- Today's date is automatically highlighted.
- **One-Click Export**: Easily copy the entire month's schedule to your clipboard formatted as clean text.

### 📖 Daily Quran Ayah & Reflection
- Daily hand-curated Ayah in authentic Arabic script with English translation.
- Clearly states both the **Surah name and Ayah number** (e.g., *Surah Ar-Ra'd • Ayah 28*).
- **Shuffle Button**: Discover another uplifting verse at any time.
- **Copy Ayah Button**: One-click copying with visual feedback.

### 🔔 Native Notifications & Athan Audio
- **Windows 11 Toast Alerts**: Rich banner notifications when prayer time arrives.
- **Pre-Prayer Reminders**: Configurable warnings (5, 10, 15, or 20 minutes before Salah).
- **Custom Athan**: Choose between a built-in harmonic chime or your own `.mp3` / `.wav` audio file.
- **Master Mute & Volume Slider**: Quick volume controls in the footer and tray menu.

### ⚙️ Calibration & Calculation
- **Calculation Methods**: Diyanet (Turkey), Muslim World League (MWL), ISNA, Umm Al-Qura (Makkah), Egyptian General Authority, Karachi, Tehran, and more.
- **Asr Juristic Schools**: Standard (Shafi'i/Maliki/Hanbali) or Hanafi.
- **Local Mosque Calibration**: Fine-tune each prayer time by $\pm 15$ minutes using dedicated calibration sliders.
- **Windows Startup**: Optional toggle to launch automatically when you sign in to Windows.
- **Offline Caching**: Schedules are cached locally so the app functions seamlessly without an active internet connection.

---

## 🛠️ Technology Stack

- **Framework**: [.NET 8](https://dotnet.microsoft.com/) (`net8.0-windows10.0.19041.0`)
- **UI Platform**: Windows Presentation Foundation (WPF) with XAML
- **Styling**: [WPF-UI 4.3.0](https://github.com/lepoco/wpfui) (Windows 11 Fluent Design System)
- **Tray Icon**: [H.NotifyIcon.Wpf](https://github.com/HavenDV/H.NotifyIcon)
- **Notifications**: [Microsoft.Toolkit.Uwp.Notifications](https://github.com/CommunityToolkit/WindowsCommunityToolkit)
- **API**: [Al-Adhan API](https://aladhan.com/prayer-times-api) for worldwide timings & monthly schedules

---

## 🚀 Building & Running from Source

### Prerequisites
- Windows 10 (version 19041+) or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 / VS Code / Antigravity IDE with C# support

### Steps
1. Clone the repository:
   ```bash
   git clone https://github.com/zakariaho23/Waqt.git
   cd Waqt
   ```
2. Build the solution:
   ```bash
   dotnet build Waqt.sln -c Release
   ```
3. Run the application:
   ```bash
   dotnet run --project Waqt.csproj
   ```
   *(The application will start minimized in your Windows System Tray)*.

---

## 👤 Author

Developed with care by **Zakaria Haj Othman**  
- GitHub: [@zakariaho23](https://github.com/zakariaho23)

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
