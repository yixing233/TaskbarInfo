using System;

namespace TaskbarInfo
{
    public static class FloatingLyricsLayout
    {
        public const double MarqueeGap = 50;

        public static double GetTextRenderWidth(double desiredWidth)
        {
            return Math.Ceiling(Math.Max(0, desiredWidth)) + 2;
        }

        public static double GetMarqueePanelWidth(double textRenderWidth)
        {
            return Math.Max(0, textRenderWidth) * 2 + MarqueeGap;
        }

        public static double GetBubbleWidth(
            double viewportWidth,
            double leftPadding,
            double rightPadding,
            double minimumWidth,
            double maximumWidth)
        {
            double desiredWidth = Math.Ceiling(
                Math.Max(0, viewportWidth) + Math.Max(0, leftPadding) + Math.Max(0, rightPadding));
            return Math.Clamp(desiredWidth, minimumWidth, Math.Max(minimumWidth, maximumWidth));
        }

        public static bool ShouldUseActiveColor(bool hasTimedProgress)
        {
            return hasTimedProgress;
        }
    }
}
