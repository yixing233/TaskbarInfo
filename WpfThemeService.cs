using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace TaskbarInfo;

public static class WpfThemeService
{
    public static void Apply(WpfApplication application, ResolvedApplicationTheme theme)
    {
        ArgumentNullException.ThrowIfNull(application);

        bool dark = theme == ResolvedApplicationTheme.Dark;
        application.Resources["ThemeMenuBackgroundBrush"] = Brush(dark ? "#FF20242B" : "#FFFFFFFF");
        application.Resources["ThemeMenuForegroundBrush"] = Brush(dark ? "#FFF1F5F9" : "#FF1B2530");
        application.Resources["ThemeMenuHoverBrush"] = Brush(dark ? "#FF303842" : "#FFF0F4F8");
        application.Resources["ThemeMenuCheckedBrush"] = Brush(dark ? "#FF173D63" : "#FFE6F2FF");
        application.Resources["ThemeMenuBorderBrush"] = Brush(dark ? "#FF3D4754" : "#FFD9E0E8");
        application.Resources["ThemeMenuSeparatorBrush"] = Brush(dark ? "#FF3D4754" : "#FFD9E0E8");
        application.Resources["ThemeMenuIconBrush"] = Brush(dark ? "#FFB8C4D0" : "#FF596675");
        application.Resources["ThemeSurfaceBrush"] = Brush(dark ? "#B81B222C" : "#A8FFFFFF");
        application.Resources["ThemeControlBackgroundBrush"] = Brush(dark ? "#9A27313D" : "#A8FFFFFF");
        application.Resources["ThemeControlBorderBrush"] = Brush(dark ? "#FF536171" : "#A6C8D0DB");
        application.Resources["ThemePrimaryTextBrush"] = Brush(dark ? "#FFF1F5F9" : "#FF1B2530");
        application.Resources["ThemeSecondaryTextBrush"] = Brush(dark ? "#FFB8C4D0" : "#FF596675");
    }

    public static WpfBrush GetBrush(string key) => (WpfBrush)WpfApplication.Current.Resources[key];

    private static WpfSolidColorBrush Brush(string hex)
    {
        var brush = new WpfSolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
