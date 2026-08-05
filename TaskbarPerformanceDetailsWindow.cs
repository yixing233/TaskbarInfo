using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;

namespace TaskbarInfo;

public sealed class TaskbarPerformanceDetailsWindow : IDisposable
{
    private const double CardWidth = 128;
    private const double CardCornerRadius = 6;
    private const byte AcrylicTintOpacity = 96;
    private const int SpacingAboveTaskbar = 6;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly Border _card;
    private readonly StackPanel _rows = new() { Margin = new Thickness(14, 10, 14, 10) };
    private readonly DispatcherTimer _outsideClickTimer;
    private readonly Window _window;
    private bool _disposed;
    private bool _wasPrimaryButtonDown;

    public bool IsOpen => _window.IsVisible;

    public TaskbarPerformanceDetailsWindow()
    {
        _card = new Border
        {
            Background = MediaBrushes.Transparent,
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(166, 200, 208, 219)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(CardCornerRadius),
            Width = CardWidth,
            Child = _rows
        };

        _window = new Window
        {
            AllowsTransparency = false,
            Background = MediaBrushes.Transparent,
            Content = _card,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.Height,
            Topmost = true,
            Width = CardWidth,
            WindowStartupLocation = WindowStartupLocation.Manual,
            WindowStyle = WindowStyle.None
        };
        WindowChrome.SetWindowChrome(_window, new WindowChrome
        {
            CaptionHeight = 0,
            CornerRadius = new CornerRadius(CardCornerRadius),
            GlassFrameThickness = new Thickness(-1),
            ResizeBorderThickness = new Thickness(0)
        });
        _window.SourceInitialized += (_, _) => ApplyAcrylicBackdrop();
        _window.ContentRendered += (_, _) => ApplyAcrylicBackdrop();
        _outsideClickTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _outsideClickTimer.Tick += OutsideClickTimer_Tick;
    }

    public void Update(TaskbarPerformanceSnapshot snapshot, IEnumerable<string> metrics)
    {
        _rows.Children.Clear();
        foreach (string metric in TaskbarPerformanceMetricCatalog.Normalize(metrics))
        {
            TaskbarPerformanceMetricDisplay? display = TaskbarPerformanceFormatter.FormatMetric(snapshot, metric);
            if (display == null) continue;

            var row = new Grid
            {
                Margin = new Thickness(0, 3, 0, 3)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new TextBlock
            {
                Text = display.Label,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(27, 37, 48)),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });

            var value = new TextBlock
            {
                Text = display.Value,
                Foreground = new SolidColorBrush(MediaColor.FromRgb(27, 37, 48)),
                FontSize = 12,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Right,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            _rows.Children.Add(row);
        }

        if (IsOpen)
        {
            _window.UpdateLayout();
        }
    }

    public void ShowAbove(Window anchor)
    {
        bool isFirstShow = !IsOpen;
        if (isFirstShow)
        {
            PrepareInitialPlacement(anchor);
            _window.Opacity = 0;
            _window.Show();
        }

        _window.UpdateLayout();
        PositionAbove(anchor);
        if (isFirstShow)
        {
            _window.Opacity = 1;
        }
        StartOutsideClickWatcher();
    }

    public void Hide()
    {
        _outsideClickTimer.Stop();
        _wasPrimaryButtonDown = false;
        if (IsOpen) _window.Hide();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _outsideClickTimer.Stop();
        _window.Close();
    }

    private void PositionAbove(Window anchor)
    {
        IntPtr handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero) return;

        DpiScale anchorDpi = VisualTreeHelper.GetDpi(anchor);
        System.Windows.Point anchorOrigin = anchor.PointToScreen(new System.Windows.Point(0, 0));
        double windowDpiScale = Math.Max(UnmanagedMethods.GetDpiForWindow(handle), 96) / 96d;
        double anchorWidth = anchor.ActualWidth * anchorDpi.DpiScaleX;
        double cardWidth = _window.ActualWidth * windowDpiScale;
        double cardHeight = _window.ActualHeight * windowDpiScale;
        int left = (int)Math.Round(anchorOrigin.X + (anchorWidth - cardWidth) / 2);
        int top = (int)Math.Round(anchorOrigin.Y - cardHeight - SpacingAboveTaskbar * windowDpiScale);

        UnmanagedMethods.SetWindowPos(
            handle,
            HwndTopmost,
            left,
            top,
            0,
            0,
            UnmanagedMethods.SWP_NOSIZE |
            UnmanagedMethods.SWP_NOACTIVATE);
    }

    private void PrepareInitialPlacement(Window anchor)
    {
        DpiScale anchorDpi = VisualTreeHelper.GetDpi(anchor);
        System.Windows.Point anchorOrigin = anchor.PointToScreen(new System.Windows.Point(0, 0));
        _card.Measure(new System.Windows.Size(CardWidth, double.PositiveInfinity));

        int anchorWidth = Math.Max(1, (int)Math.Round(anchor.ActualWidth * anchorDpi.DpiScaleX));
        int cardWidth = Math.Max(1, (int)Math.Round(CardWidth * anchorDpi.DpiScaleX));
        int cardHeight = Math.Max(1, (int)Math.Round(_card.DesiredSize.Height * anchorDpi.DpiScaleY));
        int spacing = Math.Max(1, (int)Math.Round(SpacingAboveTaskbar * anchorDpi.DpiScaleY));
        TaskbarPerformanceDetailsPlacement placement = TaskbarPerformanceDetailsLayout.GetPlacement(
            (int)Math.Round(anchorOrigin.X),
            (int)Math.Round(anchorOrigin.Y),
            anchorWidth,
            cardWidth,
            cardHeight,
            spacing);

        _window.Left = placement.Left / anchorDpi.DpiScaleX;
        _window.Top = placement.Top / anchorDpi.DpiScaleY;
    }

    private void ApplyAcrylicBackdrop()
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(_window).Handle;
            if (handle == IntPtr.Zero) return;

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                int cornerPreference = (int)UnmanagedMethods.DwmWindowCornerPreference.DWMWCP_ROUND;
                UnmanagedMethods.DwmSetWindowAttribute(
                    handle,
                    UnmanagedMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                    ref cornerPreference,
                    sizeof(int));

                int backdropType = (int)UnmanagedMethods.DwmSystemBackdropType.DWMSBT_NONE;
                UnmanagedMethods.DwmSetWindowAttribute(
                    handle,
                    UnmanagedMethods.DWMWA_SYSTEMBACKDROP_TYPE,
                    ref backdropType,
                    sizeof(int));
            }

            var accent = new UnmanagedMethods.AccentPolicy
            {
                AccentState = UnmanagedMethods.AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 0,
                GradientColor = ToAccentColor(MediaColor.FromArgb(AcrylicTintOpacity, 245, 247, 250)),
                AnimationId = 0
            };
            int size = Marshal.SizeOf<UnmanagedMethods.AccentPolicy>();
            IntPtr data = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, data, false);
                var attribute = new UnmanagedMethods.WindowCompositionAttributeData
                {
                    Attribute = UnmanagedMethods.WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    Data = data,
                    SizeOfData = size
                };
                UnmanagedMethods.SetWindowCompositionAttribute(handle, ref attribute);
            }
            finally
            {
                Marshal.FreeHGlobal(data);
            }
        }
        catch
        {
            // Fall back to the transparent window if the compositor does not support acrylic.
        }
    }

    private static int ToAccentColor(MediaColor color) =>
        unchecked((int)((uint)color.A << 24 | (uint)color.B << 16 | (uint)color.G << 8 | color.R));

    private void StartOutsideClickWatcher()
    {
        _wasPrimaryButtonDown = IsPrimaryButtonDown();
        _outsideClickTimer.Start();
    }

    private void OutsideClickTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsOpen)
        {
            _outsideClickTimer.Stop();
            return;
        }

        bool isPrimaryButtonDown = IsPrimaryButtonDown();
        if (isPrimaryButtonDown && !_wasPrimaryButtonDown &&
            UnmanagedMethods.GetCursorPos(out UnmanagedMethods.POINT cursor) &&
            !IsCursorInsideCard(cursor))
        {
            Hide();
            return;
        }

        _wasPrimaryButtonDown = isPrimaryButtonDown;
    }

    private bool IsCursorInsideCard(UnmanagedMethods.POINT cursor)
    {
        IntPtr handle = new WindowInteropHelper(_window).Handle;
        return handle != IntPtr.Zero &&
               UnmanagedMethods.GetWindowRect(handle, out UnmanagedMethods.RECT bounds) &&
               TaskbarPerformanceDetailsLayout.ContainsScreenPoint(bounds, cursor);
    }

    private static bool IsPrimaryButtonDown() =>
        (UnmanagedMethods.GetAsyncKeyState(UnmanagedMethods.VK_LBUTTON) & 0x8000) != 0;
}

public static class TaskbarPerformanceDetailsLayout
{
    public static TaskbarPerformanceDetailsPlacement GetPlacement(
        int anchorLeft,
        int anchorTop,
        int anchorWidth,
        int cardWidth,
        int cardHeight,
        int spacing) => new(
        anchorLeft + (anchorWidth - cardWidth) / 2,
        anchorTop - Math.Max(1, cardHeight) - Math.Max(0, spacing));

    public static bool ContainsScreenPoint(UnmanagedMethods.RECT bounds, UnmanagedMethods.POINT point) =>
        point.X >= bounds.Left && point.X < bounds.Right &&
        point.Y >= bounds.Top && point.Y < bounds.Bottom;
}

public readonly record struct TaskbarPerformanceDetailsPlacement(int Left, int Top);
