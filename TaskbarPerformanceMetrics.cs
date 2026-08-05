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
    public const string CpuTemperature = "cpu-temperature";
    public const string GpuTemperature = "gpu-temperature";
    public const string DiskTemperature = "disk-temperature";
    public const string Download = "download";
    public const string Upload = "upload";

    public static IReadOnlyList<TaskbarPerformanceMetricDefinition> Definitions { get; } =
    [
        new(Cpu, "CPU 使用率", "CPU"),
        new(Memory, "内存使用率", "内存"),
        new(Gpu, "GPU 使用率", "GPU"),
        new(CpuTemperature, "CPU 温度", "CPU 温度"),
        new(GpuTemperature, "GPU 温度", "GPU 温度"),
        new(DiskTemperature, "磁盘温度", "磁盘温度"),
        new(Download, "下载速度", "↓"),
        new(Upload, "上传速度", "↑")
    ];

    public static IReadOnlyList<string> DefaultSelection { get; } = [Cpu, Memory, Gpu, Download, Upload];

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

    public static List<string> GetSummarySelection(IEnumerable<string>? metricIds, int count)
    {
        return Normalize(metricIds)
            .Where(metricId => metricId is not CpuTemperature and not GpuTemperature and not DiskTemperature)
            .Take(Math.Clamp(count, 1, Definitions.Count))
            .ToList();
    }

    public static List<string> GetSummarySelection(
        IEnumerable<string>? enabledMetricIds,
        IEnumerable<string>? summaryMetricIds,
        int count)
    {
        var optedIn = Normalize(summaryMetricIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Normalize(enabledMetricIds)
            .Where(optedIn.Contains)
            .Take(Math.Clamp(count, 1, Definitions.Count))
            .ToList();
    }
}

public sealed record TaskbarPerformanceSnapshot(
    double? CpuUsagePercent,
    double? MemoryUsagePercent,
    double? GpuUsagePercent,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond,
    double? CpuTemperatureCelsius,
    double? GpuTemperatureCelsius,
    double? DiskTemperatureCelsius)
{
    public static TaskbarPerformanceSnapshot Empty { get; } = new(null, null, null, 0, 0, null, null, null);
}

public sealed record TaskbarPerformanceMetricDisplay(string Label, string Value);

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
            TaskbarPerformanceMetricDisplay? display = FormatMetric(snapshot, metricId);
            if (display != null) parts.Add($"{display.Label} {display.Value}");
        }

        return parts;
    }

    public static TaskbarPerformanceMetricDisplay? FormatMetric(TaskbarPerformanceSnapshot snapshot, string metricId)
    {
        return metricId switch
        {
            TaskbarPerformanceMetricCatalog.Cpu => FormatPercent("CPU", snapshot.CpuUsagePercent),
            TaskbarPerformanceMetricCatalog.Memory => FormatPercent("内存", snapshot.MemoryUsagePercent),
            TaskbarPerformanceMetricCatalog.Gpu => FormatPercent("GPU", snapshot.GpuUsagePercent),
            TaskbarPerformanceMetricCatalog.CpuTemperature => FormatTemperature("CPU", snapshot.CpuTemperatureCelsius),
            TaskbarPerformanceMetricCatalog.GpuTemperature => FormatTemperature("GPU", snapshot.GpuTemperatureCelsius),
            TaskbarPerformanceMetricCatalog.DiskTemperature => FormatTemperature("磁盘", snapshot.DiskTemperatureCelsius),
            TaskbarPerformanceMetricCatalog.Download => new TaskbarPerformanceMetricDisplay("↓", FormatRate(snapshot.DownloadBytesPerSecond)),
            TaskbarPerformanceMetricCatalog.Upload => new TaskbarPerformanceMetricDisplay("↑", FormatRate(snapshot.UploadBytesPerSecond)),
            _ => null
        };
    }

    private static TaskbarPerformanceMetricDisplay? FormatPercent(string label, double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return null;
        return new TaskbarPerformanceMetricDisplay(
            label,
            $"{Math.Clamp(value.Value, 0, 100).ToString("0", CultureInfo.InvariantCulture)}%");
    }

    private static TaskbarPerformanceMetricDisplay FormatTemperature(string label, double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return new TaskbarPerformanceMetricDisplay(label, "--");
        }

        return new TaskbarPerformanceMetricDisplay(
            label,
            $"{Math.Clamp(value.Value, 0, 150).ToString("0", CultureInfo.InvariantCulture)}°C");
    }

    private static string FormatRate(double value)
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
        return $"{value.ToString(format, CultureInfo.InvariantCulture)} {units[unitIndex]}";
    }
}
