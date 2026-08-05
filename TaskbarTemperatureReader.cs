using LibreHardwareMonitor.Hardware;

namespace TaskbarInfo;

public sealed record TaskbarTemperatureSnapshot(
    double? CpuTemperatureCelsius,
    double? GpuTemperatureCelsius,
    double? DiskTemperatureCelsius)
{
    public static TaskbarTemperatureSnapshot Empty { get; } = new(null, null, null);

    public static TaskbarTemperatureSnapshot Merge(params TaskbarTemperatureSnapshot[] sources)
    {
        double? cpu = null;
        double? gpu = null;
        double? disk = null;

        foreach (TaskbarTemperatureSnapshot source in sources)
        {
            cpu ??= source.CpuTemperatureCelsius;
            gpu ??= source.GpuTemperatureCelsius;
            disk ??= source.DiskTemperatureCelsius;
        }

        return new TaskbarTemperatureSnapshot(cpu, gpu, disk);
    }
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
        if (hardwareMonitor.DiskTemperatureCelsius.HasValue) return hardwareMonitor;

        return TaskbarTemperatureSnapshot.Merge(
            enhanced, hardwareMonitor,
            new TaskbarTemperatureSnapshot(null, null, _storageReader.Read()));
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
            foreach (IHardware hardware in _computer.Hardware)
            {
                ReadHardware(hardware, ref cpu, ref gpu, ref disk);
            }

            return new TaskbarTemperatureSnapshot(cpu, gpu, disk);
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
        ref double? disk)
    {
        try
        {
            hardware.Update();

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
                ReadHardware(subHardware, ref cpu, ref gpu, ref disk);
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
}
