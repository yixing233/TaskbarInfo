using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using Windows.UI.ViewManagement;
using TaskbarInfo;

namespace TinyBar.Settings;

public partial class App : Application
{
    private Window? _window;
    private DispatcherQueueTimer? _parentProcessMonitor;
    private DispatcherQueue? _dispatcherQueue;
    private UISettings? _uiSettings;

    private static void LogException(Exception? ex, string source)
    {
        if (ex == null) return;
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TinyBar");
            Directory.CreateDirectory(dir);
            string logFile = Path.Combine(dir, "settings_crash.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\r\n\r\n");
        }
        catch { }
    }

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogException(args.ExceptionObject as Exception, "AppDomain");
        };
        UnhandledException += (_, args) =>
        {
            LogException(args.Exception, "UnhandledException");
        };
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] commandLine = Environment.GetCommandLineArgs().Skip(1).ToArray();
        bool keepAlive = commandLine.Contains("--keep-alive", StringComparer.OrdinalIgnoreCase);
        bool hidden = commandLine.Contains("--hidden", StringComparer.OrdinalIgnoreCase);
        _window = new MainWindow(keepAlive, ResolveNamedArgument(commandLine, "--update-event="));
        _window.Activate();

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _uiSettings = new UISettings();
        _uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;
        _window.Closed += Window_Closed;

        if (_window is MainWindow settingsWindow && keepAlive)
        {
            if (hidden)
            {
                settingsWindow.HideForReuse();
            }
            StartParentProcessMonitor(ResolveParentProcessId(commandLine));
        }
    }

    private void UiSettings_ColorValuesChanged(UISettings sender, object args)
    {
        if (_window is not MainWindow settingsWindow || !settingsWindow.UsesSystemApplicationTheme) return;
        _dispatcherQueue?.TryEnqueue(settingsWindow.RefreshSystemTheme);
    }

    private void Window_Closed(object sender, WindowEventArgs args)
    {
        if (_uiSettings != null)
        {
            _uiSettings.ColorValuesChanged -= UiSettings_ColorValuesChanged;
            _uiSettings = null;
        }

        _dispatcherQueue = null;
    }

    private void StartParentProcessMonitor(int parentProcessId)
    {
        if (parentProcessId <= 0) return;

        DispatcherQueue? dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        if (dispatcherQueue == null) return;

        _parentProcessMonitor = dispatcherQueue.CreateTimer();
        _parentProcessMonitor.Interval = TimeSpan.FromSeconds(2);
        _parentProcessMonitor.Tick += (_, _) =>
        {
            try
            {
                using var parent = System.Diagnostics.Process.GetProcessById(parentProcessId);
                if (!parent.HasExited) return;
            }
            catch (ArgumentException)
            {
            }

            _parentProcessMonitor?.Stop();
            Environment.Exit(0);
        };
        _parentProcessMonitor.Start();
    }

    private static int ResolveParentProcessId(IEnumerable<string> arguments)
    {
        string? value = arguments.FirstOrDefault(argument => argument.StartsWith("--parent-pid=", StringComparison.OrdinalIgnoreCase));
        return value != null && int.TryParse(value[13..], out int processId) ? processId : 0;
    }

    private static string? ResolveNamedArgument(IEnumerable<string> arguments, string prefix) => arguments
        .FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
}
