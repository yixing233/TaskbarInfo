using System;
using System.IO;
using Microsoft.Win32;

namespace TaskbarInfo;

public static class AutoStartupService
{
    public const string StartupKeyName = "TinyBar";
    private static readonly string[] LegacyStartupKeyNames = ["taskbarTool", "TaskbarInfo"];
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string? ResolveExecutablePath()
    {
        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            (string.Equals(Path.GetFileName(processPath), "TinyBar.exe", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(Path.GetFileName(processPath), "taskbarTool.exe", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(Path.GetFileName(processPath), "TaskbarInfo.exe", StringComparison.OrdinalIgnoreCase)) &&
            File.Exists(processPath))
        {
            return Path.GetFullPath(processPath);
        }

        foreach (string name in new[] { "TinyBar.exe", "taskbarTool.exe", "TaskbarInfo.exe" })
        {
            string directCandidate = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(directCandidate))
            {
                return Path.GetFullPath(directCandidate);
            }

            string parentCandidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", name));
            if (File.Exists(parentCandidate))
            {
                return parentCandidate;
            }
        }

        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            return Path.GetFullPath(processPath);
        }

        return null;
    }

    public static string FormatCommandLine(string executablePath)
    {
        string trimmed = executablePath.Trim().Trim('"');
        return $"\"{trimmed}\"";
    }

    public static bool IsAutoStartEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: false);
            if (key == null) return false;

            object? value = key.GetValue(StartupKeyName);
            if (value == null)
            {
                foreach (string legacyName in LegacyStartupKeyNames)
                {
                    value = key.GetValue(legacyName);
                    if (value != null) break;
                }
            }
            if (value is not string command || string.IsNullOrWhiteSpace(command)) return false;

            string? exePath = ResolveExecutablePath();
            if (string.IsNullOrWhiteSpace(exePath)) return true;

            string cleanedCommand = command.Trim().Trim('"');
            return string.Equals(cleanedCommand, exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetAutoStart(bool enable)
    {
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true);
            if (key == null) return false;

            if (enable)
            {
                string? exePath = ResolveExecutablePath();
                if (string.IsNullOrWhiteSpace(exePath)) return false;

                string command = FormatCommandLine(exePath);
                key.SetValue(StartupKeyName, command, RegistryValueKind.String);
                foreach (string legacyName in LegacyStartupKeyNames)
                {
                    if (key.GetValue(legacyName) != null)
                    {
                        key.DeleteValue(legacyName, throwOnMissingValue: false);
                    }
                }
            }
            else
            {
                if (key.GetValue(StartupKeyName) != null)
                {
                    key.DeleteValue(StartupKeyName, throwOnMissingValue: false);
                }
                foreach (string legacyName in LegacyStartupKeyNames)
                {
                    if (key.GetValue(legacyName) != null)
                    {
                        key.DeleteValue(legacyName, throwOnMissingValue: false);
                    }
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Sync(bool enable)
    {
        SetAutoStart(enable);
    }
}
