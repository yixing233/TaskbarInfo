using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TaskbarInfo;

public partial class TaskbarTranslateButtonWindow : Window, IDisposable
{
    public const int WidthInPixels = 32;
    private const int DragThresholdInPixels = 3;

    private AppSettings _settings = new();
    private IntPtr _taskbarWindow;
    private int _taskbarWidth;
    private int _trayLeft;
    private bool _pointerDown;
    private bool _isDragging;
    private System.Windows.Point _dragStartMouseScreenPosition;
    private int _dragStartOffset;
    private bool _disposed;

    public event EventHandler? TranslateRequested;
    public event EventHandler? SettingsRequested;

    public TaskbarTranslateButtonWindow()
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

    public bool TryGetScreenBounds(out System.Drawing.Rectangle bounds)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero &&
            UnmanagedMethods.GetWindowRect(handle, out UnmanagedMethods.RECT rect))
        {
            bounds = System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return bounds.Width > 0 && bounds.Height > 0;
        }

        bounds = default;
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }

    private void Translate_Click(object sender, RoutedEventArgs e)
    {
        TranslateRequested?.Invoke(this, EventArgs.Empty);
    }

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
        int left = TaskbarTranslateButtonLayout.GetLeftFromTray(
            taskbarWidth,
            trayLeft,
            _settings.TaskbarTranslateButtonOffsetX);
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            UnmanagedMethods.MoveWindow(handle, left, 0, WidthInPixels, taskbarHeight, true);
        }
    }

    private void TranslateButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pointerDown = true;
        _isDragging = false;
        _dragStartMouseScreenPosition = PointToScreen(e.GetPosition(this));
        _dragStartOffset = Math.Max(
            0,
            _settings.TaskbarTranslateButtonOffsetX ?? TaskbarTranslateButtonLayout.DefaultOffsetFromTray);
    }

    private void TranslateButton_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_pointerDown || e.LeftButton != MouseButtonState.Pressed || _taskbarWidth <= 0) return;

        System.Windows.Point currentMouseScreenPosition = PointToScreen(e.GetPosition(this));
        double horizontalDelta = currentMouseScreenPosition.X - _dragStartMouseScreenPosition.X;
        if (!_isDragging && Math.Abs(horizontalDelta) < DragThresholdInPixels) return;

        if (!_isDragging)
        {
            _isDragging = true;
            TranslateButton.CaptureMouse();
        }

        int candidateOffset = Math.Max(0, _dragStartOffset - (int)Math.Round(horizontalDelta));
        int left = TaskbarTranslateButtonLayout.GetLeftFromTray(_taskbarWidth, _trayLeft, candidateOffset);
        int nextOffset = TaskbarTranslateButtonLayout.GetOffsetForLeft(_trayLeft, left);
        if (_settings.TaskbarTranslateButtonOffsetX != nextOffset)
        {
            _settings.TaskbarTranslateButtonOffsetX = nextOffset;
            Reposition();
        }
        e.Handled = true;
    }

    private void TranslateButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pointerDown) return;

        _pointerDown = false;
        if (!_isDragging) return;

        if (TranslateButton.IsMouseCaptured) TranslateButton.ReleaseMouseCapture();
        _isDragging = false;
        _settings.Save();
        e.Handled = true;
    }

    private void EnsureHosted()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        IntPtr taskbar = TaskbarMonitorLocator.FindTaskbarWindow(_settings.TaskbarMonitorDeviceName);
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
