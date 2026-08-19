using System.Configuration;
using System.Data;
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace TaskbarInfo;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstanceMutex;
    private static bool _hasMutexOwnership;
    private UserPreferenceChangedEventHandler? _userPreferenceChangedHandler;

    private static void LogException(Exception? ex, string source)
    {
        if (ex == null) return;
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TinyBar");
            Directory.CreateDirectory(dir);
            string logFile = Path.Combine(dir, "crash.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\r\n\r\n");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogException(args.ExceptionObject as Exception, "AppDomain");
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogException(args.Exception, "Dispatcher");
        };

        if (TemperatureSensorHelper.TryRun(e.Args))
        {
            Shutdown();
            return;
        }

        try
        {
            string mutexName = $"Local\\TinyBar.SingleInstance.{Environment.UserName}";
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
            if (!createdNew)
            {
                try
                {
                    if (!_singleInstanceMutex.WaitOne(TimeSpan.Zero, false))
                    {
                        _singleInstanceMutex.Dispose();
                        _singleInstanceMutex = null;
                        System.Windows.MessageBox.Show("TinyBar 已在后台运行。\n如需调整设置，请右键点击任务栏右下角托盘图标。", "TinyBar", MessageBoxButton.OK, MessageBoxImage.Information);
                        Shutdown();
                        return;
                    }
                }
                catch (AbandonedMutexException)
                {
                    // Mutex was abandoned by a terminated process; we now own it.
                }
            }
            _hasMutexOwnership = true;
        }
        catch (Exception ex)
        {
            LogException(ex, "SingleInstanceMutex");
        }

        base.OnStartup(e);

        _userPreferenceChangedHandler = (_, _) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RefreshSystemTheme();
                }
            });
        };
        SystemEvents.UserPreferenceChanged += _userPreferenceChangedHandler;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_userPreferenceChangedHandler != null)
        {
            SystemEvents.UserPreferenceChanged -= _userPreferenceChangedHandler;
            _userPreferenceChangedHandler = null;
        }

        if (_singleInstanceMutex != null)
        {
            try
            {
                if (_hasMutexOwnership)
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
            }
            catch { }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }

    public static System.Windows.Media.ImageSource? GetAppIcon()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/src/icons/TinyBar.png");
            return System.Windows.Media.Imaging.BitmapFrame.Create(uri);
        }
        catch
        {
            try
            {
                var uri = new Uri("pack://application:,,,/src/icons/桌面图标.png");
                return System.Windows.Media.Imaging.BitmapFrame.Create(uri);
            }
            catch { }
        }
        return null;
    }
}

