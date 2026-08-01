using TaskbarInfo;

var tests = new (string Name, Action Test)[]
{
    ("Clone copies app filter list independently", CloneCopiesAppFilterListIndependently),
    ("Desktop widget defaults to dark theme", DesktopWidgetDefaultsToDarkTheme),
    ("Desktop widget palettes differ by theme", DesktopWidgetPalettesDifferByTheme),
    ("Desktop widget formats playback time", DesktopWidgetFormatsPlaybackTime),
    ("Desktop widget scales to monitor DPI", DesktopWidgetScalesToMonitorDpi),
    ("Desktop widget clamps inside offset monitor", DesktopWidgetClampsInsideOffsetMonitor),
    ("Desktop host locates Explorer desktop view", DesktopHostLocatesExplorerDesktopView),
    ("Floating marquee keeps two complete lyric copies", FloatingMarqueeKeepsTwoCompleteLyricCopies),
    ("Floating bubble width is explicit and includes padding", FloatingBubbleWidthIsExplicitAndIncludesPadding),
    ("Floating bubble native width matches logical width", FloatingBubbleNativeWidthMatchesLogicalWidth),
    ("Floating marquee panel is not layout clipped", FloatingMarqueePanelIsNotLayoutClipped),
    ("Untimed floating lyric disables active color", UntimedFloatingLyricDisablesActiveColor),
};

var failures = new List<string>();

foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"{failures.Count} test(s) failed.");
    Environment.Exit(1);
}

static void CloneCopiesAppFilterListIndependently()
{
    var original = new AppSettings();
    original.IncludedAppIds.Add("Spotify");

    var clone = original.Clone();
    original.IncludedAppIds.Add("QQMusic");

    AssertEqual(1, clone.IncludedAppIds.Count, "clone should keep the original app filter snapshot");
    AssertEqual("Spotify", clone.IncludedAppIds[0], "clone should preserve existing app id");
}

static void DesktopWidgetDefaultsToDarkTheme()
{
    var settings = new AppSettings();
    AssertEqual(false, settings.EnableDesktopWidget, "desktop widget must be opt-in");
    AssertEqual(DesktopWidgetTheme.Dark, settings.DesktopWidgetTheme, "default theme");
}

static void DesktopWidgetPalettesDifferByTheme()
{
    var dark = DesktopWidgetThemePalette.Get(DesktopWidgetTheme.Dark);
    var light = DesktopWidgetThemePalette.Get(DesktopWidgetTheme.Light);

    AssertEqual("#FF081025", dark.WindowBackground, "dark window background");
    AssertEqual("#FFF4F6FB", light.WindowBackground, "light window background");
    if (dark.PrimaryText == light.PrimaryText)
    {
        throw new InvalidOperationException("theme primary text colors must differ");
    }
}

static void DesktopWidgetFormatsPlaybackTime()
{
    AssertEqual("0:18", DesktopWidgetFormatting.FormatTime(TimeSpan.FromSeconds(18)), "elapsed time");
    AssertEqual("1:03:14", DesktopWidgetFormatting.FormatTime(TimeSpan.FromSeconds(3794)), "long duration");
}

static void DesktopWidgetScalesToMonitorDpi()
{
    var size = DesktopWidgetLayout.GetPixelSize(1.25, 1.25);
    AssertEqual(575, size.Width, "125% widget width");
    AssertEqual(210, size.Height, "125% widget height");
    AssertEqual(3, DesktopWidgetLayout.LyricMaxLines, "visible lyric line count");
}

static void DesktopWidgetClampsInsideOffsetMonitor()
{
    var position = DesktopWidgetLayout.ClampToWorkArea(
        -100,
        20,
        575,
        205,
        -2400,
        140,
        0,
        1430);

    AssertEqual(-575, position.X, "right edge should remain on the left monitor");
    AssertEqual(140, position.Y, "top should respect the monitor's vertical offset");
}

static void DesktopHostLocatesExplorerDesktopView()
{
    if (!OperatingSystem.IsWindows()) return;

    IntPtr desktopView = DesktopHostService.FindDesktopView();
    if (desktopView == IntPtr.Zero)
    {
        throw new InvalidOperationException("Explorer SHELLDLL_DefView was not found.");
    }

    AssertEqual(
        "SHELLDLL_DefView",
        DesktopHostService.GetWindowClassName(desktopView),
        "desktop view class");

    IntPtr desktopHost = DesktopHostService.FindDesktopHost();
    AssertEqual(
        UnmanagedMethods.GetParent(desktopView),
        desktopHost,
        "desktop widget host must be the desktop view parent");

    IntPtr inputHost = DesktopHostService.FindDesktopInputHost();
    string inputHostClass = DesktopHostService.GetWindowClassName(inputHost);
    if (inputHostClass != "SysListView32" && inputHostClass != "SHELLDLL_DefView")
    {
        throw new InvalidOperationException($"Unexpected desktop input host class: {inputHostClass}");
    }
}

static void FloatingMarqueeKeepsTwoCompleteLyricCopies()
{
    double textWidth = FloatingLyricsLayout.GetTextRenderWidth(900.4);
    AssertEqual(903d, textWidth, "text width should include layout rounding and glyph overhang guard");
    AssertEqual(1856d, FloatingLyricsLayout.GetMarqueePanelWidth(textWidth),
        "marquee panel should contain both full copies plus the gap");
}

static void FloatingBubbleWidthIsExplicitAndIncludesPadding()
{
    AssertEqual(225d, FloatingLyricsLayout.GetBubbleWidth(198.2, 12.8, 13.1, 180, 900),
        "automatic bubble width should include both padding edges in logical units");
    AssertEqual(180d, FloatingLyricsLayout.GetBubbleWidth(100, 8, 8, 180, 900),
        "automatic bubble width should respect the responsive minimum");
}

static void FloatingBubbleNativeWidthMatchesLogicalWidth()
{
    RunFloatingWindowLayoutTest(new AppSettings
    {
        FloatingLyricsWidth = null,
        FloatingLyricsFontSize = 18,
        FloatingLyricsUseAcrylic = false,
        FloatingLyricsEnableShadow = false
    }, "你在心上", window =>
    {
        if (window.SizeToContent != System.Windows.SizeToContent.Height)
        {
            throw new InvalidOperationException("floating bubble width must be explicitly managed");
        }

        var background = window.FindName("FloatingBackground") as System.Windows.FrameworkElement
            ?? throw new InvalidOperationException("floating background was not created");
        if (Math.Abs(background.ActualWidth - window.ActualWidth) > 0.01)
        {
            throw new InvalidOperationException("floating background does not fill the HWND width");
        }

        var origin = window.PointToScreen(new System.Windows.Point(0, 0));
        var right = window.PointToScreen(new System.Windows.Point(window.ActualWidth, 0));
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        double expectedPixels = Math.Round(window.Width * dpi.DpiScaleX);
        if (Math.Abs((right.X - origin.X) - expectedPixels) > 1)
        {
            throw new InvalidOperationException("native HWND width is not synchronized with WPF DIPs");
        }
    });
}

static void FloatingMarqueePanelIsNotLayoutClipped()
{
    RunFloatingWindowLayoutTest(new AppSettings
    {
        FloatingLyricsWidth = 216,
        FloatingLyricsUseAcrylic = false,
        FloatingLyricsEnableShadow = false
    }, "要穿碎花洋裙和你一起看海边晚霞", window =>
    {
        var panel = window.FindName("MarqueePanel") as System.Windows.Media.Visual
            ?? throw new InvalidOperationException("marquee panel was not created");
        if (System.Windows.Media.VisualTreeHelper.GetClip(panel) != null)
        {
            throw new InvalidOperationException(
                "marquee panel must not retain a viewport-sized layout clip");
        }
    });
}

static void RunFloatingWindowLayoutTest(
    AppSettings settings,
    string lyric,
    Action<FloatingLyricsWindow> assertion)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        FloatingLyricsWindow? window = null;
        try
        {
            window = new FloatingLyricsWindow(settings);
            window.Show();
            window.UpdateLyrics(lyric);

            var frame = new System.Windows.Threading.DispatcherFrame();
            window.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    window.UpdateLayout();
                    assertion(window);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    window.Close();
                    frame.Continue = false;
                }
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        catch (Exception ex)
        {
            failure = ex;
            window?.Close();
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    if (!thread.Join(TimeSpan.FromSeconds(5)))
    {
        throw new TimeoutException("floating marquee layout test timed out");
    }
    if (failure != null)
    {
        throw new InvalidOperationException("floating marquee layout regression", failure);
    }
}

static void UntimedFloatingLyricDisablesActiveColor()
{
    AssertEqual(false, FloatingLyricsLayout.ShouldUseActiveColor(false),
        "plain LRC lyrics must not paint a synthetic active-color prefix");
    AssertEqual(true, FloatingLyricsLayout.ShouldUseActiveColor(true),
        "word-timed lyrics should retain progress highlighting");
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message}. Expected {expected}, got {actual}.");
    }
}
