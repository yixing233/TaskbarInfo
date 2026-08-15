using LibreHardwareMonitor.Hardware;

namespace TaskbarInfo;

public sealed record TaskbarTemperatureSnapshot(
    double? CpuTemperatureCelsius,
    double? GpuTemperatureCelsius,
    double? DiskTemperatureCelsius,
    IReadOnlyList<string>? GpuDevices = null,
    IReadOnlyList<string>? DiskDevices = null)
{
    public IReadOnlyList<string> GpuDeviceNames { get; } = NormalizeDeviceNames(GpuDevices);
    public IReadOnlyList<string> DiskDeviceNames { get; } = NormalizeDeviceNames(DiskDevices);

    public static TaskbarTemperatureSnapshot Empty { get; } = new(null, null, null);

    public static TaskbarTemperatureSnapshot Merge(params TaskbarTemperatureSnapshot[] sources)
    {
        double? cpu = null;
        double? gpu = null;
        double? disk = null;
        var gpuDevices = new List<string>();
        var diskDevices = new List<string>();

        foreach (TaskbarTemperatureSnapshot source in sources)
        {
            cpu ??= source.CpuTemperatureCelsius;
            gpu ??= source.GpuTemperatureCelsius;
            disk ??= source.DiskTemperatureCelsius;
            gpuDevices.AddRange(source.GpuDeviceNames);
            diskDevices.AddRange(source.DiskDeviceNames);
        }

        return new TaskbarTemperatureSnapshot(cpu, gpu, disk, gpuDevices, diskDevices);
    }

    private static IReadOnlyList<string> NormalizeDeviceNames(IEnumerable<string>? names) =>
        names?
            .Select(name => name?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray() ?? Array.Empty<string>();
}

/// <summary>
/// Reads hardware temperatures only when the performance collector asks for them.
/// Hardware and driver support varies, so missing sensor values are expected.
/// </summary>
public sealed class TaskbarTemperatureReader : IDisposable
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsStorageEnabled = true,
        IsControllerEnabled = true
    };
    private readonly WindowsStorageTemperatureReader _storageReader = new();
    private readonly TemperatureSensorHelperClient _helperClient = new();
    private bool _enhanced;
    private bool _opened;
    private bool _disposed;

    public TaskbarTemperatureSnapshot Read()
    {
        if (_disposed) return TaskbarTemperatureSnapshot.Empty;

        TaskbarTemperatureSnapshot enhanced = _helperClient.Read(_enhanced);
        TaskbarTemperatureSnapshot hardwareMonitor = ReadLibreHardwareMonitor();
        TaskbarTemperatureSnapshot merged = TaskbarTemperatureSnapshot.Merge(enhanced, hardwareMonitor);
        if (merged.DiskTemperatureCelsius.HasValue) return merged;

        return TaskbarTemperatureSnapshot.Merge(
            merged, new TaskbarTemperatureSnapshot(null, null, _storageReader.Read()));
    }
    public void SetEnhancedMode(bool enabled) => _enhanced = enabled;

    private TaskbarTemperatureSnapshot ReadLibreHardwareMonitor()
    {
        try
        {
            if (!_opened)
            {
                _computer.Open();
                _opened = true;
            }

            double? cpu = null;
            double? gpu = null;
            double? disk = null;
            var gpuDevices = new List<string>();
            var diskDevices = new List<string>();
            foreach (IHardware hardware in _computer.Hardware)
            {
                ReadHardware(hardware, ref cpu, ref gpu, ref disk, gpuDevices, diskDevices);
            }

            return new TaskbarTemperatureSnapshot(cpu, gpu, disk, gpuDevices, diskDevices);
        }
        catch
        {
            return TaskbarTemperatureSnapshot.Empty;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_opened) _computer.Close();
        }
        catch
        {
            // Closing an unavailable hardware provider should not affect app shutdown.
        }

        _storageReader.Dispose();
        _helperClient.Dispose();
    }

    private static void ReadHardware(
        IHardware hardware,
        ref double? cpu,
        ref double? gpu,
        ref double? disk,
        List<string> gpuDevices,
        List<string> diskDevices)
    {
        try
        {
            hardware.Update();

            switch (hardware.HardwareType)
            {
                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel:
                    AddDeviceName(gpuDevices, hardware.Name);
                    break;
                case HardwareType.Storage:
                    AddDeviceName(diskDevices, hardware.Name);
                    break;
            }

            foreach (ISensor sensor in hardware.Sensors)
            {
                if (sensor.SensorType != SensorType.Temperature || !IsValid(sensor.Value)) continue;

                switch (hardware.HardwareType)
                {
                    case HardwareType.Cpu:
                        cpu = Highest(cpu, sensor.Value!.Value);
                        break;
                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        gpu = Highest(gpu, sensor.Value!.Value);
                        break;
                    case HardwareType.Storage:
                        disk = Highest(disk, sensor.Value!.Value);
                        break;
                }
            }

            foreach (IHardware subHardware in hardware.SubHardware)
            {
                ReadHardware(subHardware, ref cpu, ref gpu, ref disk, gpuDevices, diskDevices);
            }
        }
        catch
        {
            // Individual sensor providers may fail while the device is sleeping or removed.
        }
    }

    private static bool IsValid(float? value) =>
        value.HasValue && float.IsFinite(value.Value) && value.Value >= 0 && value.Value <= 150;

    private static double Highest(double? current, float value) =>
        !current.HasValue ? value : Math.Max(current.Value, value);

    private static void AddDeviceName(List<string> names, string? name)
    {
        string normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || names.Contains(normalized, StringComparer.OrdinalIgnoreCase)) return;
        names.Add(normalized);
    }
}
