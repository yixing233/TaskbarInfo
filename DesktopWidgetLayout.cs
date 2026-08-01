using System;

namespace TaskbarInfo
{
    public static class DesktopWidgetLayout
    {
        public const double Width = 460;
        public const double Height = 168;
        public const double LyricLineHeight = 18;
        public const int LyricMaxLines = 3;

        public static DesktopWidgetPixelSize GetPixelSize(double dpiScaleX, double dpiScaleY)
        {
            return new DesktopWidgetPixelSize(
                Math.Max(1, (int)Math.Round(Width * dpiScaleX)),
                Math.Max(1, (int)Math.Round(Height * dpiScaleY)));
        }

        public static DesktopWidgetPosition ClampToWorkArea(
            int x,
            int y,
            int width,
            int height,
            int workLeft,
            int workTop,
            int workRight,
            int workBottom)
        {
            return new DesktopWidgetPosition(
                Math.Clamp(x, workLeft, Math.Max(workLeft, workRight - width)),
                Math.Clamp(y, workTop, Math.Max(workTop, workBottom - height)));
        }
    }

    public readonly record struct DesktopWidgetPixelSize(int Width, int Height);
    public readonly record struct DesktopWidgetPosition(int X, int Y);
}
