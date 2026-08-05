using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.Win32;

namespace TaskbarInfo;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private UserPreferenceChangedEventHandler? _userPreferenceChangedHandler;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (TemperatureSensorHelper.TryRun(e.Args))
        {
            Shutdown();
            return;
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

        base.OnExit(e);
    }

    public static System.Windows.Media.ImageSource? GetAppIcon()
    {
        try
        {
            // Load Desktop Icon from embedded resources
            var uri = new Uri("pack://application:,,,/src/icons/桌面图标.png");
            return System.Windows.Media.Imaging.BitmapFrame.Create(uri);
        }
        catch {}
        return null;
    }
}

