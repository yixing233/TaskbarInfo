namespace TaskbarInfo;

public sealed class TranslationProviderProfile
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Provider { get; set; } = "";
    public string AppId { get; set; } = "";
    public string AppSecret { get; set; } = "";
    public string ExtraCredential { get; set; } = "";
    public string ApiBaseUrl { get; set; } = "";
}

public sealed record TranslationProviderDefinition(
    string Id,
    string DisplayName,
    string ApiBaseUrl,
    string AppIdLabel,
    string AppSecretLabel,
    string ExtraCredentialLabel,
    string DefaultExtraCredential = "");

public static class TranslationProviderProfiles
{
    private static readonly IReadOnlyList<TranslationProviderDefinition> ProviderDefinitions =
    [
        new("Baidu", "百度翻译", "https://fanyi-api.baidu.com/api/trans/vip/translate", "App ID", "API Secret", ""),
        new("Youdao", "有道智云", "https://openapi.youdao.com/api", "App Key", "App Secret", ""),
        new("Google", "Google Cloud Translation", "https://translation.googleapis.com/language/translate/v2", "API Key", "", ""),
        new("DeepL", "DeepL", "https://api-free.deepl.com/v2/translate", "Auth Key", "", ""),
        new("Azure", "Microsoft Azure Translator", "https://api.cognitive.microsofttranslator.com/translate", "Subscription Key", "", "资源区域（可选）"),
        new("Tencent", "腾讯翻译君", "https://tmt.tencentcloudapi.com", "SecretId", "SecretKey", "区域", "ap-guangzhou"),
        new("Alibaba", "阿里云机器翻译", "https://mt.cn-hangzhou.aliyuncs.com", "AccessKey ID", "AccessKey Secret", ""),
        new("Volcengine", "火山翻译", "https://translate.volcengineapi.com", "Access Key", "Secret Key", "区域", "cn-north-1"),
        new("Huawei", "华为云机器翻译", "https://nlp-ext.cn-north-4.myhuaweicloud.com", "Access Key", "Secret Key", "项目 ID"),
        new("iFlytek", "讯飞翻译", "https://itrans.xfyun.cn/v2/its", "APPID", "API Secret", "API Key"),
        new("OpenAI", "OpenAI", "https://api.openai.com/v1/chat/completions", "API Key", "", "模型 ID", "gpt-4o-mini"),
        new("DeepSeek", "DeepSeek", "https://api.deepseek.com/chat/completions", "API Key", "", "模型 ID", "deepseek-chat"),
        new("Qwen", "通义千问", "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions", "API Key", "", "模型 ID", "qwen-turbo"),
        new("SiliconFlow", "硅基流动", "https://api.siliconflow.cn/v1/chat/completions", "API Key", "", "模型 ID", "Qwen/Qwen2.5-7B-Instruct"),
        new("OpenAICompatible", "OpenAI 兼容 AI", "https://api.openai.com/v1/chat/completions", "API Key", "", "模型 ID", "gpt-4o-mini"),
        new("Ollama", "Ollama 本地模型", "http://localhost:11434/api/chat", "", "", "模型 ID", "qwen2.5:7b")
    ];

    public static IReadOnlyList<TranslationProviderDefinition> Definitions => ProviderDefinitions;
    public static IReadOnlyList<string> BuiltInProviders => ProviderDefinitions.Select(definition => definition.Id).ToArray();
    public static List<TranslationProviderProfile> Normalize(
        IEnumerable<TranslationProviderProfile>? profiles,
        string? legacyProvider,
        string? legacyBaiduAppId,
        string? legacyBaiduAppSecret,
        string? legacyYoudaoAppKey,
        string? legacyYoudaoAppSecret)
    {
        var normalized = new List<TranslationProviderProfile>();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (TranslationProviderProfile source in profiles ?? [])
        {
            if (IsEmptyDraft(source))
            {
                normalized.Add(new TranslationProviderProfile());
                continue;
            }

            string provider = NormalizeProvider(source.Provider);
            string id = source.Id?.Trim() ?? string.Empty;
            if (!IsValidId(id) || !usedIds.Add(id))
            {
                id = CreateUniqueId(provider, usedIds);
            }

            string displayName = string.IsNullOrWhiteSpace(source.DisplayName)
                ? GetDefaultDisplayName(provider)
                : source.DisplayName.Trim();
            normalized.Add(new TranslationProviderProfile
            {
                Id = id,
                DisplayName = displayName,
                Provider = provider,
                AppId = source.AppId ?? string.Empty,
                AppSecret = source.AppSecret ?? string.Empty,
                ExtraCredential = source.ExtraCredential ?? string.Empty,
                ApiBaseUrl = NormalizeApiBaseUrl(source.ApiBaseUrl, provider)
            });
        }

        if (normalized.Count > 0) return normalized;

        string legacyKind = NormalizeProvider(legacyProvider);
        return
        [
            new TranslationProviderProfile
            {
                Id = CreateUniqueId(legacyKind, usedIds),
                DisplayName = GetDefaultDisplayName(legacyKind),
                Provider = legacyKind,
                AppId = legacyKind == "Youdao" ? legacyYoudaoAppKey ?? string.Empty : legacyBaiduAppId ?? string.Empty,
                AppSecret = legacyKind == "Youdao" ? legacyYoudaoAppSecret ?? string.Empty : legacyBaiduAppSecret ?? string.Empty,
                ApiBaseUrl = GetDefaultApiBaseUrl(legacyKind)
            }
        ];
    }

    public static TranslationProviderProfile CreateNew(
        IEnumerable<TranslationProviderProfile>? existing,
        string? provider = null)
    {
        string normalizedProvider = NormalizeProvider(provider);
        if (string.IsNullOrEmpty(normalizedProvider)) return new TranslationProviderProfile();

        var usedIds = new HashSet<string>(
            existing?.Select(profile => profile.Id).Where(IsValidId) ?? [],
            StringComparer.OrdinalIgnoreCase);
        return new TranslationProviderProfile
        {
            Id = CreateUniqueId(normalizedProvider, usedIds),
            DisplayName = GetDefaultDisplayName(normalizedProvider),
            Provider = normalizedProvider,
            ApiBaseUrl = GetDefaultApiBaseUrl(normalizedProvider),
            ExtraCredential = GetDefaultExtraCredential(normalizedProvider)
        };
    }

    public static string ResolveSelectedId(IEnumerable<TranslationProviderProfile>? profiles, string? selectedId)
    {
        var list = profiles?.ToList() ?? [];
        return list.FirstOrDefault(profile => string.Equals(profile.Id, selectedId, StringComparison.OrdinalIgnoreCase))?.Id
            ?? list.FirstOrDefault()?.Id
            ?? string.Empty;
    }

    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) return false;
        return id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    }

    public static bool HasUniqueIds(IEnumerable<TranslationProviderProfile>? profiles)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TranslationProviderProfile profile in profiles ?? [])
        {
            if (IsEmptyDraft(profile)) continue;
            if (!IsValidId(profile.Id) || !ids.Add(profile.Id)) return false;
        }

        return true;
    }

    public static bool IsEmptyDraft(TranslationProviderProfile profile) =>
        string.IsNullOrWhiteSpace(profile.Id) &&
        string.IsNullOrWhiteSpace(profile.DisplayName) &&
        string.IsNullOrWhiteSpace(profile.Provider) &&
        string.IsNullOrWhiteSpace(profile.AppId) &&
        string.IsNullOrWhiteSpace(profile.AppSecret) &&
        string.IsNullOrWhiteSpace(profile.ExtraCredential) &&
        string.IsNullOrWhiteSpace(profile.ApiBaseUrl);

    public static string NormalizeProvider(string? provider) => FindDefinition(provider)?.Id ?? "";

    public static string GetDefaultDisplayName(string? provider) => FindDefinition(provider)?.DisplayName ?? "";

    public static string GetDefaultApiBaseUrl(string? provider) => FindDefinition(provider)?.ApiBaseUrl ?? "";

    public static string GetAppIdLabel(string? provider) => FindDefinition(provider)?.AppIdLabel ?? "API Key / App ID";

    public static string GetAppSecretLabel(string? provider) => FindDefinition(provider)?.AppSecretLabel ?? "API Secret";

    public static string GetExtraCredentialLabel(string? provider) => FindDefinition(provider)?.ExtraCredentialLabel ?? "";

    public static string GetDefaultExtraCredential(string? provider) => FindDefinition(provider)?.DefaultExtraCredential ?? "";

    public static string NormalizeApiBaseUrl(string? value, string? provider)
    {
        string candidate = value?.Trim() ?? string.Empty;
        if (IsValidApiBaseUrl(candidate))
        {
            return candidate;
        }

        return GetDefaultApiBaseUrl(provider);
    }

    public static bool IsValidApiBaseUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string CreateUniqueId(string provider, ISet<string> usedIds)
    {
        string baseId = NormalizeProvider(provider).ToLowerInvariant();
        if (string.IsNullOrEmpty(baseId)) baseId = "provider";
        for (int suffix = 1; ; suffix++)
        {
            string candidate = suffix == 1 ? baseId : $"{baseId}-{suffix}";
            if (usedIds.Add(candidate)) return candidate;
        }
    }

    private static TranslationProviderDefinition? FindDefinition(string? provider) =>
        ProviderDefinitions.FirstOrDefault(definition => string.Equals(
            definition.Id,
            provider?.Trim(),
            StringComparison.OrdinalIgnoreCase));
}
