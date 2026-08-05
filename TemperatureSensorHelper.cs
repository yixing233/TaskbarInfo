using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;

namespace TaskbarInfo;

public static class TemperatureSensorHelper
{
    public static bool TryRun(string[] args)
    {
        if (!args.Contains("--temperature-helper", StringComparer.Ordinal)) return false;
        string? pipe = args.FirstOrDefault(value => value.StartsWith("--pipe=", StringComparison.Ordinal))?[7..];
        string? token = args.FirstOrDefault(value => value.StartsWith("--token=", StringComparison.Ordinal))?[8..];
        string? parentText = args.FirstOrDefault(value => value.StartsWith("--parent-pid=", StringComparison.Ordinal))?[13..];
        if (string.IsNullOrWhiteSpace(pipe) || string.IsNullOrWhiteSpace(token) || !int.TryParse(parentText, out int parentPid)) return true;

        Run(pipe, token, parentPid);
        return true;
    }

    private static void Run(string pipeName, string token, int parentPid)
    {
        var computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsStorageEnabled = true, IsControllerEnabled = true };
        computer.Open();
        try
        {
            while (IsParentAlive(parentPid))
            {
                using var server = CreateServer(pipeName);
                try { server.WaitForConnectionAsync().Wait(TimeSpan.FromSeconds(15)); } catch { return; }
                if (!server.IsConnected) return;
                using var reader = new StreamReader(server, leaveOpen: true);
                using var writer = new StreamWriter(server) { AutoFlush = true };
                try
                {
                    string? requestJson = reader.ReadLine();
                    var request = requestJson == null ? null : JsonSerializer.Deserialize<TemperatureHelperRequest>(requestJson);
                    if (request == null || !TemperatureHelperProtocol.HasValidToken(token, request.Token)) return;
                    writer.WriteLine(JsonSerializer.Serialize(ReadSnapshot(computer)));
                }
                catch { return; }
            }
        }
        finally { computer.Close(); }
    }

    private static TemperatureHelperResponse ReadSnapshot(Computer computer)
    {
        double? cpu = null, gpu = null, disk = null;
        foreach (IHardware hardware in computer.Hardware) ReadHardware(hardware, ref cpu, ref gpu, ref disk);
        return new TemperatureHelperResponse(cpu, gpu, disk);
    }

    private static void ReadHardware(IHardware hardware, ref double? cpu, ref double? gpu, ref double? disk)
    {
        hardware.Update();
        foreach (ISensor sensor in hardware.Sensors)
        {
            float? value = sensor.Value;
            if (sensor.SensorType != SensorType.Temperature || !value.HasValue || !float.IsFinite(value.Value) || value.Value < 0 || value.Value > 150) continue;
            if (hardware.HardwareType == HardwareType.Cpu) cpu = Math.Max(cpu ?? value.Value, value.Value);
            else if (hardware.HardwareType is HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia) gpu = Math.Max(gpu ?? value.Value, value.Value);
            else if (hardware.HardwareType == HardwareType.Storage) disk = Math.Max(disk ?? value.Value, value.Value);
        }
        foreach (IHardware child in hardware.SubHardware) ReadHardware(child, ref cpu, ref gpu, ref disk);
    }

    private static bool IsParentAlive(int processId)
    {
        try { return !Process.GetProcessById(processId).HasExited; } catch { return false; }
    }

    private static NamedPipeServerStream CreateServer(string pipeName)
    {
        var security = new PipeSecurity();
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current user SID is unavailable.");
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.None,
            0,
            0,
            security);
    }
}

public sealed class TemperatureSensorHelperClient : IDisposable
{
    private readonly string _pipeName = $"TaskbarInfo.Temperature.{Environment.ProcessId}.{Guid.NewGuid():N}";
    private readonly string _token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private bool _started;

    public TaskbarTemperatureSnapshot Read(bool enabled)
    {
        if (!enabled || !OperatingSystem.IsWindows()) return TaskbarTemperatureSnapshot.Empty;
        if (!_started && !Start()) return TaskbarTemperatureSnapshot.Empty;
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
            client.Connect(300);
            using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, leaveOpen: true);
            writer.WriteLine(JsonSerializer.Serialize(new TemperatureHelperRequest(_token)));
            string? responseJson = reader.ReadLine();
            var response = responseJson == null ? null : JsonSerializer.Deserialize<TemperatureHelperResponse>(responseJson);
            return response == null ? TaskbarTemperatureSnapshot.Empty : new(response.CpuTemperatureCelsius, response.GpuTemperatureCelsius, response.DiskTemperatureCelsius);
        }
        catch { return TaskbarTemperatureSnapshot.Empty; }
    }

    private bool Start()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true, Verb = "runas",
                Arguments = $"--temperature-helper --pipe={_pipeName} --token={_token} --parent-pid={Environment.ProcessId}",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            _started = true;
            return true;
        }
        catch { return false; }
    }

    public void Dispose() { }
}
