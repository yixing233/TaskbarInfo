namespace TaskbarInfo;

public sealed record QuickTranslateHotkey(uint Modifiers, uint VirtualKey)
{
    public const uint Alt = 0x0001;
    public const uint Control = 0x0002;
    public const uint Shift = 0x0004;
    public const uint Win = 0x0008;

    public static bool TryParse(string? value, out QuickTranslateHotkey hotkey)
    {
        hotkey = null!;
        if (string.IsNullOrWhiteSpace(value)) return false;

        string[] parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        uint modifiers = 0;
        uint? virtualKey = null;
        foreach (string part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    if ((modifiers & Control) != 0) return false;
                    modifiers |= Control;
                    break;
                case "ALT":
                    if ((modifiers & Alt) != 0) return false;
                    modifiers |= Alt;
                    break;
                case "SHIFT":
                    if ((modifiers & Shift) != 0) return false;
                    modifiers |= Shift;
                    break;
                case "WIN":
                case "WINDOWS":
                    if ((modifiers & Win) != 0) return false;
                    modifiers |= Win;
                    break;
                default:
                    if (virtualKey.HasValue || !TryParseVirtualKey(part, out uint parsedKey)) return false;
                    virtualKey = parsedKey;
                    break;
            }
        }

        if (modifiers == 0 || !virtualKey.HasValue) return false;
        hotkey = new QuickTranslateHotkey(modifiers, virtualKey.Value);
        return true;
    }

    public static bool TryCreate(uint modifiers, uint virtualKey, out QuickTranslateHotkey hotkey)
    {
        hotkey = null!;
        if (modifiers == 0 || !IsSupportedVirtualKey(virtualKey)) return false;

        hotkey = new QuickTranslateHotkey(modifiers, virtualKey);
        return true;
    }

    public string ToDisplayString()
    {
        var parts = new List<string>();
        if ((Modifiers & Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & Alt) != 0) parts.Add("Alt");
        if ((Modifiers & Shift) != 0) parts.Add("Shift");
        if ((Modifiers & Win) != 0) parts.Add("Win");
        parts.Add(FormatVirtualKey(VirtualKey));
        return string.Join('+', parts);
    }

    private static bool TryParseVirtualKey(string value, out uint virtualKey)
    {
        virtualKey = 0;
        string key = value.ToUpperInvariant();
        if (key.Length == 1 && key[0] is >= 'A' and <= 'Z')
        {
            virtualKey = key[0];
            return true;
        }
        if (key.Length == 1 && key[0] is >= '0' and <= '9')
        {
            virtualKey = key[0];
            return true;
        }
        if (key.StartsWith('F') && int.TryParse(key[1..], out int functionKey) && functionKey is >= 1 and <= 12)
        {
            virtualKey = (uint)(0x70 + functionKey - 1);
            return true;
        }
        return false;
    }

    private static bool IsSupportedVirtualKey(uint virtualKey) =>
        virtualKey is >= 'A' and <= 'Z' ||
        virtualKey is >= '0' and <= '9' ||
        virtualKey is >= 0x70 and <= 0x7B;

    private static string FormatVirtualKey(uint virtualKey) => virtualKey switch
    {
        >= 'A' and <= 'Z' => ((char)virtualKey).ToString(),
        >= '0' and <= '9' => ((char)virtualKey).ToString(),
        >= 0x70 and <= 0x7B => "F" + (virtualKey - 0x70 + 1),
        _ => string.Empty
    };
}
