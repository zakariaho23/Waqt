using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Toolkit.Uwp.Notifications;
using Waqt.Models;

namespace Waqt.Services
{
    public class NotificationAndAudioService
    {
        private MediaPlayer? _mediaPlayer;

        public bool IsPlaying { get; private set; }
        public event Action<bool>? OnPlaybackStateChanged;

        public void SendPrayerToast(PrayerType prayer, string time, string city)
        {
            try
            {
                new ToastContentBuilder()
                    .AddAttributionText("Waqt • Islamic Prayer Times")
                    .AddText($"🕌 {prayer} Prayer Time")
                    .AddText($"It's time for {prayer} ({time}) in {city}")
                    .Show();
            }
            catch (Exception)
            {
                // Toast notifications may not be available in all environments
            }
        }

        public void SendPreReminderToast(PrayerType prayer, int minutesBefore, string city)
        {
            try
            {
                new ToastContentBuilder()
                    .AddAttributionText("Waqt • Islamic Prayer Times")
                    .AddText($"🕌 {prayer} Prayer Reminder")
                    .AddText($"{prayer} in {minutesBefore} minutes in {city}")
                    .Show();
            }
            catch (Exception)
            {
                // Toast notifications may not be available in all environments
            }
        }

        public void PlayAthanAudio(string? customFilePath, double volume)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                StopAudio();

                _mediaPlayer = new MediaPlayer();

                string filePath;
                if (!string.IsNullOrEmpty(customFilePath) && File.Exists(customFilePath))
                {
                    filePath = customFilePath;
                }
                else
                {
                    filePath = EnsureDefaultChimeExists();
                }

                _mediaPlayer.MediaEnded += (s, e) =>
                {
                    IsPlaying = false;
                    OnPlaybackStateChanged?.Invoke(false);
                };
                _mediaPlayer.MediaFailed += (s, e) =>
                {
                    IsPlaying = false;
                    OnPlaybackStateChanged?.Invoke(false);
                };

                _mediaPlayer.Volume = Math.Clamp(volume, 0.0, 1.0);
                _mediaPlayer.Open(new Uri(filePath, UriKind.Absolute));
                _mediaPlayer.Play();

                IsPlaying = true;
                OnPlaybackStateChanged?.Invoke(true);
            });
        }

        public void StopAudio()
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Stop();
                _mediaPlayer.Close();
                _mediaPlayer = null;
            }

            if (IsPlaying)
            {
                IsPlaying = false;
                OnPlaybackStateChanged?.Invoke(false);
            }
        }

        public static void GenerateDefaultChime(string outputPath)
        {
            const int sampleRate = 44100;
            const int durationSeconds = 2;
            const int totalSamples = sampleRate * durationSeconds;
            const int bitsPerSample = 16;
            const int channels = 1;
            const int bytesPerSample = bitsPerSample / 8;
            int dataSize = totalSamples * channels * bytesPerSample;

            // Harmonic frequencies: A major chord (A4, C#5, E5)
            double[] frequencies = { 440.0, 554.37, 659.26 };
            double[] amplitudes = { 0.45, 0.30, 0.25 };

            short[] samples = new short[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                double t = (double)i / sampleRate;
                double sample = 0.0;

                for (int h = 0; h < frequencies.Length; h++)
                {
                    sample += amplitudes[h] * Math.Sin(2.0 * Math.PI * frequencies[h] * t);
                }

                // Apply envelope: fade-in (first 5%) and fade-out (last 40%)
                double envelope = 1.0;
                double position = (double)i / totalSamples;

                if (position < 0.05)
                {
                    // Fade in over first 5%
                    envelope = position / 0.05;
                }
                else if (position > 0.60)
                {
                    // Fade out over last 40%
                    envelope = (1.0 - position) / 0.40;
                }

                // Apply a gentle exponential decay for a more natural chime sound
                double decay = Math.Exp(-t * 1.5);
                sample *= envelope * decay;

                // Clamp and convert to 16-bit
                sample = Math.Clamp(sample, -1.0, 1.0);
                samples[i] = (short)(sample * short.MaxValue);
            }

            // Write WAV file
            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(stream);

            // RIFF header
            writer.Write(new char[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataSize); // File size - 8
            writer.Write(new char[] { 'W', 'A', 'V', 'E' });

            // fmt sub-chunk
            writer.Write(new char[] { 'f', 'm', 't', ' ' });
            writer.Write(16);                   // Sub-chunk size (PCM)
            writer.Write((short)1);             // Audio format (1 = PCM)
            writer.Write((short)channels);      // Number of channels
            writer.Write(sampleRate);           // Sample rate
            writer.Write(sampleRate * channels * bytesPerSample); // Byte rate
            writer.Write((short)(channels * bytesPerSample));     // Block align
            writer.Write((short)bitsPerSample); // Bits per sample

            // data sub-chunk
            writer.Write(new char[] { 'd', 'a', 't', 'a' });
            writer.Write(dataSize);

            for (int i = 0; i < totalSamples; i++)
            {
                writer.Write(samples[i]);
            }
        }

        public string EnsureDefaultChimeExists()
        {
            string appDataPath = AppSettings.GetAppDataPath();
            string chimePath = Path.Combine(appDataPath, "default_athan.wav");

            if (!File.Exists(chimePath))
            {
                GenerateDefaultChime(chimePath);
            }

            return chimePath;
        }
    }
}
