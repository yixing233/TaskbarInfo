using System.Drawing;

namespace TaskbarInfo;

public sealed record QuickTranslateLaunchOptions(
    Rectangle ButtonBounds,
    Rectangle TaskbarBounds,
    Rectangle ScreenBounds,
    Rectangle WorkArea)
{
    public static bool TryParse(IEnumerable<string> arguments, out QuickTranslateLaunchOptions options)
    {
        options = null!;
        string[] values = arguments.ToArray();
        if (!values.Contains("--quick-translate", StringComparer.OrdinalIgnoreCase)) return false;

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string argument in values)
        {
            if (!argument.StartsWith("--", StringComparison.Ordinal)) continue;

            string[] parts = argument.Split('=', 2);
            if (parts.Length == 2) fields[parts[0]] = parts[1];
        }

        if (!TryGetRectangle(fields, "anchor", out Rectangle buttonBounds) ||
            !TryGetRectangle(fields, "taskbar", out Rectangle taskbarBounds) ||
            !TryGetRectangle(fields, "screen", out Rectangle screenBounds) ||
            !TryGetRectangle(fields, "work", out Rectangle workArea))
        {
            return false;
        }

        options = new QuickTranslateLaunchOptions(buttonBounds, taskbarBounds, screenBounds, workArea);
        return true;
    }

    private static bool TryGetRectangle(
        IReadOnlyDictionary<string, string> fields,
        string prefix,
        out Rectangle rectangle)
    {
        rectangle = default;
        if (!TryGetInt(fields, $"--{prefix}-left", out int left) ||
            !TryGetInt(fields, $"--{prefix}-top", out int top) ||
            !TryGetInt(fields, $"--{prefix}-width", out int width) ||
            !TryGetInt(fields, $"--{prefix}-height", out int height) ||
            width <= 0 ||
            height <= 0)
        {
            return false;
        }

        rectangle = new Rectangle(left, top, width, height);
        return true;
    }

    private static bool TryGetInt(IReadOnlyDictionary<string, string> fields, string key, out int value)
    {
        value = 0;
        return fields.TryGetValue(key, out string? text) && int.TryParse(text, out value);
    }
}
