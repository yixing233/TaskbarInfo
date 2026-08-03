using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace TaskbarInfo;

public sealed class TaskbarPerformanceCollector : IDisposable
{
    private const uint PdhSuccess = 0x00000000;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhFmtDouble = 0x00000200;

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
    private IntPtr _pdhQuery;
    private IntPtr _gpuCounter;
    private bool _gpuPrimed;

    public event EventHandler<TaskbarPerformanceSnapshot>? SnapshotUpdated;

    public bool IsRunning => _timer != null;

    public TaskbarPerformanceCollector()
    {
        TryInitializeGpuCounter();
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

            if (_gpuCounter != IntPtr.Zero)
            {
                PdhRemoveCounter(_gpuCounter);
                _gpuCounter = IntPtr.Zero;
            }

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
        double? memory = ReadMemoryUsage();
        double? gpu = ReadGpuUsage();
        (double download, double upload) = ReadNetworkRates();
        return new TaskbarPerformanceSnapshot(cpu, memory, gpu, download, upload);
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

    private static double? ReadMemoryUsage()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) && status.TotalPhysicalMemory > 0
            ? Math.Clamp((status.TotalPhysicalMemory - status.AvailablePhysicalMemory) * 100d / status.TotalPhysicalMemory, 0, 100)
            : null;
    }

    private (double Download, double Upload) ReadNetworkRates()
    {
        long received = 0;
        long sent = 0;
        try
        {
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
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

    private double? ReadGpuUsage()
    {
        if (_pdhQuery == IntPtr.Zero || _gpuCounter == IntPtr.Zero) return null;

        uint collectStatus = PdhCollectQueryData(_pdhQuery);
        if (collectStatus != PdhSuccess)
        {
            return null;
        }

        if (!_gpuPrimed)
        {
            _gpuPrimed = true;
            return null;
        }

        uint bufferSize = 0;
        uint itemCount = 0;
        uint status = PdhGetFormattedCounterArrayW(
            _gpuCounter,
            PdhFmtDouble,
            ref bufferSize,
            ref itemCount,
            IntPtr.Zero);
        if (status != PdhMoreData && status != PdhSuccess || bufferSize == 0 || itemCount == 0)
        {
            return status == PdhSuccess ? 0 : null;
        }

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)bufferSize));
        try
        {
            status = PdhGetFormattedCounterArrayW(
                _gpuCounter,
                PdhFmtDouble,
                ref bufferSize,
                ref itemCount,
                buffer);
            if (status != PdhSuccess) return null;

            int itemSize = Marshal.SizeOf<PdhFormattedCounterItem>();
            double total = 0;
            for (uint index = 0; index < itemCount; index++)
            {
                var item = Marshal.PtrToStructure<PdhFormattedCounterItem>(
                    IntPtr.Add(buffer, checked((int)(index * (uint)itemSize))));
                if (item.Value.Status == PdhSuccess && !double.IsNaN(item.Value.DoubleValue))
                {
                    total += Math.Max(item.Value.DoubleValue, 0);
                }
            }

            return Math.Clamp(total, 0, 100);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void TryInitializeGpuCounter()
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            if (PdhOpenQuery(null, UIntPtr.Zero, out _pdhQuery) != PdhSuccess) return;
            uint status = PdhAddEnglishCounterW(
                _pdhQuery,
                "\\GPU Engine(*)\\Utilization Percentage",
                UIntPtr.Zero,
                out _gpuCounter);
            if (status != PdhSuccess)
            {
                PdhCloseQuery(_pdhQuery);
                _pdhQuery = IntPtr.Zero;
                _gpuCounter = IntPtr.Zero;
            }
        }
        catch
        {
            _gpuCounter = IntPtr.Zero;
            _pdhQuery = IntPtr.Zero;
        }
    }

    private void ResetBaselines()
    {
        _previousIdle = 0;
        _previousTotal = 0;
        _previousReceived = 0;
        _previousSent = 0;
        _previousNetworkTime = default;
        _gpuPrimed = false;
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
}
