using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using MediaColor = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace TaskbarInfo;

public partial class WaterReminderPopupWindow : Window, IDisposable
{
    private const byte AcrylicTintOpacity = 112;
    private const int SpacingAboveTaskbar = 6;
    private static readonly IntPtr HwndTopmost = new(-1);
    private ResolvedApplicationTheme _theme = ResolvedApplicationTheme.Light;
    private bool _disposed;

    public event EventHandler? DrinkRequested;
    public event EventHandler? SnoozeRequested;

    public WaterReminderPopupWindow()
    {
        InitializeComponent();
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            CornerRadius = new CornerRadius(8),
            GlassFrameThickness = new Thickness(-1),
            ResizeBorderThickness = new Thickness(0)
        });
        SourceInitialized += (_, _) => ApplyAcrylicBackdrop();
        ContentRendered += (_, _) => ApplyAcrylicBackdrop();
        Deactivated += (_, _) => Hide();
    }

    public void ApplyTheme(ResolvedApplicationTheme theme)
    {
        _theme = theme;
        ApplyAcrylicBackdrop();
    }

    public void ShowAbove(Window anchor, WaterReminderStatus status)
    {
        if (_disposed) return;

        SummaryText.Text = $"今日已完成 {status.CompletedToday}/{status.DailyGoal} 次";
        bool firstShow = !IsVisible;
        if (firstShow)
        {
            Opacity = 0;
            Show();
        }

        UpdateLayout();
        PositionAbove(anchor);
        if (firstShow) Opacity = 1;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }

    private void Drink_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        DrinkRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        SnoozeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PositionAbove(Window anchor)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        DpiScale anchorDpi = VisualTreeHelper.GetDpi(anchor);
        Point anchorOrigin = anchor.PointToScreen(new Point(0, 0));
        double scale = Math.Max(UnmanagedMethods.GetDpiForWindow(handle), 96) / 96d;
        double anchorWidth = anchor.ActualWidth * anchorDpi.DpiScaleX;
        double popupWidth = ActualWidth * scale;
        double popupHeight = ActualHeight * scale;
        int left = (int)Math.Round(anchorOrigin.X + (anchorWidth - popupWidth) / 2);
        int top = (int)Math.Round(anchorOrigin.Y - popupHeight - SpacingAboveTaskbar * scale);
        UnmanagedMethods.SetWindowPos(
            handle,
            HwndTopmost,
            left,
            top,
            0,
            0,
            UnmanagedMethods.SWP_NOSIZE | UnmanagedMethods.SWP_NOACTIVATE);
    }

    private void ApplyAcrylicBackdrop()
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
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
                GradientColor = ToAccentColor(_theme == ResolvedApplicationTheme.Dark
                    ? MediaColor.FromArgb(AcrylicTintOpacity, 24, 32, 42)
                    : MediaColor.FromArgb(AcrylicTintOpacity, 245, 247, 250)),
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
            // The dynamic theme brushes keep the reminder usable when acrylic is unavailable.
        }
    }

    private static int ToAccentColor(MediaColor color) =>
        unchecked((int)((uint)color.A << 24 | (uint)color.B << 16 | (uint)color.G << 8 | color.R));
}
