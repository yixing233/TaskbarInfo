using TaskbarInfo;

var tests = new (string Name, Action Test)[]
{
    ("Clone copies app filter list independently", CloneCopiesAppFilterListIndependently),
};

var failures = new List<string>();

foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: {ex.Message}");
        Console.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"{failures.Count} test(s) failed.");
    Environment.Exit(1);
}

static void CloneCopiesAppFilterListIndependently()
{
    var original = new AppSettings();
    original.IncludedAppIds.Add("Spotify");

    var clone = original.Clone();
    original.IncludedAppIds.Add("QQMusic");

    AssertEqual(1, clone.IncludedAppIds.Count, "clone should keep the original app filter snapshot");
    AssertEqual("Spotify", clone.IncludedAppIds[0], "clone should preserve existing app id");
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message}. Expected {expected}, got {actual}.");
    }
}
