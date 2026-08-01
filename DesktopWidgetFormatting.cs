using System;

namespace TaskbarInfo
{
    public static class DesktopWidgetFormatting
    {
        public static string FormatTime(TimeSpan value)
        {
            value = value < TimeSpan.Zero ? TimeSpan.Zero : value;
            return value.TotalHours >= 1
                ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
                : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
        }
    }
}
