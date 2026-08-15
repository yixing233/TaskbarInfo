using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace TaskbarInfo;

public sealed class TaskbarPerformanceCollector : IDisposable
{
    private const uint PdhSuccess = 0x00000000;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhFmtDouble = 0x00000200;
    private static readonly TimeSpan NetworkInterfaceRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TemperatureRefreshInterval = TimeSpan.FromSeconds(2);

    private readonly object _sync = new();
    private System.Threading.Timer? _timer;
    private int _refreshSeconds;
    private int _collecting;
    private bool _disposed;
    private long _previousIdle;
    private long _previousTotal;
    private long _previousReceived;
    private long _previousSent;
    private DateTime _previousNetworkTime;
    private readonly NetworkAddressChangedEventHandler _networkAddressChangedHandler;
    private readonly TaskbarTemperatureReader _temperatureReader = new();
    private NetworkInterface[] _networkInterfaces = Array.Empty<NetworkInterface>();
    private DateTime _nextNetworkInterfaceRefresh;
    private int _networkInterfacesDirty = 1;
    private TaskbarTemperatureSnapshot _temperatureSnapshot = TaskbarTemperatureSnapshot.Empty;
    private DateTime _nextTemperatureRefresh;
    private string[]? _cpuDeviceNames;
    private IntPtr _pdhQuery;
    private IntPtr _gpuUsageCounter;
    private IntPtr _gpuDedicatedMemoryCounter;
    private IntPtr _cpuFrequencyCounter;
    private IntPtr _diskReadCounter;
    private IntPtr _diskWriteCounter;
    private IntPtr _networkDownloadCounter;
    private IntPtr _networkUploadCounter;
    private bool _pdhPrimed;

    public event EventHandler<TaskbarPerformanceSnapshot>? SnapshotUpdated;

    public bool IsRunning => _timer != null;
    public void SetEnhancedTemperatureSensorsEnabled(bool enabled) => _temperatureReader.SetEnhancedMode(enabled);

    public TaskbarPerformanceCollector()
    {
        _networkAddressChangedHandler = (_, _) => Volatile.Write(ref _networkInterfacesDirty, 1);
        NetworkChange.NetworkAddressChanged += _networkAddressChangedHandler;
        TryInitializePdhCounters();
    }

    public void Start(int refreshSeconds)
    {
        refreshSeconds = refreshSeconds is 1 or 2 or 5 ? refreshSeconds : 1;

        lock (_sync)
        {
            ThrowIfDisposed();
            if (_timer != null && _refreshSeconds == refreshSeconds) return;

            _timer?.Dispose();
            _refreshSeconds = refreshSeconds;
            _timer = new System.Threading.Timer(
                _ => CollectSafely(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(refreshSeconds));
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _timer?.Dispose();
            _timer = null;
            ResetBaselines();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            NetworkChange.NetworkAddressChanged -= _networkAddressChangedHandler;
            _temperatureReader.Dispose();

            RemovePdhCounter(ref _gpuUsageCounter);
            RemovePdhCounter(ref _gpuDedicatedMemoryCounter);
            RemovePdhCounter(ref _cpuFrequencyCounter);
            RemovePdhCounter(ref _diskReadCounter);
            RemovePdhCounter(ref _diskWriteCounter);
            RemovePdhCounter(ref _networkDownloadCounter);
            RemovePdhCounter(ref _networkUploadCounter);

            if (_pdhQuery != IntPtr.Zero)
            {
                PdhCloseQuery(_pdhQuery);
                _pdhQuery = IntPtr.Zero;
            }
        }
    }

    private void CollectSafely()
    {
        if (Interlocked.Exchange(ref _collecting, 1) != 0) return;

        try
        {
            TaskbarPerformanceSnapshot snapshot = Collect();
            SnapshotUpdated?.Invoke(this, snapshot);
        }
        catch
        {
            // A missing counter or a transient network adapter must not affect the UI thread.
        }
        finally
        {
            Volatile.Write(ref _collecting, 0);
        }
    }

    private TaskbarPerformanceSnapshot Collect()
    {
        double? cpu = ReadCpuUsage();
        (double? memory, double? memoryUsed, double? memoryTotal) = ReadMemory();
        PdhSnapshot pdh = ReadPdhSnapshot();
        (double download, double upload) = ReadNetworkRates();
        download = pdh.DownloadBytesPerSecond ?? download;
        upload = pdh.UploadBytesPerSecond ?? upload;
        TaskbarTemperatureSnapshot temperatures = ReadTemperatures();
        return new TaskbarPerformanceSnapshot(
            cpu,
            memory,
            pdh.GpuUsagePercent,
            download,
            upload,
            temperatures.CpuTemperatureCelsius,
            temperatures.GpuTemperatureCelsius,
            temperatures.DiskTemperatureCelsius,
            pdh.CpuFrequencyMegahertz,
            pdh.GpuDedicatedMemoryBytes,
            memoryUsed,
            memoryTotal,
            pdh.DiskReadBytesPerSecond,
            pdh.DiskWriteBytesPerSecond,
            temperatures.GpuDeviceNames,
            temperatures.DiskDeviceNames,
            _cpuDeviceNames ??= ReadCpuDeviceNames());
    }

    private static string[]? ReadCpuDeviceNames()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            string? name = key?.GetValue("ProcessorNameString") as string;
            string? shortName = TaskbarPerformanceDeviceSummary.ShortenCpuModelName(name);
            return shortName is null ? null : [shortName];
        }
        catch
        {
            return null;
        }
    }

    private TaskbarTemperatureSnapshot ReadTemperatures()
    {
        DateTime now = DateTime.UtcNow;
        if (now < _nextTemperatureRefresh) return _temperatureSnapshot;

        _temperatureSnapshot = _temperatureReader.Read();
        _nextTemperatureRefresh = now.Add(TemperatureRefreshInterval);
        return _temperatureSnapshot;
    }

    private double? ReadCpuUsage()
    {
        if (!OperatingSystem.IsWindows() || !GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user))
        {
            return null;
        }

        long idleTicks = ToInt64(idle);
        long totalTicks = ToInt64(kernel) + ToInt64(user);
        if (_previousTotal == 0)
        {
            _previousIdle = idleTicks;
            _previousTotal = totalTicks;
            return null;
        }

        long totalDelta = totalTicks - _previousTotal;
        long idleDelta = idleTicks - _previousIdle;
        _previousIdle = idleTicks;
        _previousTotal = totalTicks;
        if (totalDelta <= 0) return null;

        return Math.Clamp((totalDelta - idleDelta) * 100d / totalDelta, 0, 100);
    }

    private static (double? UsagePercent, double? UsedBytes, double? TotalBytes) ReadMemory()
    {
        if (!OperatingSystem.IsWindows()) return (null, null, null);

        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysicalMemory == 0)
        {
            return (null, null, null);
        }

        double total = status.TotalPhysicalMemory;
        double used = Math.Max(status.TotalPhysicalMemory - status.AvailablePhysicalMemory, 0);
        return (Math.Clamp(used * 100d / total, 0, 100), used, total);
    }

    private (double Download, double Upload) ReadNetworkRates()
    {
        long received = 0;
        long sent = 0;
        try
        {
            foreach (NetworkInterface networkInterface in GetNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or
                        NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp)
                {
                    continue;
                }

                try
                {
                    IPv4InterfaceStatistics statistics = networkInterface.GetIPv4Statistics();
                    received += statistics.BytesReceived;
                    sent += statistics.BytesSent;
                }
                catch
                {
                    // Some virtual adapters do not expose IPv4 statistics.
                }
            }
        }
        catch
        {
            return (0, 0);
        }

        DateTime now = DateTime.UtcNow;
        if (_previousNetworkTime == default)
        {
            _previousReceived = received;
            _previousSent = sent;
            _previousNetworkTime = now;
            return (0, 0);
        }

        double seconds = Math.Max((now - _previousNetworkTime).TotalSeconds, 0.1);
        double download = Math.Max(received - _previousReceived, 0) / seconds;
        double upload = Math.Max(sent - _previousSent, 0) / seconds;
        _previousReceived = received;
        _previousSent = sent;
        _previousNetworkTime = now;
        return (download, upload);
    }

    private NetworkInterface[] GetNetworkInterfaces()
    {
        DateTime now = DateTime.UtcNow;
        if (Volatile.Read(ref _networkInterfacesDirty) == 0 && now < _nextNetworkInterfaceRefresh)
        {
            return _networkInterfaces;
        }

        lock (_sync)
        {
            if (Volatile.Read(ref _networkInterfacesDirty) == 0 && now < _nextNetworkInterfaceRefresh)
            {
                return _networkInterfaces;
            }

            try
            {
                _networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch
            {
                _networkInterfaces = Array.Empty<NetworkInterface>();
            }

            _nextNetworkInterfaceRefresh = now.Add(NetworkInterfaceRefreshInterval);
            Volatile.Write(ref _networkInterfacesDirty, 0);
            return _networkInterfaces;
        }
    }

    private PdhSnapshot ReadPdhSnapshot()
    {
        if (_pdhQuery == IntPtr.Zero) return default;

        uint collectStatus = PdhCollectQueryData(_pdhQuery);
        if (collectStatus != PdhSuccess)
        {
            return default;
        }

        if (!_pdhPrimed)
        {
            _pdhPrimed = true;
            return default;
        }

        double? gpuUsage = ReadPdhCounterArrayTotal(_gpuUsageCounter);
        return new PdhSnapshot(
            gpuUsage.HasValue ? Math.Clamp(gpuUsage.Value, 0, 100) : null,
            ReadPdhCounterArrayTotal(_gpuDedicatedMemoryCounter),
            ReadPdhCounterValue(_cpuFrequencyCounter),
            ReadPdhCounterValue(_diskReadCounter),
            ReadPdhCounterValue(_diskWriteCounter),
            ReadPdhNetworkRate(_networkDownloadCounter),
            ReadPdhNetworkRate(_networkUploadCounter));
    }

    private static double? ReadPdhCounterArrayTotal(IntPtr counter) =>
        ReadPdhCounterArray(counter, static _ => true);

    private static double? ReadPdhNetworkRate(IntPtr counter) =>
        ReadPdhCounterArray(counter, IsPhysicalNetworkInstance);

    private static bool IsPhysicalNetworkInstance(string instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName)) return false;

        return !instanceName.Contains("pseudo", StringComparison.OrdinalIgnoreCase) &&
               !instanceName.Contains("teredo", StringComparison.OrdinalIgnoreCase) &&
               !instanceName.Contains("isatap", StringComparison.OrdinalIgnoreCase) &&
               !instanceName.Contains("vethernet", StringComparison.OrdinalIgnoreCase) &&
               !instanceName.Contains("loopback", StringComparison.OrdinalIgnoreCase) &&
               !instanceName.Contains("bluetooth", StringComparison.OrdinalIgnoreCase) &&
               !instanceName.Contains("virtual", StringComparison.OrdinalIgnoreCase) &&
               !instanceName.Contains("hyper-v", StringComparison.OrdinalIgnoreCase) &&
               !instanceName.Contains("docker", StringComparison.OrdinalIgnoreCase) &&
               !instanceName.Contains("vmware", StringComparison.OrdinalIgnoreCase) &&
               !instanceName.Contains("tap", StringComparison.OrdinalIgnoreCase) &&
               !instanceName.Contains("tun", StringComparison.OrdinalIgnoreCase) &&
               !instanceName.Contains("ppp", StringComparison.OrdinalIgnoreCase) &&
               !instanceName.Contains("vpn", StringComparison.OrdinalIgnoreCase) &&
               !instanceName.Contains("wireguard", StringComparison.OrdinalIgnoreCase) &&
               !instanceName.Contains("zerotier", StringComparison.OrdinalIgnoreCase) &&
               !instanceName.Contains("tailscale", StringComparison.OrdinalIgnoreCase) &&
               !instanceName.Contains("hamachi", StringComparison.OrdinalIgnoreCase);
    }

    private static double? ReadPdhCounterArray(IntPtr counter, Func<string, bool> includeInstance)
    {
        if (counter == IntPtr.Zero) return null;

        uint bufferSize = 0;
        uint itemCount = 0;
        uint status = PdhGetFormattedCounterArrayW(
            counter,
            PdhFmtDouble,
            ref bufferSize,
            ref itemCount,
            IntPtr.Zero);
        if (status != PdhMoreData && status != PdhSuccess || bufferSize == 0 || itemCount == 0)
        {
            return null;
        }

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)bufferSize));
        try
        {
            status = PdhGetFormattedCounterArrayW(
                counter,
                PdhFmtDouble,
                ref bufferSize,
                ref itemCount,
                buffer);
            if (status != PdhSuccess) return null;

            int itemSize = Marshal.SizeOf<PdhFormattedCounterItem>();
            double total = 0;
            bool hasValue = false;
            for (uint index = 0; index < itemCount; index++)
            {
                var item = Marshal.PtrToStructure<PdhFormattedCounterItem>(
                    IntPtr.Add(buffer, checked((int)(index * (uint)itemSize))));
                if (item.Value.Status != PdhSuccess || double.IsNaN(item.Value.DoubleValue)) continue;

                string? instanceName = item.Name == IntPtr.Zero ? null : Marshal.PtrToStringUni(item.Name);
                if (instanceName is not null && !includeInstance(instanceName)) continue;

                total += Math.Max(item.Value.DoubleValue, 0);
                hasValue = true;
            }

            return hasValue ? total : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static double? ReadPdhCounterValue(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return null;

        uint counterType = 0;
        uint status = PdhGetFormattedCounterValue(
            counter,
            PdhFmtDouble,
            out counterType,
            out PdhFormattedCounterValue value);
        return status == PdhSuccess && value.Status == PdhSuccess &&
               !double.IsNaN(value.DoubleValue) && !double.IsInfinity(value.DoubleValue)
            ? Math.Max(value.DoubleValue, 0)
            : null;
    }

    private void TryInitializePdhCounters()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            if (PdhOpenQuery(null, UIntPtr.Zero, out _pdhQuery) != PdhSuccess) return;
            _gpuUsageCounter = TryAddPdhCounter("\\GPU Engine(*)\\Utilization Percentage");
            _gpuDedicatedMemoryCounter = TryAddPdhCounter("\\GPU Adapter Memory(*)\\Dedicated Usage");
            _cpuFrequencyCounter = TryAddPdhCounter("\\Processor Information(_Total)\\Processor Frequency");
            _diskReadCounter = TryAddPdhCounter("\\PhysicalDisk(_Total)\\Disk Read Bytes/sec");
            _diskWriteCounter = TryAddPdhCounter("\\PhysicalDisk(_Total)\\Disk Write Bytes/sec");
            _networkDownloadCounter = TryAddPdhCounter("\\Network Interface(*)\\Bytes Received/sec");
            _networkUploadCounter = TryAddPdhCounter("\\Network Interface(*)\\Bytes Sent/sec");
            if (_gpuUsageCounter == IntPtr.Zero &&
                _gpuDedicatedMemoryCounter == IntPtr.Zero &&
                _cpuFrequencyCounter == IntPtr.Zero &&
                _diskReadCounter == IntPtr.Zero &&
                _diskWriteCounter == IntPtr.Zero &&
                _networkDownloadCounter == IntPtr.Zero &&
                _networkUploadCounter == IntPtr.Zero)
            {
                PdhCloseQuery(_pdhQuery);
                _pdhQuery = IntPtr.Zero;
            }
        }
        catch
        {
            RemovePdhCounter(ref _gpuUsageCounter);
            RemovePdhCounter(ref _gpuDedicatedMemoryCounter);
            RemovePdhCounter(ref _cpuFrequencyCounter);
            RemovePdhCounter(ref _diskReadCounter);
            RemovePdhCounter(ref _diskWriteCounter);
            RemovePdhCounter(ref _networkDownloadCounter);
            RemovePdhCounter(ref _networkUploadCounter);
            if (_pdhQuery != IntPtr.Zero)
            {
                PdhCloseQuery(_pdhQuery);
                _pdhQuery = IntPtr.Zero;
            }
        }
    }

    private IntPtr TryAddPdhCounter(string path) =>
        PdhAddEnglishCounterW(_pdhQuery, path, UIntPtr.Zero, out IntPtr counter) == PdhSuccess
            ? counter
            : IntPtr.Zero;

    private static void RemovePdhCounter(ref IntPtr counter)
    {
        if (counter == IntPtr.Zero) return;
        PdhRemoveCounter(counter);
        counter = IntPtr.Zero;
    }

    private void ResetBaselines()
    {
        _previousIdle = 0;
        _previousTotal = 0;
        _previousReceived = 0;
        _previousSent = 0;
        _previousNetworkTime = default;
        _pdhPrimed = false;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TaskbarPerformanceCollector));
    }

    private static long ToInt64(FileTime value) => ((long)value.HighDateTime << 32) + (uint)value.LowDateTime;

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, UIntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string fullCounterPath, UIntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterArrayW(
        IntPtr counter,
        uint format,
        ref uint bufferSize,
        ref uint itemCount,
        IntPtr buffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(
        IntPtr counter,
        uint format,
        out uint counterType,
        out PdhFormattedCounterValue value);

    [DllImport("pdh.dll")]
    private static extern uint PdhRemoveCounter(IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public int LowDateTime;
        public int HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysicalMemory;
        public ulong AvailablePhysicalMemory;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFormattedCounterValue
    {
        public uint Status;
        public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFormattedCounterItem
    {
        public IntPtr Name;
        public PdhFormattedCounterValue Value;
    }

    private readonly record struct PdhSnapshot(
        double? GpuUsagePercent,
        double? GpuDedicatedMemoryBytes,
        double? CpuFrequencyMegahertz,
        double? DiskReadBytesPerSecond,
        double? DiskWriteBytesPerSecond,
        double? DownloadBytesPerSecond,
        double? UploadBytesPerSecond);
}
