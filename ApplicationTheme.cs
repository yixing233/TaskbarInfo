namespace TaskbarInfo;

public enum ApplicationThemePreference { System, Light, Dark }

public enum ResolvedApplicationTheme { Light, Dark }

public static class ApplicationThemeParser
{
    public static ApplicationThemePreference Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "light" => ApplicationThemePreference.Light,
        "dark" => ApplicationThemePreference.Dark,
        _ => ApplicationThemePreference.System
    };

    public static string ToStorageValue(ApplicationThemePreference value) => value switch
    {
        ApplicationThemePreference.Light => "Light",
        ApplicationThemePreference.Dark => "Dark",
        _ => "System"
    };

    public static string ToDisplayName(ApplicationThemePreference value) => value switch
    {
        ApplicationThemePreference.Light => "浅色",
        ApplicationThemePreference.Dark => "深色",
        _ => "跟随系统"
    };

    public static ResolvedApplicationTheme Resolve(string? value) => Parse(value) switch
    {
        ApplicationThemePreference.Light => ResolvedApplicationTheme.Light,
        ApplicationThemePreference.Dark => ResolvedApplicationTheme.Dark,
        _ => IsSystemDark() ? ResolvedApplicationTheme.Dark : ResolvedApplicationTheme.Light
    };

    private static bool IsSystemDark()
    {
        const string key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        return Microsoft.Win32.Registry.GetValue(key, "AppsUseLightTheme", 1) is int value && value == 0;
    }
}
