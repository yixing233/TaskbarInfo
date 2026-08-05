namespace TaskbarInfo;

public static class TaskbarComponentMonitorSelection
{
    public static string Resolve(string? componentDeviceName, string? legacyLyricDeviceName) =>
        string.IsNullOrWhiteSpace(componentDeviceName)
            ? legacyLyricDeviceName?.Trim() ?? string.Empty
            : componentDeviceName.Trim();
}
