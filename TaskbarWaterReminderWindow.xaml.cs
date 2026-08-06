using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MediaColor = System.Windows.Media.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace TaskbarInfo;

public partial class TaskbarWaterReminderWindow : Window, IDisposable
{
    public const int WidthInPixels = 116;
    private const int DragThresholdInPixels = 3;

    private AppSettings _settings = new();
    private IntPtr _taskbarWindow;
    private int _taskbarWidth;
    private int _trayLeft;
    private bool _pointerDown;
    private bool _isDragging;
    private Point _dragStartMouseScreenPosition;
    private int _dragStartOffset;
    private WaterReminderStatus? _lastStatus;
    private DispatcherTimer? _feedbackTimer;
    private bool _disposed;

    public event EventHandler? DrinkRequested;
    public event EventHandler? SnoozeRequested;
    public event EventHandler? SettingsRequested;

    public TaskbarWaterReminderWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyTaskbarWindowStyle();
    }

    public void ApplySettings(AppSettings settings)
    {
        if (_disposed) return;

        _settings = settings;
        if (!IsVisible) Show();
        EnsureHosted();
        Reposition();
    }

    public void Update(WaterReminderStatus status)
    {
        if (_disposed) return;

        _lastStatus = status;
        if (_feedbackTimer?.IsEnabled == true) return;

        ApplyStatus(status);
    }

    public void ShowDrinkRecordedFeedback()
    {
        if (_disposed) return;

        ReminderText.Text = "已记录";
        ReminderButton.ToolTip = "饮水记录已保存";
        ReminderSurface.Background = new SolidColorBrush(MediaColor.FromArgb(125, 48, 150, 92));
        if (_feedbackTimer == null)
        {
            _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _feedbackTimer.Tick += (_, _) =>
            {
                _feedbackTimer.Stop();
                if (_lastStatus != null) ApplyStatus(_lastStatus);
            };
        }
        _feedbackTimer.Stop();
        _feedbackTimer.Start();
    }

    private void ApplyStatus(WaterReminderStatus status)
    {
        string text = status.IsGoalReached
            ? "今日达标"
            : status.IsQuietHours
                ? "静默中"
                : status.IsDue
                    ? "该喝水了"
                    : $"{Math.Max(1, (int)Math.Ceiling(status.Remaining.TotalMinutes))} 分钟";
        ReminderText.Text = text;
        ReminderButton.ToolTip = $"今日 {status.CompletedToday}/{status.DailyGoal} 次";
        ReminderSurface.Background = status.IsDue
            ? new SolidColorBrush(MediaColor.FromArgb(115, 57, 130, 207))
            : new SolidColorBrush(MediaColor.FromArgb(51, 0, 0, 0));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _feedbackTimer?.Stop();
        Close();
    }

    private void Drink_Click(object sender, RoutedEventArgs e) =>
        DrinkRequested?.Invoke(this, EventArgs.Empty);

    private void Snooze_Click(object sender, RoutedEventArgs e) =>
        SnoozeRequested?.Invoke(this, EventArgs.Empty);

    private void OpenSettings_Click(object sender, RoutedEventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void Reposition()
    {
        if (_disposed || !IsVisible) return;

        EnsureHosted();
        if (_taskbarWindow == IntPtr.Zero ||
            !UnmanagedMethods.GetWindowRect(_taskbarWindow, out UnmanagedMethods.RECT taskbarRect))
        {
            return;
        }

        int taskbarWidth = Math.Max(0, taskbarRect.Right - taskbarRect.Left);
        int taskbarHeight = Math.Max(1, taskbarRect.Bottom - taskbarRect.Top);
        IntPtr trayWindow = UnmanagedMethods.FindWindowEx(_taskbarWindow, IntPtr.Zero, "TrayNotifyWnd", null);
        int trayLeft = taskbarWidth;
        if (trayWindow != IntPtr.Zero &&
            UnmanagedMethods.GetWindowRect(trayWindow, out UnmanagedMethods.RECT trayRect))
        {
            trayLeft = Math.Clamp(trayRect.Left - taskbarRect.Left, 0, taskbarWidth);
        }

        _taskbarWidth = taskbarWidth;
        _trayLeft = trayLeft;
        int left = TaskbarWaterReminderLayout.GetLeftFromTray(
            taskbarWidth,
            trayLeft,
            _settings.TaskbarWaterReminderOffsetX);
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            UnmanagedMethods.MoveWindow(handle, left, 0, WidthInPixels, taskbarHeight, true);
        }
    }

    private void ReminderButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pointerDown = true;
        _isDragging = false;
        _dragStartMouseScreenPosition = PointToScreen(e.GetPosition(this));
        _dragStartOffset = Math.Max(
            0,
            _settings.TaskbarWaterReminderOffsetX ?? TaskbarWaterReminderLayout.DefaultOffsetFromTray);
    }

    private void ReminderButton_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pointerDown || e.LeftButton != MouseButtonState.Pressed || _taskbarWidth <= 0) return;

        Point currentMouseScreenPosition = PointToScreen(e.GetPosition(this));
        double horizontalDelta = currentMouseScreenPosition.X - _dragStartMouseScreenPosition.X;
        if (!_isDragging && Math.Abs(horizontalDelta) < DragThresholdInPixels) return;

        if (!_isDragging)
        {
            _isDragging = true;
            ReminderButton.CaptureMouse();
        }

        int candidateOffset = Math.Max(0, _dragStartOffset - (int)Math.Round(horizontalDelta));
        int left = TaskbarWaterReminderLayout.GetLeftFromTray(_taskbarWidth, _trayLeft, candidateOffset);
        int nextOffset = TaskbarWaterReminderLayout.GetOffsetForLeft(_trayLeft, left);
        if (_settings.TaskbarWaterReminderOffsetX != nextOffset)
        {
            _settings.TaskbarWaterReminderOffsetX = nextOffset;
            Reposition();
        }
        e.Handled = true;
    }

    private void ReminderButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pointerDown) return;

        _pointerDown = false;
        if (!_isDragging) return;

        if (ReminderButton.IsMouseCaptured) ReminderButton.ReleaseMouseCapture();
        _isDragging = false;
        _settings.Save();
        e.Handled = true;
    }

    private void EnsureHosted()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        IntPtr taskbar = TaskbarMonitorLocator.FindTaskbarWindow(_settings.TaskbarWaterReminderMonitorDeviceName);
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

        ApplyTaskbarWindowStyle();
    }

    private void ApplyTaskbarWindowStyle()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        int extendedStyle = UnmanagedMethods.GetWindowLong(handle, UnmanagedMethods.GWL_EXSTYLE);
        extendedStyle |= UnmanagedMethods.WS_EX_TOOLWINDOW | UnmanagedMethods.WS_EX_NOACTIVATE;
        extendedStyle &= ~(UnmanagedMethods.WS_EX_APPWINDOW | UnmanagedMethods.WS_EX_TRANSPARENT);
        UnmanagedMethods.SetWindowLong(handle, UnmanagedMethods.GWL_EXSTYLE, extendedStyle);
    }
}
