using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TaskbarInfo;

public static class WindowsStorageTemperatureParser
{
    private const int DescriptorHeaderSize = 24;
    private const int TemperatureInfoSize = 16;
    private const short TemperatureNotReported = unchecked((short)0x8000);

    public static double? GetHighestTemperature(ReadOnlySpan<byte> descriptor)
    {
        if (descriptor.Length < DescriptorHeaderSize) return null;

        int infoCount = BinaryPrimitives.ReadUInt16LittleEndian(descriptor.Slice(12, sizeof(ushort)));
        int availableCount = (descriptor.Length - DescriptorHeaderSize) / TemperatureInfoSize;
        int count = Math.Min(infoCount, availableCount);
        double? highest = null;

        for (int index = 0; index < count; index++)
        {
            int temperatureOffset = DescriptorHeaderSize + index * TemperatureInfoSize + sizeof(ushort);
            short temperature = BinaryPrimitives.ReadInt16LittleEndian(descriptor.Slice(temperatureOffset, sizeof(short)));
            if (temperature == TemperatureNotReported || temperature < 0 || temperature > 150) continue;

            highest = !highest.HasValue ? temperature : Math.Max(highest.Value, temperature);
        }

        return highest;
    }
}

/// <summary>
/// Reads NVMe and SATA temperatures exposed through the Windows storage stack.
/// Unsupported disks and access-denied handles simply remain unavailable.
/// </summary>
public sealed class WindowsStorageTemperatureReader : IDisposable
{
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const int StorageDeviceTemperatureProperty = 52;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const int MaximumPhysicalDrives = 32;
    private const int DescriptorBufferSize = 1024;
    private static readonly TimeSpan ProbeRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly Dictionary<int, SafeFileHandle> _physicalDriveHandles = [];
    private DateTime _nextProbe;
    private bool _disposed;

    public double? Read()
    {
        if (_disposed || !OperatingSystem.IsWindows()) return null;

        DateTime now = DateTime.UtcNow;
        if (_physicalDriveHandles.Count == 0 && now >= _nextProbe)
        {
            ProbePhysicalDrives(now);
        }

        double? highest = null;
        foreach (SafeFileHandle handle in _physicalDriveHandles.Values)
        {
            double? temperature = ReadTemperature(handle);
            if (temperature.HasValue)
            {
                highest = !highest.HasValue ? temperature : Math.Max(highest.Value, temperature.Value);
            }
        }

        return highest;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeHandles();
    }

    private void ProbePhysicalDrives(DateTime now)
    {
        DisposeHandles();
        _nextProbe = now.Add(ProbeRefreshInterval);

        for (int index = 0; index < MaximumPhysicalDrives; index++)
        {
            SafeFileHandle handle = CreateFile(
                $"\\\\.\\PhysicalDrive{index}",
                0,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);
            if (!handle.IsInvalid)
            {
                _physicalDriveHandles[index] = handle;
            }
            else
            {
                handle.Dispose();
            }
        }
    }

    private static double? ReadTemperature(SafeFileHandle handle)
    {
        byte[] query = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(query, StorageDeviceTemperatureProperty);
        byte[] descriptor = new byte[DescriptorBufferSize];

        return DeviceIoControl(
            handle,
            IoctlStorageQueryProperty,
            query,
            query.Length,
            descriptor,
            descriptor.Length,
            out int bytesReturned,
            IntPtr.Zero) && bytesReturned > 0
            ? WindowsStorageTemperatureParser.GetHighestTemperature(descriptor.AsSpan(0, bytesReturned))
            : null;
    }

    private void DisposeHandles()
    {
        foreach (SafeFileHandle handle in _physicalDriveHandles.Values)
        {
            handle.Dispose();
        }

        _physicalDriveHandles.Clear();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        byte[] inputBuffer,
        int inputBufferSize,
        byte[] outputBuffer,
        int outputBufferSize,
        out int bytesReturned,
        IntPtr overlapped);
}
