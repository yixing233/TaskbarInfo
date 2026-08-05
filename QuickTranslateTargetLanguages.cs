using System;
using System.Collections.Generic;

namespace TaskbarInfo;

public static class QuickTranslateTargetLanguages
{
    public const string Default = "zh-CN";

    private static readonly HashSet<string> Supported =
    [
        "zh-CN",
        "en",
        "ja",
        "ko"
    ];

    public static string Normalize(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Supported.Contains(value)
            ? value
            : Default;
}
