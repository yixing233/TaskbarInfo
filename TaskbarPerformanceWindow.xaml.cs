using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace TaskbarInfo;

public partial class TaskbarPerformanceWindow : Window, IDisposable
{
    private TaskbarPerformanceCollector? _collector;
    private AppSettings _settings = new();
    private IntPtr _taskbarWindow;
    private int _lyricLeft;
    private int _taskbarWidth;
    private int _trayLeft;
    private double _pixelsPerDip = 1;
    private bool _isDoubleLine;
    private bool _isDragPending;
    private bool _isDragging;
    private System.Windows.Point _dragStartMouseScreenPos;
    private int _dragStartOffset;
    private bool _disposed;
    private TaskbarPerformanceDetailsWindow? _detailsWindow;
    private TaskbarPerformanceSnapshot _latestSnapshot = TaskbarPerformanceSnapshot.Empty;

    public event EventHandler? SettingsRequested;

    public TaskbarPerformanceWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyClickThroughStyle();
    }

    public void ApplySettings(AppSettings settings, int lyricLeft)
    {
        if (_disposed) return;

        _settings = settings;
        _lyricLeft = lyricLeft;

        var selectedMetrics = TaskbarPerformanceMetricCatalog.GetSummarySelection(
            settings.TaskbarPerformanceMetrics,
            settings.TaskbarPerformanceSummaryMetricCount);
        bool enabled = settings.EnableTaskbarPerformanceMonitor && selectedMetrics.Count > 0;
        if (!enabled)
        {
            _collector?.Stop();
            PerformanceText.Text = string.Empty;
            PerformanceTextSecond.Text = string.Empty;
            PerformanceTextSecond.Visibility = Visibility.Collapsed;
            if (IsVisible) Hide();
            return;
        }

        _isDoubleLine = settings.TaskbarPerformanceIsDoubleLine;
        UpdateDpiScale();
        Width = GetRenderedWidth(selectedMetrics);
        PerformanceText.FontFamily = new System.Windows.Media.FontFamily(settings.TaskbarPerformanceFontFamily);
        PerformanceTextSecond.FontFamily = PerformanceText.FontFamily;
        PerformanceText.FontSize = settings.TaskbarPerformanceFontSize;
        PerformanceTextSecond.FontSize = settings.TaskbarPerformanceFontSize;
        FontWeight performanceWeight = GetConfiguredFontWeight(settings.TaskbarPerformanceFontWeight);
        PerformanceText.FontWeight = performanceWeight;
        PerformanceTextSecond.FontWeight = performanceWeight;
        PerformanceGrid.RowDefinitions[0].Height = settings.TaskbarPerformanceIsDoubleLine
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
        PerformanceGrid.RowDefinitions[1].Height = settings.TaskbarPerformanceIsDoubleLine
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        Height = settings.TaskbarPerformanceIsDoubleLine ? 60 : 30;
        ApplyPalette(settings);
        _detailsWindow?.ApplyTheme(ApplicationThemeParser.Resolve(settings.ApplicationTheme));

        if (!IsVisible)
        {
            Show();
        }

        EnsureHosted();
        Reposition();
        _collector ??= CreateCollector();
        _collector.SetEnhancedTemperatureSensorsEnabled(settings.EnableEnhancedTemperatureSensors);
        _collector.Start(settings.TaskbarPerformanceRefreshSeconds);
    }

    public void Reposition()
    {
        if (_disposed || !IsVisible) return;

        EnsureHosted();
        if (_taskbarWindow == IntPtr.Zero) return;

        UpdateDpiScale();

        if (!UnmanagedMethods.GetWindowRect(_taskbarWindow, out UnmanagedMethods.RECT taskbarRect)) return;
        _pixelsPerDip = GetTaskbarDpiScale(_taskbarWindow, _pixelsPerDip);
        int taskbarWidth = Math.Max(0, taskbarRect.Right - taskbarRect.Left);
        int taskbarHeight = Math.Max(1, taskbarRect.Bottom - taskbarRect.Top);
        IntPtr trayWindow = UnmanagedMethods.FindWindowEx(_taskbarWindow, IntPtr.Zero, "TrayNotifyWnd", null);
        int trayLeft = taskbarWidth;
        if (trayWindow != IntPtr.Zero && UnmanagedMethods.GetWindowRect(trayWindow, out UnmanagedMethods.RECT trayRect))
        {
            trayLeft = Math.Clamp(trayRect.Left - taskbarRect.Left, 0, taskbarWidth);
        }

        _taskbarWidth = taskbarWidth;
        _trayLeft = trayLeft;
        int offsetX = GetEffectiveOffset(taskbarWidth, trayLeft);
        var position = TaskbarPerformanceLayout.GetPosition(
            taskbarWidth,
            taskbarHeight,
            trayLeft,
            offsetX,
            TaskbarPerformanceMetricCatalog.GetSummarySelection(
                _settings.TaskbarPerformanceMetrics,
                _settings.TaskbarPerformanceSummaryMetricCount),
            _settings.TaskbarPerformanceIsDoubleLine,
            _settings.TaskbarPerformanceFontFamily,
            _settings.TaskbarPerformanceFontSize,
            _settings.TaskbarPerformanceFontWeight,
            _pixelsPerDip);

        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            UnmanagedMethods.MoveWindow(handle, position.Left, position.Top, position.Width, position.Height, true);
        }
    }

    public void UpdateLyricsPosition(int lyricLeft)
    {
        _lyricLeft = lyricLeft;
        PinInitialPosition();
        Reposition();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _collector?.Dispose();
        _detailsWindow?.Dispose();
        _collector = null;
        if (IsVisible) Hide();
        Close();
    }

    private TaskbarPerformanceCollector CreateCollector()
    {
        var collector = new TaskbarPerformanceCollector();
        collector.SnapshotUpdated += Collector_SnapshotUpdated;
        return collector;
    }

    private void Collector_SnapshotUpdated(object? sender, TaskbarPerformanceSnapshot snapshot)
    {
        if (_disposed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (_disposed || !IsVisible) return;
            _latestSnapshot = snapshot;
            if (_detailsWindow?.IsOpen == true)
            {
                _detailsWindow.Update(snapshot, _settings.TaskbarPerformanceMetrics);
            }
            var lines = TaskbarPerformanceFormatter.FormatLines(
                snapshot,
                TaskbarPerformanceMetricCatalog.GetSummarySelection(
                    _settings.TaskbarPerformanceMetrics,
                    _settings.TaskbarPerformanceSummaryMetricCount),
                _settings.TaskbarPerformanceIsDoubleLine);
            PerformanceText.Text = lines.First;
            PerformanceTextSecond.Text = lines.Second;
            PerformanceTextSecond.Visibility = string.IsNullOrWhiteSpace(lines.Second)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }));
    }

    private void EnsureHosted()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        IntPtr taskbar = TaskbarMonitorLocator.FindTaskbarWindow(_settings.TaskbarPerformanceMonitorDeviceName);
        if (taskbar == IntPtr.Zero) return;

        if (_taskbarWindow != taskbar || UnmanagedMethods.GetParent(handle) != taskbar)
        {
            int style = UnmanagedMethods.GetWindowLong(handle, UnmanagedMethods.GWL_STYLE);
            style = (style & ~UnmanagedMethods.WS_POPUP) |
                    UnmanagedMethods.WS_CHILD |
                    UnmanagedMethods.WS_VISIBLE |
                    UnmanagedMethods.WS_CLIPSIBLINGS;
            UnmanagedMethods.SetWindowLong(handle, UnmanagedMethods.GWL_STYLE, style);

            UnmanagedMethods.SetParent(handle, taskbar);
            _taskbarWindow = taskbar;
        }

        ApplyClickThroughStyle();
    }

    private void ApplyClickThroughStyle()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        int exStyle = UnmanagedMethods.GetWindowLong(handle, UnmanagedMethods.GWL_EXSTYLE);
        exStyle |= UnmanagedMethods.WS_EX_TOOLWINDOW |
                   UnmanagedMethods.WS_EX_NOACTIVATE;
        exStyle &= ~(UnmanagedMethods.WS_EX_APPWINDOW | UnmanagedMethods.WS_EX_TRANSPARENT);
        UnmanagedMethods.SetWindowLong(handle, UnmanagedMethods.GWL_EXSTYLE, exStyle);
        UnmanagedMethods.SetWindowPos(
            handle,
            UnmanagedMethods.HWND_TOP,
            0,
            0,
            0,
            0,
            UnmanagedMethods.SWP_NOMOVE |
            UnmanagedMethods.SWP_NOSIZE |
            UnmanagedMethods.SWP_NOZORDER |
            UnmanagedMethods.SWP_NOACTIVATE |
            UnmanagedMethods.SWP_FRAMECHANGED);
    }

    private int GetEffectiveOffset(int taskbarWidth, int trayLeft)
    {
        if (_settings.TaskbarPerformanceOffsetX is int savedOffset)
        {
            return Math.Max(0, savedOffset);
        }

        int width = GetRenderedWidth(TaskbarPerformanceMetricCatalog.GetSummarySelection(
            _settings.TaskbarPerformanceMetrics,
            _settings.TaskbarPerformanceSummaryMetricCount));
        int defaultLeft = TaskbarPerformanceLayout.GetLeftBesideLyrics(
            taskbarWidth,
            _lyricLeft,
            TaskbarPerformanceMetricCatalog.GetSummarySelection(_settings.TaskbarPerformanceMetrics, _settings.TaskbarPerformanceSummaryMetricCount),
            _settings.TaskbarPerformanceIsDoubleLine,
            _settings.TaskbarPerformanceFontFamily,
            _settings.TaskbarPerformanceFontSize,
            _settings.TaskbarPerformanceFontWeight,
            _pixelsPerDip);
        return TaskbarPerformanceLayout.GetOffsetForLeft(trayLeft, width, defaultLeft);
    }

    private void PinInitialPosition()
    {
        if (_settings.TaskbarPerformanceOffsetX.HasValue) return;

        EnsureHosted();
        if (_taskbarWindow == IntPtr.Zero ||
            !UnmanagedMethods.GetWindowRect(_taskbarWindow, out UnmanagedMethods.RECT taskbarRect))
        {
            return;
        }

        int taskbarWidth = Math.Max(0, taskbarRect.Right - taskbarRect.Left);
        IntPtr trayWindow = UnmanagedMethods.FindWindowEx(_taskbarWindow, IntPtr.Zero, "TrayNotifyWnd", null);
        int trayLeft = taskbarWidth;
        if (trayWindow != IntPtr.Zero && UnmanagedMethods.GetWindowRect(trayWindow, out UnmanagedMethods.RECT trayRect))
        {
            trayLeft = Math.Clamp(trayRect.Left - taskbarRect.Left, 0, taskbarWidth);
        }

        int width = GetRenderedWidth(TaskbarPerformanceMetricCatalog.GetSummarySelection(
            _settings.TaskbarPerformanceMetrics,
            _settings.TaskbarPerformanceSummaryMetricCount));
        int defaultLeft = TaskbarPerformanceLayout.GetLeftBesideLyrics(
            taskbarWidth,
            _lyricLeft,
            TaskbarPerformanceMetricCatalog.GetSummarySelection(_settings.TaskbarPerformanceMetrics, _settings.TaskbarPerformanceSummaryMetricCount),
            _settings.TaskbarPerformanceIsDoubleLine,
            _settings.TaskbarPerformanceFontFamily,
            _settings.TaskbarPerformanceFontSize,
            _settings.TaskbarPerformanceFontWeight,
            _pixelsPerDip);
        _settings.TaskbarPerformanceOffsetX = TaskbarPerformanceLayout.GetOffsetForLeft(trayLeft, width, defaultLeft);
        _settings.Save();
    }

    private void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragPending = true;
        _isDragging = false;
        DragHandle.CaptureMouse();
        _dragStartMouseScreenPos = PointToScreen(e.GetPosition(this));
        _dragStartOffset = GetEffectiveOffset(_taskbarWidth, _trayLeft);
        e.Handled = true;
    }

    private void DragHandle_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragPending) return;

        System.Windows.Point currentMouseScreenPos = PointToScreen(e.GetPosition(this));
        double horizontalDistance = Math.Abs(currentMouseScreenPos.X - _dragStartMouseScreenPos.X);
        if (!_isDragging && horizontalDistance < SystemParameters.MinimumHorizontalDragDistance)
        {
            return;
        }

        _isDragging = true;
        int nextOffset = Math.Max(0, _dragStartOffset - (int)Math.Round(currentMouseScreenPos.X - _dragStartMouseScreenPos.X));
        if (_settings.TaskbarPerformanceOffsetX != nextOffset)
        {
            _settings.TaskbarPerformanceOffsetX = nextOffset;
            Reposition();
        }

        e.Handled = true;
    }

    private void DragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragPending) return;

        _isDragPending = false;
        if (DragHandle.IsMouseCaptured) DragHandle.ReleaseMouseCapture();
        bool wasDragging = _isDragging;
        _isDragging = false;
        if (wasDragging) _settings.Save();
        e.Handled = true;
    }

    private void PerformanceBackground_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) ToggleDetails();
    }

    private void ToggleDetails()
    {
        if (_detailsWindow?.IsOpen == true) { _detailsWindow.Hide(); return; }
        _detailsWindow ??= new TaskbarPerformanceDetailsWindow();
        _detailsWindow.ApplyTheme(ApplicationThemeParser.Resolve(_settings.ApplicationTheme));
        _detailsWindow.Update(_latestSnapshot, _settings.TaskbarPerformanceMetrics);
        _detailsWindow.ShowAbove(this);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void ApplyPalette(AppSettings settings)
    {
        try
        {
            var background = (MediaColor)MediaColorConverter.ConvertFromString(settings.BackgroundColor);
            if (background.A == 0) background.A = 1;
            PerformanceBackground.Background = new SolidColorBrush(background);

            var foreground = (MediaColor)MediaColorConverter.ConvertFromString(settings.TextColor);
            PerformanceText.Foreground = new SolidColorBrush(foreground);
        }
        catch
        {
            PerformanceBackground.Background = new SolidColorBrush(MediaColor.FromArgb(0x33, 0, 0, 0));
            PerformanceText.Foreground = new SolidColorBrush(MediaColor.FromArgb(0xD8, 0xFF, 0xFF, 0xFF));
        }
    }

    private int GetRenderedWidth(IEnumerable<string> metrics)
    {
        return TaskbarPerformanceLayout.GetWidth(
            metrics,
            _isDoubleLine,
            _settings.TaskbarPerformanceFontFamily,
            _settings.TaskbarPerformanceFontSize,
            _settings.TaskbarPerformanceFontWeight,
            _pixelsPerDip);
    }

    private void UpdateDpiScale()
    {
        try
        {
            var source = PresentationSource.FromVisual(this);
            _pixelsPerDip = source?.CompositionTarget?.TransformToDevice.M11 is double scale && scale > 0
                ? scale
                : 1;
        }
        catch
        {
            _pixelsPerDip = 1;
        }
    }

    private static double GetTaskbarDpiScale(IntPtr taskbarWindow, double fallback)
    {
        try
        {
            uint dpi = UnmanagedMethods.GetDpiForWindow(taskbarWindow);
            return dpi >= 96 ? dpi / 96d : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static FontWeight GetConfiguredFontWeight(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return FontWeights.SemiBold;
        try
        {
            var converter = new FontWeightConverter();
            object? converted = converter.ConvertFromString(value.Split(' ')[0]);
            return converted is FontWeight weight ? weight : FontWeights.SemiBold;
        }
        catch
        {
            return FontWeights.SemiBold;
        }
    }
}
