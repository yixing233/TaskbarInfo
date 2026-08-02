namespace TaskbarInfo;

public readonly record struct WindowTrackSizeBounds(
    int MinimumWidth,
    int MinimumHeight,
    int MaximumWidth,
    int MaximumHeight);

public static class WindowSizeConstraints
{
    public static WindowTrackSizeBounds GetTrackSizeBounds(
        int minimumWidthDip,
        int minimumHeightDip,
        int maximumWidthDip,
        int maximumHeightDip,
        uint dpi)
    {
        double scale = Math.Max(dpi, 96) / 96d;
        return new WindowTrackSizeBounds(
            ScaleToPixels(minimumWidthDip, scale),
            ScaleToPixels(minimumHeightDip, scale),
            ScaleToPixels(maximumWidthDip, scale),
            ScaleToPixels(maximumHeightDip, scale));
    }

    private static int ScaleToPixels(int dip, double scale) =>
        (int)Math.Ceiling(dip * scale);
}
