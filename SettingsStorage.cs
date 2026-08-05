using System.IO;

namespace TaskbarInfo;

public static class SettingsStorage
{
    public static string CurrentPath => GetPath(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public static string GetPath(string localApplicationDataPath)
    {
        if (string.IsNullOrWhiteSpace(localApplicationDataPath))
        {
            throw new ArgumentException("A local application data path is required.", nameof(localApplicationDataPath));
        }

        return Path.Combine(localApplicationDataPath, "TaskbarInfo", "settings.json");
    }
}
