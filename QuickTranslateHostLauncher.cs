using System.Text;

namespace TaskbarInfo;

public static class QuickTranslateHostLauncher
{
    public static string BuildArguments(string settingsPath, QuickTranslateLaunchOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);

        var arguments = new StringBuilder();
        arguments.Append('"').Append(settingsPath.Replace("\"", "\\\"")).Append('"');
        arguments.Append(" --quick-translate");
        AppendRectangle(arguments, "anchor", options.ButtonBounds);
        AppendRectangle(arguments, "taskbar", options.TaskbarBounds);
        AppendRectangle(arguments, "screen", options.ScreenBounds);
        AppendRectangle(arguments, "work", options.WorkArea);
        return arguments.ToString();
    }

    private static void AppendRectangle(StringBuilder arguments, string name, System.Drawing.Rectangle rectangle)
    {
        arguments.Append($" --{name}-left={rectangle.Left}");
        arguments.Append($" --{name}-top={rectangle.Top}");
        arguments.Append($" --{name}-width={rectangle.Width}");
        arguments.Append($" --{name}-height={rectangle.Height}");
    }
}
