using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using TaskbarInfo;

namespace LyricsX.Settings;

public partial class App : Application
{
    private Window? _window;
    private DispatcherQueueTimer? _parentProcessMonitor;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] commandLine = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (QuickTranslateLaunchOptions.TryParse(commandLine, out QuickTranslateLaunchOptions quickTranslateOptions))
        {
            _window = new QuickTranslateWindow(
                quickTranslateOptions,
                ResolveSettingsPath(commandLine));
        }
        else
        {
            bool keepAlive = commandLine.Contains("--keep-alive", StringComparer.OrdinalIgnoreCase);
            _window = new MainWindow(keepAlive);
        }
        _window.Activate();

        if (_window is MainWindow settingsWindow && commandLine.Contains("--keep-alive", StringComparer.OrdinalIgnoreCase))
        {
            if (commandLine.Contains("--hidden", StringComparer.OrdinalIgnoreCase))
            {
                settingsWindow.HideForReuse();
            }
            StartParentProcessMonitor(ResolveParentProcessId(commandLine));
        }
    }

    private static string ResolveSettingsPath(IEnumerable<string> arguments)
    {
        string? argument = arguments.FirstOrDefault(value => !value.StartsWith("-", StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(argument)
            ? SettingsStorage.CurrentPath
            : Path.GetFullPath(argument);
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
}
