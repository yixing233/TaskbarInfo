namespace TaskbarInfo
{
    public enum DesktopWidgetTheme
    {
        Dark,
        Light
    }

    public sealed record DesktopWidgetThemePalette(
        string WindowBackground,
        string CardBackground,
        string CardBorder,
        string PrimaryText,
        string SecondaryText,
        string LyricText,
        string ControlForeground,
        string ControlHover,
        string ControlPressed,
        string ProgressTrack,
        string Accent)
    {
        public static DesktopWidgetThemePalette Get(DesktopWidgetTheme theme) => theme switch
        {
            DesktopWidgetTheme.Light => new(
                "#FFF4F6FB", "#E6FFFFFF", "#16000000", "#1D2433", "#667085",
                "#333C4D", "#334155", "#12000000", "#22000000", "#D7DDE7", "#7C3AED"),
            _ => new(
                "#FF081025", "#E6081025", "#16FFFFFF", "#F6F8FF", "#78839D",
                "#DEE5F5", "#DDE5F7", "#20FFFFFF", "#35FFFFFF", "#1B2338", "#B778FF")
        };
    }
}
