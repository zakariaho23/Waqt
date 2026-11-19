using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Waqt.Views;

namespace Waqt
{
    public partial class App : System.Windows.Application
    {
        private static Mutex? _mutex;
        private static EventWaitHandle? _showEvent;
        private Thread? _signalListenerThread;

        /// <summary>
        /// Strongly-typed accessor for the application's MainWindow.
        /// </summary>
        public MainWindow? AppMainWindow => MainWindow as MainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // --- Single-instance guard ---
            _mutex = new Mutex(true, @"Global\Waqt_SingleInstance_Mutex", out bool createdNew);

            if (!createdNew)
            {
                // Another instance is already running — signal it to show its flyout and exit.
                try
                {
                    using var signal = EventWaitHandle.OpenExisting(@"Global\Waqt_ShowFlyout_Event");
                    signal.Set();
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    // The other instance hasn't created the event yet; nothing we can do.
                }

                Shutdown();
                return;
            }

            // --- Create the EventWaitHandle that other instances will signal ---
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Global\Waqt_ShowFlyout_Event");

            // Background thread: waits for the signal and marshals ShowFlyout onto the UI thread.
            _signalListenerThread = new Thread(() =>
            {
                while (_showEvent != null)
                {
                    try
                    {
                        if (_showEvent.WaitOne(Timeout.Infinite))
                        {
                            Dispatcher.BeginInvoke(() =>
                            {
                                AppMainWindow?.ShowFlyout();
                            });
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        // Handle was disposed during shutdown — exit the loop.
                        break;
                    }
                }
            })
            {
                IsBackground = true,
                Name = "Waqt_SignalListener"
            };
            _signalListenerThread.Start();

            // --- Global unhandled exception handlers ---
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // --- Create MainWindow (stays hidden — it lives in the system tray) ---
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            // Kick off async initialization (data loading, engine start, etc.)
            _ = mainWindow.InitializeAsync();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Dispose the EventWaitHandle so the listener thread exits cleanly.
            try
            {
                _showEvent?.Dispose();
                _showEvent = null;
            }
            catch (ObjectDisposedException)
            {
                // Already disposed.
            }

            // Release and dispose the mutex.
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Mutex was not owned — safe to ignore.
            }
            finally
            {
                _mutex?.Dispose();
                _mutex = null;
            }

            // Dispose active dynamic tray icon
            try
            {
                Waqt.Services.TrayIconRenderer.DisposeActiveIcon();
            }
            catch
            {
                // Ignore cleanup errors
            }

            base.OnExit(e);
        }

        /// <summary>
        /// Handles unhandled exceptions on background threads / finalizers.
        /// Logs the error so it can be diagnosed, then lets the app terminate.
        /// </summary>
        private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogFatalException(ex);
            }
        }

        /// <summary>
        /// Handles unhandled exceptions on the WPF dispatcher thread.
        /// Marks them as handled to prevent an instant crash, logs, and shuts down gracefully.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogFatalException(e.Exception);
            e.Handled = true;

            // Give the user a simple error message, then shut down.
            System.Windows.MessageBox.Show(
                $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nThe application will now close.",
                "Waqt — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }

        /// <summary>
        /// Writes the exception details to a crash log in the app-data folder.
        /// </summary>
        private static void LogFatalException(Exception ex)
        {
            try
            {
                string appDataPath = Models.AppSettings.GetAppDataPath();
                string logPath = Path.Combine(appDataPath, "crash.log");
                string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}";
                File.AppendAllText(logPath, entry);
            }
            catch
            {
                // Last-resort: write to Debug output.
                Debug.WriteLine($"FATAL: {ex}");
            }
        }
    }
}
