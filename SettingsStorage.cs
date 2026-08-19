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

        string newPath = Path.Combine(localApplicationDataPath, "TinyBar", "settings.json");
        string legacyPath1 = Path.Combine(localApplicationDataPath, "taskbarTool", "settings.json");
        string legacyPath2 = Path.Combine(localApplicationDataPath, "TaskbarInfo", "settings.json");

        if (!File.Exists(newPath))
        {
            string? source = File.Exists(legacyPath1) ? legacyPath1 : (File.Exists(legacyPath2) ? legacyPath2 : null);
            if (source != null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
                    File.Copy(source, newPath, overwrite: false);
                }
                catch { }
            }
        }

        return newPath;
    }
}
