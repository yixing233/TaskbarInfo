using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TaskbarInfo;

/// <summary>
/// Pure layout helpers for the independent taskbar performance component.
/// Keeping the width stable prevents changing numbers from moving the lyric window.
/// </summary>
public static class TaskbarPerformanceLayout
{
    private const int HorizontalPadding = 14;
    private const int ItemGap = 7;
    private const int ComponentGap = 6;

    private const string DefaultFontFamily = "Microsoft YaHei UI, Segoe UI";

    public static int GetWidth(IEnumerable<string>? metricIds, bool doubleLine = false)
    {
        var metrics = TaskbarPerformanceMetricCatalog.Normalize(metricIds);
        if (metrics.Count == 0) return 0;

        static int GetMetricWidth(string metric) => metric switch
        {
            TaskbarPerformanceMetricCatalog.Cpu => 58,
            TaskbarPerformanceMetricCatalog.Memory => 66,
            TaskbarPerformanceMetricCatalog.Gpu => 58,
            TaskbarPerformanceMetricCatalog.CpuTemperature => 67,
            TaskbarPerformanceMetricCatalog.GpuTemperature => 67,
            TaskbarPerformanceMetricCatalog.DiskTemperature => 72,
            TaskbarPerformanceMetricCatalog.Download or TaskbarPerformanceMetricCatalog.Upload => 82,
            _ => 0
        };

        if (doubleLine && metrics.Count > 1)
        {
            int firstLineCount = (metrics.Count + 1) / 2;
            int firstLineWidth = metrics.Take(firstLineCount).Sum(GetMetricWidth);
            int secondLineWidth = metrics.Skip(firstLineCount).Sum(GetMetricWidth);
            int firstGaps = Math.Max(0, firstLineCount - 1) * ItemGap;
            int secondGaps = Math.Max(0, metrics.Count - firstLineCount - 1) * ItemGap;
            return Math.Max(firstLineWidth + firstGaps, secondLineWidth + secondGaps) + HorizontalPadding * 2;
        }

        int width = metrics.Sum(GetMetricWidth) + Math.Max(0, metrics.Count - 1) * ItemGap;

        return width + HorizontalPadding * 2;
    }

    /// <summary>
    /// Calculates the physical width needed by the rendered labels. The old overload is
    /// intentionally retained for persisted-layout compatibility and unit tests; runtime
    /// callers should use this overload so custom fonts, sizes, and monitor DPI are honored.
    /// </summary>
    public static int GetWidth(
        IEnumerable<string>? metricIds,
        bool doubleLine,
        string? fontFamily,
        double fontSize,
        string? fontWeight = null,
        double pixelsPerDip = 1)
    {
        var metrics = TaskbarPerformanceMetricCatalog.Normalize(metricIds);
        if (metrics.Count == 0) return 0;

        pixelsPerDip = double.IsFinite(pixelsPerDip) && pixelsPerDip > 0 ? pixelsPerDip : 1;
        fontSize = double.IsFinite(fontSize) && fontSize > 0 ? fontSize : 10;
        var typeface = new Typeface(
            new System.Windows.Media.FontFamily(string.IsNullOrWhiteSpace(fontFamily) ? DefaultFontFamily : fontFamily),
            FontStyles.Normal,
            GetFontWeight(fontWeight),
            FontStretches.Normal);

        static string Sample(string metric) => metric switch
        {
            TaskbarPerformanceMetricCatalog.Cpu => "CPU 100%",
            TaskbarPerformanceMetricCatalog.Memory => "内存 100%",
            TaskbarPerformanceMetricCatalog.Gpu => "GPU 100%",
            TaskbarPerformanceMetricCatalog.CpuTemperature => "CPU 100°C",
            TaskbarPerformanceMetricCatalog.GpuTemperature => "GPU 100°C",
            TaskbarPerformanceMetricCatalog.DiskTemperature => "磁盘 100°C",
            TaskbarPerformanceMetricCatalog.Download or TaskbarPerformanceMetricCatalog.Upload => "↓ 999.99 GB/s",
            _ => string.Empty
        };

        int Measure(string metric)
        {
            string sample = Sample(metric);
            if (sample.Length == 0) return 0;
            var text = new FormattedText(
                sample,
                CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                typeface,
                fontSize,
                System.Windows.Media.Brushes.White,
                pixelsPerDip);
            return (int)Math.Ceiling(text.Width);
        }

        int GetLineWidth(IEnumerable<string> lineMetrics)
        {
            var widths = lineMetrics.Select(Measure).ToList();
            return widths.Sum() + Math.Max(0, widths.Count - 1) * ItemGap;
        }

        int contentWidth;
        if (doubleLine && metrics.Count > 1)
        {
            int firstLineCount = (metrics.Count + 1) / 2;
            contentWidth = Math.Max(
                GetLineWidth(metrics.Take(firstLineCount)),
                GetLineWidth(metrics.Skip(firstLineCount)));
        }
        else
        {
            contentWidth = GetLineWidth(metrics);
        }

        // Border padding plus the drag handle/text margin. These values are WPF DIPs,
        // so scale the complete logical width before passing it to MoveWindow (pixels).
        // Keep a small safety pixel so glyph overhang and ClearType rounding cannot clip
        // the final character.
        return (int)Math.Ceiling((contentWidth + HorizontalPadding * 2) * pixelsPerDip) + 2;
    }

    private static FontWeight GetFontWeight(string? value) => value switch
    {
        "Light" => FontWeights.Light,
        "SemiBold" => FontWeights.SemiBold,
        "Bold" => FontWeights.Bold,
        _ => FontWeights.Normal
    };

    public static int GetLeftBesideLyrics(
        int taskbarWidth,
        int lyricLeft,
        IEnumerable<string>? metricIds,
        bool doubleLine = false)
    {
        int performanceWidth = GetWidth(metricIds, doubleLine);
        if (performanceWidth == 0 || taskbarWidth <= 0) return 0;

        int maxLeft = Math.Max(0, taskbarWidth - performanceWidth);
        int candidate = lyricLeft - performanceWidth - ComponentGap;
        return Math.Clamp(candidate, 0, maxLeft);
    }

    public static int GetLeftBesideLyrics(
        int taskbarWidth,
        int lyricLeft,
        IEnumerable<string>? metricIds,
        bool doubleLine,
        string? fontFamily,
        double fontSize,
        string? fontWeight,
        double pixelsPerDip)
    {
        int performanceWidth = GetWidth(metricIds, doubleLine, fontFamily, fontSize, fontWeight, pixelsPerDip);
        if (performanceWidth == 0 || taskbarWidth <= 0) return 0;

        int maxLeft = Math.Max(0, taskbarWidth - performanceWidth);
        int candidate = lyricLeft - performanceWidth - ComponentGap;
        return Math.Clamp(candidate, 0, maxLeft);
    }

    public static int GetLeftFromTray(
        int taskbarWidth,
        int trayLeft,
        IEnumerable<string>? metricIds,
        int offsetX,
        bool doubleLine = false)
    {
        int performanceWidth = GetWidth(metricIds, doubleLine);
        if (performanceWidth == 0 || taskbarWidth <= 0) return 0;

        int maxLeft = Math.Max(0, taskbarWidth - performanceWidth);
        int candidate = trayLeft - performanceWidth - Math.Max(0, offsetX);
        return Math.Clamp(candidate, 0, maxLeft);
    }

    public static int GetOffsetForLeft(int trayLeft, int performanceWidth, int left)
    {
        return Math.Max(0, trayLeft - performanceWidth - left);
    }

    public static (int Left, int Top, int Width, int Height) GetPosition(
        int taskbarWidth,
        int taskbarHeight,
        int trayLeft,
        int offsetX,
        IEnumerable<string>? metricIds,
        bool doubleLine = false)
    {
        int width = GetWidth(metricIds, doubleLine);
        return (GetLeftFromTray(taskbarWidth, trayLeft, metricIds, offsetX, doubleLine), 0, width, Math.Max(1, taskbarHeight));
    }

    public static (int Left, int Top, int Width, int Height) GetPosition(
        int taskbarWidth,
        int taskbarHeight,
        int trayLeft,
        int offsetX,
        IEnumerable<string>? metricIds,
        bool doubleLine,
        string? fontFamily,
        double fontSize,
        string? fontWeight,
        double pixelsPerDip)
    {
        int width = GetWidth(metricIds, doubleLine, fontFamily, fontSize, fontWeight, pixelsPerDip);
        return (GetLeftFromTray(taskbarWidth, trayLeft, width, offsetX), 0, width, Math.Max(1, taskbarHeight));
    }

    private static int GetLeftFromTray(int taskbarWidth, int trayLeft, int performanceWidth, int offsetX)
    {
        if (performanceWidth == 0 || taskbarWidth <= 0) return 0;
        int maxLeft = Math.Max(0, taskbarWidth - performanceWidth);
        int candidate = trayLeft - performanceWidth - Math.Max(0, offsetX);
        return Math.Clamp(candidate, 0, maxLeft);
    }
}
