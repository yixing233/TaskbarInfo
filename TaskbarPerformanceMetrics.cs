using System.Globalization;
using System.Text.RegularExpressions;

namespace TaskbarInfo;

public sealed record TaskbarPerformanceMetricDefinition(
    string Id,
    string DisplayName,
    string ShortLabel,
    string Group);

public static class TaskbarPerformanceMetricCatalog
{
    public const string Cpu = "cpu";
    public const string Memory = "memory";
    public const string Gpu = "gpu";
    public const string CpuFrequency = "cpu-frequency";
    public const string GpuDedicatedMemory = "gpu-dedicated-memory";
    public const string MemoryUsed = "memory-used";
    public const string CpuTemperature = "cpu-temperature";
    public const string GpuTemperature = "gpu-temperature";
    public const string DiskTemperature = "disk-temperature";
    public const string DiskRead = "disk-read";
    public const string DiskWrite = "disk-write";
    public const string Download = "download";
    public const string Upload = "upload";

    public static IReadOnlyList<TaskbarPerformanceMetricDefinition> Definitions { get; } =
    [
        new(Cpu, "CPU 使用率", "使用率", "CPU"),
        new(CpuFrequency, "CPU 频率", "频率", "CPU"),
        new(CpuTemperature, "CPU 温度", "温度", "CPU"),
        new(Gpu, "GPU 使用率", "使用率", "GPU"),
        new(GpuDedicatedMemory, "GPU 专用显存", "专用显存", "GPU"),
        new(GpuTemperature, "GPU 温度", "温度", "GPU"),
        new(Memory, "内存使用率", "使用率", "内存"),
        new(MemoryUsed, "内存已用容量", "已用", "内存"),
        new(DiskRead, "磁盘读取速度", "读取", "磁盘"),
        new(DiskWrite, "磁盘写入速度", "写入", "磁盘"),
        new(DiskTemperature, "磁盘温度", "温度", "磁盘"),
        new(Download, "下载速度", "下载", "网络"),
        new(Upload, "上传速度", "上传", "网络")
    ];

    public static IReadOnlyList<string> DefaultSelection { get; } = [Cpu, Memory, Gpu, Download, Upload];

    public static TaskbarPerformanceMetricDefinition? GetDefinition(string? metricId) =>
        Definitions.FirstOrDefault(definition => string.Equals(
            definition.Id,
            metricId?.Trim(),
            StringComparison.OrdinalIgnoreCase));

    public static int GetGroupOrder(string group) => group switch
    {
        "CPU" => 0,
        "GPU" => 1,
        "内存" => 2,
        "磁盘" => 3,
        "网络" => 4,
        _ => int.MaxValue
    };

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

            result.Add(GetDefinition(normalizedId)!.Id);
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
    double? DiskTemperatureCelsius,
    double? CpuFrequencyMegahertz = null,
    double? GpuDedicatedMemoryBytes = null,
    double? MemoryUsedBytes = null,
    double? MemoryTotalBytes = null,
    double? DiskReadBytesPerSecond = null,
    double? DiskWriteBytesPerSecond = null,
    IReadOnlyList<string>? GpuDeviceNames = null,
    IReadOnlyList<string>? DiskDeviceNames = null,
    IReadOnlyList<string>? CpuDeviceNames = null)
{
    public static TaskbarPerformanceSnapshot Empty { get; } = new(null, null, null, 0, 0, null, null, null);
}

public sealed record TaskbarPerformanceMetricDisplay(string Label, string Value);

public static class TaskbarPerformanceDeviceSummary
{
    public static string GetLabel(IEnumerable<string>? deviceNames)
    {
        IReadOnlyList<string> names = Normalize(deviceNames);
        return names.Count switch
        {
            0 => string.Empty,
            1 => names[0],
            _ => $"{names.Count} 个设备"
        };
    }

    public static string GetToolTip(IEnumerable<string>? deviceNames) =>
        string.Join("\n", Normalize(deviceNames));

    /// <summary>
    /// Reduces a verbose processor brand string (for example
    /// "12th Gen Intel(R) Core(TM) i7-12700H") to a compact label
    /// ("Intel i7-12700h") that fits a group header.
    /// </summary>
    public static string? ShortenCpuModelName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        string text = name.Trim()
            .Replace("(R)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(C)", string.Empty, StringComparison.OrdinalIgnoreCase);

        // "12th Gen Intel Core i7-12700H" -> "Intel i7-12700H"
        text = Regex.Replace(text, @"\d+(?:st|nd|rd|th)\s+Gen\s+", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bCore\b\s+", string.Empty, RegexOptions.IgnoreCase);

        // Drop " CPU @ 2.60GHz" style suffixes.
        text = Regex.Replace(text, @"\s*(?:CPU\s*)?@\s*[\d.]+\s*GHz.*$", string.Empty, RegexOptions.IgnoreCase);

        // Intel model suffixes (for example the "H" in "i7-12700H") display lowercase.
        text = Regex.Replace(text, @"(?<=\bi[3579]-\d+)[A-Za-z]+", match => match.Value.ToLowerInvariant());

        text = text.Trim();
        return text.Length == 0 ? null : text;
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string>? deviceNames) =>
        deviceNames?
            .Select(name => name?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray() ?? Array.Empty<string>();
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
            TaskbarPerformanceMetricCatalog.CpuFrequency => FormatFrequency("CPU", snapshot.CpuFrequencyMegahertz),
            TaskbarPerformanceMetricCatalog.GpuDedicatedMemory => FormatDataSize("GPU 显存", snapshot.GpuDedicatedMemoryBytes),
            TaskbarPerformanceMetricCatalog.MemoryUsed => FormatMemoryUsage(snapshot.MemoryUsedBytes, snapshot.MemoryTotalBytes),
            TaskbarPerformanceMetricCatalog.CpuTemperature => FormatTemperature("CPU", snapshot.CpuTemperatureCelsius),
            TaskbarPerformanceMetricCatalog.GpuTemperature => FormatTemperature("GPU", snapshot.GpuTemperatureCelsius),
            TaskbarPerformanceMetricCatalog.DiskTemperature => FormatTemperature("磁盘", snapshot.DiskTemperatureCelsius),
            TaskbarPerformanceMetricCatalog.DiskRead => FormatRate("磁盘读", snapshot.DiskReadBytesPerSecond),
            TaskbarPerformanceMetricCatalog.DiskWrite => FormatRate("磁盘写", snapshot.DiskWriteBytesPerSecond),
            TaskbarPerformanceMetricCatalog.Download => new TaskbarPerformanceMetricDisplay("↓", FormatRate(snapshot.DownloadBytesPerSecond)),
            TaskbarPerformanceMetricCatalog.Upload => new TaskbarPerformanceMetricDisplay("↑", FormatRate(snapshot.UploadBytesPerSecond)),
            _ => null
        };
    }

    public static TaskbarPerformanceMetricDisplay? FormatDetailMetric(
        TaskbarPerformanceSnapshot snapshot,
        string metricId)
    {
        TaskbarPerformanceMetricDefinition? definition = TaskbarPerformanceMetricCatalog.GetDefinition(metricId);
        TaskbarPerformanceMetricDisplay? display = FormatMetric(snapshot, metricId);
        return definition == null || display == null
            ? null
            : display with { Label = definition.ShortLabel };
    }

    private static TaskbarPerformanceMetricDisplay? FormatPercent(string label, double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return null;
        return new TaskbarPerformanceMetricDisplay(
            label,
            $"{Math.Clamp(value.Value, 0, 100).ToString("0", CultureInfo.InvariantCulture)}%");
    }

    private static TaskbarPerformanceMetricDisplay? FormatTemperature(string label, double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return null;
        }

        return new TaskbarPerformanceMetricDisplay(
            label,
            $"{Math.Clamp(value.Value, 0, 150).ToString("0", CultureInfo.InvariantCulture)}°C");
    }

    private static TaskbarPerformanceMetricDisplay? FormatFrequency(string label, double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value < 0)
        {
            return null;
        }

        double megahertz = value.Value;
        return megahertz >= 1000
            ? new TaskbarPerformanceMetricDisplay(label, $"{(megahertz / 1000).ToString("0.00", CultureInfo.InvariantCulture)} GHz")
            : new TaskbarPerformanceMetricDisplay(label, $"{megahertz.ToString("0", CultureInfo.InvariantCulture)} MHz");
    }

    private static TaskbarPerformanceMetricDisplay? FormatDataSize(string label, double? bytes)
    {
        if (!bytes.HasValue || double.IsNaN(bytes.Value) || double.IsInfinity(bytes.Value) || bytes.Value < 0)
        {
            return null;
        }

        return new TaskbarPerformanceMetricDisplay(label, FormatBytes(bytes.Value));
    }

    private static TaskbarPerformanceMetricDisplay? FormatMemoryUsage(double? usedBytes, double? totalBytes)
    {
        if (!usedBytes.HasValue || !totalBytes.HasValue ||
            double.IsNaN(usedBytes.Value) || double.IsInfinity(usedBytes.Value) || usedBytes.Value < 0 ||
            double.IsNaN(totalBytes.Value) || double.IsInfinity(totalBytes.Value) || totalBytes.Value <= 0)
        {
            return null;
        }

        double total = totalBytes.Value;
        int unitIndex = 0;
        double unitSize = 1;
        while (total / unitSize >= 1024 && unitIndex < 4)
        {
            unitSize *= 1024;
            unitIndex++;
        }

        double used = usedBytes.Value / unitSize;
        double scaledTotal = total / unitSize;
        string format = scaledTotal >= 100 ? "0" : scaledTotal >= 10 ? "0.0" : "0.00";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        return new TaskbarPerformanceMetricDisplay(
            "内存",
            $"{used.ToString(format, CultureInfo.InvariantCulture)} / {scaledTotal.ToString(format, CultureInfo.InvariantCulture)} {units[unitIndex]}");
    }

    private static TaskbarPerformanceMetricDisplay? FormatRate(string label, double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value < 0)
        {
            return null;
        }

        return new TaskbarPerformanceMetricDisplay(label, FormatRate(value.Value));
    }

    private static string FormatBytes(double value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        string format = unitIndex == 0 ? "0" : value >= 100 ? "0" : value >= 10 ? "0.0" : "0.00";
        return $"{value.ToString(format, CultureInfo.InvariantCulture)} {units[unitIndex]}";
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
