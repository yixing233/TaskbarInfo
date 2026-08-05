namespace TaskbarInfo;

public static class TranslationDomainCatalog
{
    public const string General = "通用领域";
    private const int MaximumNameLength = 40;

    public static List<string> Normalize(IEnumerable<string>? domains)
    {
        var normalized = new List<string> { General };
        foreach (string? source in domains ?? [])
        {
            string domain = NormalizeDomain(source);
            if (string.Equals(domain, General, StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains(domain, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            normalized.Add(domain);
        }

        return normalized;
    }

    public static string ResolveSelected(IEnumerable<string>? domains, string? selectedDomain)
    {
        List<string> normalized = Normalize(domains);
        string candidate = NormalizeDomain(selectedDomain);
        return normalized.FirstOrDefault(domain =>
            string.Equals(domain, candidate, StringComparison.OrdinalIgnoreCase)) ?? General;
    }

    public static string NormalizeDomain(string? domain)
    {
        string value = domain?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(value) || value.Length > MaximumNameLength
            ? General
            : value;
    }

    public static bool TryNormalizeCustomDomain(string? domain, out string normalized)
    {
        normalized = domain?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(normalized) &&
            normalized.Length <= MaximumNameLength &&
            !string.Equals(normalized, General, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCustomDomain(string? domain) =>
        !string.Equals(NormalizeDomain(domain), General, StringComparison.OrdinalIgnoreCase);
}
