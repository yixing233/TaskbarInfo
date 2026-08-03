using System.Globalization;

namespace TaskbarInfo;

public sealed record TaskbarPerformanceMetricDefinition(
    string Id,
    string DisplayName,
    string ShortLabel);

public static class TaskbarPerformanceMetricCatalog
{
    public const string Cpu = "cpu";
    public const string Memory = "memory";
    public const string Gpu = "gpu";
    public const string Download = "download";
    public const string Upload = "upload";

    public static IReadOnlyList<TaskbarPerformanceMetricDefinition> Definitions { get; } =
    [
        new(Cpu, "CPU 使用率", "CPU"),
        new(Memory, "内存使用率", "内存"),
        new(Gpu, "GPU 使用率", "GPU"),
        new(Download, "下载速度", "↓"),
        new(Upload, "上传速度", "↑")
    ];

    public static IReadOnlyList<string> DefaultSelection { get; } = [Cpu, Memory, Gpu];

    public static List<string> Normalize(IEnumerable<string>? metricIds)
    {
        if (metricIds == null) return [];

        var knownIds = Definitions
            .Select(definition => definition.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string? metricId in metricIds)
        {
            string normalizedId = metricId?.Trim() ?? string.Empty;
            if (normalizedId.Length == 0 || !knownIds.Contains(normalizedId) || !seen.Add(normalizedId))
            {
                continue;
            }

            result.Add(Definitions.First(definition =>
                string.Equals(definition.Id, normalizedId, StringComparison.OrdinalIgnoreCase)).Id);
        }

        return result;
    }
}

public sealed record TaskbarPerformanceSnapshot(
    double? CpuUsagePercent,
    double? MemoryUsagePercent,
    double? GpuUsagePercent,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond)
{
    public static TaskbarPerformanceSnapshot Empty { get; } = new(null, null, null, 0, 0);
}

public static class TaskbarPerformanceFormatter
{
    public static string Format(TaskbarPerformanceSnapshot snapshot, IEnumerable<string>? metricIds, int maxCharacters = 0)
    {
        var parts = GetParts(snapshot, metricIds);

        if (maxCharacters > 0)
        {
            while (parts.Count > 1 && string.Join("  ", parts).Length > maxCharacters)
            {
                parts.RemoveAt(parts.Count - 1);
            }
        }

        return string.Join("  ", parts);
    }

    public static (string First, string Second) FormatLines(
        TaskbarPerformanceSnapshot snapshot,
        IEnumerable<string>? metricIds,
        bool doubleLine)
    {
        var parts = GetParts(snapshot, metricIds);
        if (!doubleLine || parts.Count <= 1)
        {
            return (string.Join("  ", parts), string.Empty);
        }

        int firstCount = (parts.Count + 1) / 2;
        return (
            string.Join("  ", parts.Take(firstCount)),
            string.Join("  ", parts.Skip(firstCount)));
    }

    private static List<string> GetParts(TaskbarPerformanceSnapshot snapshot, IEnumerable<string>? metricIds)
    {
        var parts = new List<string>();
        foreach (string metricId in TaskbarPerformanceMetricCatalog.Normalize(metricIds))
        {
            string? part = metricId switch
            {
                TaskbarPerformanceMetricCatalog.Cpu => FormatPercent("CPU", snapshot.CpuUsagePercent),
                TaskbarPerformanceMetricCatalog.Memory => FormatPercent("内存", snapshot.MemoryUsagePercent),
                TaskbarPerformanceMetricCatalog.Gpu => FormatPercent("GPU", snapshot.GpuUsagePercent),
                TaskbarPerformanceMetricCatalog.Download => FormatRate("↓", snapshot.DownloadBytesPerSecond),
                TaskbarPerformanceMetricCatalog.Upload => FormatRate("↑", snapshot.UploadBytesPerSecond),
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(part)) parts.Add(part);
        }

        return parts;
    }

    private static string? FormatPercent(string label, double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return null;
        return $"{label} {Math.Clamp(value.Value, 0, 100).ToString("0", CultureInfo.InvariantCulture)}%";
    }

    private static string FormatRate(string label, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) value = 0;

        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        int unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        string format = unitIndex == 0 ? "0" : value >= 100 ? "0" : value >= 10 ? "0.0" : "0.00";
        return $"{label} {value.ToString(format, CultureInfo.InvariantCulture)} {units[unitIndex]}";
    }
}
