using System.Text;
using FormsScreen = System.Windows.Forms.Screen;

namespace TaskbarInfo;

public sealed record TaskbarMonitor(IntPtr TaskbarWindow, string DeviceName);

public static class TaskbarMonitorLocator
{
    public static IntPtr FindTaskbarWindow(string? deviceName)
    {
        IReadOnlyList<TaskbarMonitor> monitors = FindAll();
        return monitors.Count == 0 ? IntPtr.Zero : Select(monitors, deviceName).TaskbarWindow;
    }

    public static TaskbarMonitor Select(IReadOnlyList<TaskbarMonitor> monitors, string? deviceName)
    {
        if (monitors.Count == 0)
        {
            throw new ArgumentException("At least one taskbar monitor is required.", nameof(monitors));
        }

        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            TaskbarMonitor? configured = monitors.FirstOrDefault(monitor =>
                string.Equals(monitor.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
            if (configured != null) return configured;
        }

        return monitors[0];
    }

    private static IReadOnlyList<TaskbarMonitor> FindAll()
    {
        if (!OperatingSystem.IsWindows()) return [];

        var monitors = new List<TaskbarMonitor>();
        AddTaskbar(monitors, UnmanagedMethods.FindWindow("Shell_TrayWnd", null));
        UnmanagedMethods.EnumWindows((window, _) =>
        {
            if (GetClassName(window) == "Shell_SecondaryTrayWnd")
            {
                AddTaskbar(monitors, window);
            }

            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    private static void AddTaskbar(List<TaskbarMonitor> monitors, IntPtr taskbarWindow)
    {
        if (taskbarWindow == IntPtr.Zero) return;

        FormsScreen screen = FormsScreen.FromHandle(taskbarWindow);
        if (monitors.All(monitor => monitor.TaskbarWindow != taskbarWindow))
        {
            monitors.Add(new TaskbarMonitor(taskbarWindow, screen.DeviceName));
        }
    }

    private static string GetClassName(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        return UnmanagedMethods.GetClassName(window, buffer, buffer.Capacity) > 0
            ? buffer.ToString()
            : string.Empty;
    }
}
