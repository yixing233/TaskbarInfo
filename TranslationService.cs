using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TaskbarInfo;

public static class TranslationService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static Task<string> TranslateAsync(
        string text,
        string targetLanguage,
        TranslationConfiguration configuration,
        CancellationToken cancellationToken) => TranslationProviderProfiles.NormalizeProvider(configuration.Provider) switch
        {
            "Baidu" => TranslateWithBaiduAsync(text, targetLanguage, configuration, cancellationToken),
            "Youdao" => TranslateWithYoudaoAsync(text, targetLanguage, configuration, cancellationToken),
            "Google" => TranslateWithGoogleAsync(text, targetLanguage, configuration, cancellationToken),
            "DeepL" => TranslateWithDeepLAsync(text, targetLanguage, configuration, cancellationToken),
            "Azure" => TranslateWithAzureAsync(text, targetLanguage, configuration, cancellationToken),
            "Tencent" => TranslateWithTencentAsync(text, targetLanguage, configuration, cancellationToken),
            "Alibaba" => TranslateWithAlibabaAsync(text, targetLanguage, configuration, cancellationToken),
            "Volcengine" => TranslateWithVolcengineAsync(text, targetLanguage, configuration, cancellationToken),
            "Huawei" => TranslateWithHuaweiAsync(text, targetLanguage, configuration, cancellationToken),
            "iFlytek" => TranslateWithIFlytekAsync(text, targetLanguage, configuration, cancellationToken),
            "OpenAI" or "DeepSeek" or "Qwen" or "SiliconFlow" or "OpenAICompatible" =>
                TranslateWithOpenAICompatibleAsync(text, targetLanguage, configuration, cancellationToken),
            "Ollama" => TranslateWithOllamaAsync(text, targetLanguage, configuration, cancellationToken),
            _ => Task.FromException<string>(new InvalidOperationException("请先选择受支持的翻译服务类型。"))
        };

    public static bool IsSupportedProvider(string? provider) =>
        !string.IsNullOrEmpty(TranslationProviderProfiles.NormalizeProvider(provider));

    public static bool IsAiProvider(string? provider) => TranslationProviderProfiles.NormalizeProvider(provider) is
        "OpenAI" or "DeepSeek" or "Qwen" or "SiliconFlow" or "OpenAICompatible" or "Ollama";

    public static async Task<IReadOnlyList<string>> GetAvailableModelsAsync(
        TranslationConfiguration configuration,
        CancellationToken cancellationToken)
    {
        string provider = TranslationProviderProfiles.NormalizeProvider(configuration.Provider);
        if (!IsAiProvider(provider))
        {
            throw new InvalidOperationException("当前服务商不支持获取 AI 模型列表。" );
        }

        bool isOllama = provider == "Ollama";
        if (!isOllama)
        {
            RequireCredentials(configuration, TranslationProviderProfiles.GetDefaultDisplayName(provider),
                ("API Key", configuration.AppId));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, GetModelListEndpoint(configuration.ApiBaseUrl, isOllama));
        if (!isOllama)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.AppId.Trim());
        }

        using HttpResponseMessage response = await Client.SendAsync(request, cancellationToken);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"获取模型列表失败（HTTP {(int)response.StatusCode}）：{ExtractErrorMessage(payload)}");
        }

        return isOllama ? ExtractOllamaModelIds(payload) : ExtractOpenAICompatibleModelIds(payload);
    }

    public static string GetModelListEndpoint(string apiBaseUrl, bool isOllama)
    {
        Uri endpoint = new(apiBaseUrl);
        string sourceSuffix = isOllama ? "/api/chat" : "/chat/completions";
        string targetSuffix = isOllama ? "/api/tags" : "/models";
        string path = endpoint.AbsolutePath.TrimEnd('/');
        int suffixIndex = path.LastIndexOf(sourceSuffix, StringComparison.OrdinalIgnoreCase);
        path = suffixIndex >= 0
            ? path[..suffixIndex] + targetSuffix
            : path + targetSuffix;

        var builder = new UriBuilder(endpoint) { Path = path, Query = string.Empty };
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    public static string CreateBaiduSignature(string appId, string text, string salt, string appSecret) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(appId + text + salt + appSecret))).ToLowerInvariant();

    private static async Task<string> TranslateWithBaiduAsync(string text, string targetLanguage, TranslationConfiguration configuration, CancellationToken cancellationToken)
    {
        RequireCredentials(configuration, "百度翻译", ("App ID", configuration.AppId), ("API Secret", configuration.AppSecret));
        string salt = RandomNumberGenerator.GetInt32(100000, 999999).ToString(CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Post, TranslationProviderProfiles.NormalizeApiBaseUrl(configuration.ApiBaseUrl, "Baidu"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["q"] = text,
                ["from"] = "auto",
                ["to"] = ToBaiduTargetLanguage(targetLanguage),
                ["appid"] = configuration.AppId,
                ["salt"] = salt,
                ["sign"] = CreateBaiduSignature(configuration.AppId, text, salt, configuration.AppSecret)
            })
        };
        return await SendAsync(request, "百度翻译", ExtractBaiduTranslatedText, cancellationToken);
    }

    private static async Task<string> TranslateWithYoudaoAsync(string text, string targetLanguage, TranslationConfiguration configuration, CancellationToken cancellationToken)
    {
        RequireCredentials(configuration, "有道智云", ("App Key", configuration.AppId), ("App Secret", configuration.AppSecret));
        string salt = Guid.NewGuid().ToString("N");
        string currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        string input = TruncateYoudaoInput(text);
        string sign = Sha256Hex(configuration.AppId + input + salt + currentTime + configuration.AppSecret);
        using var request = new HttpRequestMessage(HttpMethod.Post, TranslationProviderProfiles.NormalizeApiBaseUrl(configuration.ApiBaseUrl, "Youdao"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["q"] = text,
                ["from"] = "auto",
                ["to"] = ToYoudaoTargetLanguage(targetLanguage),
                ["appKey"] = configuration.AppId,
                ["salt"] = salt,
                ["sign"] = sign,
                ["signType"] = "v3",
                ["curtime"] = currentTime
            })
        };
        return await SendAsync(request, "有道智云", ExtractYoudaoTranslatedText, cancellationToken);
    }

    private static async Task<string> TranslateWithGoogleAsync(string text, string targetLanguage, TranslationConfiguration configuration, CancellationToken cancellationToken)
    {
        RequireCredentials(configuration, "Google Cloud Translation", ("API Key", configuration.AppId));
        Uri endpoint = AppendQuery(TranslationProviderProfiles.NormalizeApiBaseUrl(configuration.ApiBaseUrl, "Google"), new Dictionary<string, string> { ["key"] = configuration.AppId });
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["q"] = text,
                ["source"] = "auto",
                ["target"] = ToGoogleTargetLanguage(targetLanguage)
            })
        };
        return await SendAsync(request, "Google Cloud Translation", ExtractGoogleTranslatedText, cancellationToken);
    }

    private static async Task<string> TranslateWithDeepLAsync(string text, string targetLanguage, TranslationConfiguration configuration, CancellationToken cancellationToken)
    {
        RequireCredentials(configuration, "DeepL", ("Auth Key", configuration.AppId));
        using var request = new HttpRequestMessage(HttpMethod.Post, TranslationProviderProfiles.NormalizeApiBaseUrl(configuration.ApiBaseUrl, "DeepL"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["text"] = text,
                ["target_lang"] = ToDeepLTargetLanguage(targetLanguage)
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", configuration.AppId);
        return await SendAsync(request, "DeepL", ExtractDeepLTranslatedText, cancellationToken);
    }

    private static async Task<string> TranslateWithAzureAsync(string text, string targetLanguage, TranslationConfiguration configuration, CancellationToken cancellationToken)
    {
        RequireCredentials(configuration, "Microsoft Azure Translator", ("Subscription Key", configuration.AppId));
        Uri endpoint = AppendQuery(TranslationProviderProfiles.NormalizeApiBaseUrl(configuration.ApiBaseUrl, "Azure"), new Dictionary<string, string>
        {
            ["api-version"] = "3.0",
            ["from"] = "auto",
            ["to"] = ToAzureTargetLanguage(targetLanguage)
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(new[] { new { Text = text } }), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Ocp-Apim-Subscription-Key", configuration.AppId);
        if (!string.IsNullOrWhiteSpace(configuration.ExtraCredential))
        {
            request.Headers.Add("Ocp-Apim-Subscription-Region", configuration.ExtraCredential.Trim());
        }
        return await SendAsync(request, "Microsoft Azure Translator", ExtractAzureTranslatedText, cancellationToken);
    }

    private static async Task<string> TranslateWithTencentAsync(string text, string targetLanguage, TranslationConfiguration configuration, CancellationToken cancellationToken)
    {
        RequireCredentials(configuration, "腾讯翻译君", ("SecretId", configuration.AppId), ("SecretKey", configuration.AppSecret));
        string body = JsonSerializer.Serialize(new { SourceText = text, Source = "auto", Target = ToTencentTargetLanguage(targetLanguage), ProjectId = 0 });
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string region = string.IsNullOrWhiteSpace(configuration.ExtraCredential) ? "ap-guangzhou" : configuration.ExtraCredential.Trim();
        string endpoint = TranslationProviderProfiles.NormalizeApiBaseUrl(configuration.ApiBaseUrl, "Tencent");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Authorization", CreateTencentAuthorization(configuration.AppId, configuration.AppSecret, timestamp, body, new Uri(endpoint).Host));
        request.Headers.Add("X-TC-Action", "TextTranslate");
        request.Headers.Add("X-TC-Version", "2018-03-21");
        request.Headers.Add("X-TC-Timestamp", timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("X-TC-Region", region);
        return await SendAsync(request, "腾讯翻译君", ExtractTencentTranslatedText, cancellationToken);
    }

    private static async Task<string> TranslateWithAlibabaAsync(string text, string targetLanguage, TranslationConfiguration configuration, CancellationToken cancellationToken)
    {
        RequireCredentials(configuration, "阿里云机器翻译", ("AccessKey ID", configuration.AppId), ("AccessKey Secret", configuration.AppSecret));
        var parameters = new Dictionary<string, string>
        {
            ["Action"] = "TranslateGeneral",
            ["Version"] = "2018-10-12",
            ["Format"] = "JSON",
            ["SourceLanguage"] = "auto",
            ["TargetLanguage"] = ToAlibabaTargetLanguage(targetLanguage),
            ["SourceText"] = text,
            ["Scene"] = "general",
            ["AccessKeyId"] = configuration.AppId,
            ["SignatureMethod"] = "HMAC-SHA1",
            ["SignatureNonce"] = Guid.NewGuid().ToString("N"),
            ["SignatureVersion"] = "1.0",
            ["Timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        parameters["Signature"] = CreateAlibabaSignature(configuration.AppSecret, parameters);
        using var request = new HttpRequestMessage(HttpMethod.Get, AppendQuery(TranslationProviderProfiles.NormalizeApiBaseUrl(configuration.ApiBaseUrl, "Alibaba"), parameters));
        return await SendAsync(request, "阿里云机器翻译", ExtractAlibabaTranslatedText, cancellationToken);
    }

    private static async Task<string> TranslateWithVolcengineAsync(string text, string targetLanguage, TranslationConfiguration configuration, CancellationToken cancellationToken)
    {
        RequireCredentials(configuration, "火山翻译", ("Access Key", configuration.AppId), ("Secret Key", configuration.AppSecret));
        const string query = "Action=TranslateText&Version=2020-06-01";
        string body = JsonSerializer.Serialize(new { SourceLanguage = "auto", TargetLanguage = ToVolcengineTargetLanguage(targetLanguage), TextList = new[] { text } });
        string endpoint = TranslationProviderProfiles.NormalizeApiBaseUrl(configuration.ApiBaseUrl, "Volcengine");
        string region = string.IsNullOrWhiteSpace(configuration.ExtraCredential) ? "cn-north-1" : configuration.ExtraCredential.Trim();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.TrimEnd('/') + "/?" + query)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        string payloadHash = Sha256Hex(body);
        string date = now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        string host = new Uri(endpoint).Host;
        request.Headers.Add("X-Date", date);
        request.Headers.Add("X-Content-Sha256", payloadHash);
        request.Headers.Add("Authorization", CreateVolcengineAuthorization(configuration.AppId, configuration.AppSecret, region, host, query, payloadHash, date));
        return await SendAsync(request, "火山翻译", ExtractVolcengineTranslatedText, cancellationToken);
    }

    private static async Task<string> TranslateWithHuaweiAsync(string text, string targetLanguage, TranslationConfiguration configuration, CancellationToken cancellationToken)
    {
        RequireCredentials(configuration, "华为云机器翻译", ("Access Key", configuration.AppId), ("Secret Key", configuration.AppSecret), ("Project ID", configuration.ExtraCredential));
        string endpoint = TranslationProviderProfiles.NormalizeApiBaseUrl(configuration.ApiBaseUrl, "Huawei").TrimEnd('/');
        string path = "/v1/" + Uri.EscapeDataString(configuration.ExtraCredential.Trim()) + "/machine-translation/text";
        string body = JsonSerializer.Serialize(new { from = "auto", to = ToHuaweiTargetLanguage(targetLanguage), text });
        string date = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        string host = new Uri(endpoint).Host;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint + path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Sdk-Date", date);
        request.Headers.Add("Authorization", CreateHuaweiAuthorization(configuration.AppId, configuration.AppSecret, host, path, body, date));
        return await SendAsync(request, "华为云机器翻译", ExtractHuaweiTranslatedText, cancellationToken);
    }

    private static async Task<string> TranslateWithIFlytekAsync(string text, string targetLanguage, TranslationConfiguration configuration, CancellationToken cancellationToken)
    {
        RequireCredentials(configuration, "讯飞翻译", ("APPID", configuration.AppId), ("API Secret", configuration.AppSecret), ("API Key", configuration.ExtraCredential));
        string endpoint = TranslationProviderProfiles.NormalizeApiBaseUrl(configuration.ApiBaseUrl, "iFlytek");
        Uri uri = new(endpoint);
        string date = DateTimeOffset.UtcNow.ToString("r", CultureInfo.InvariantCulture);
        string authorization = CreateIFlytekAuthorization(configuration.ExtraCredential, configuration.AppSecret, uri.Host, uri.AbsolutePath, date);
        string body = JsonSerializer.Serialize(new
        {
            common = new { app_id = configuration.AppId },
            business = new { from = "auto", to = ToIFlytekTargetLanguage(targetLanguage) },
            data = new { text = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Date", date);
        request.Headers.Add("Host", uri.Host);
        request.Headers.Add("Authorization", authorization);
        return await SendAsync(request, "讯飞翻译", ExtractIFlytekTranslatedText, cancellationToken);
    }

    private static async Task<string> TranslateWithOpenAICompatibleAsync(string text, string targetLanguage, TranslationConfiguration configuration, CancellationToken cancellationToken)
    {
        RequireCredentials(configuration, TranslationProviderProfiles.GetDefaultDisplayName(configuration.Provider),
            ("API Key", configuration.AppId), ("模型 ID", configuration.ExtraCredential));
        using var request = new HttpRequestMessage(HttpMethod.Post, configuration.ApiBaseUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                model = configuration.ExtraCredential.Trim(),
                temperature = 0.2,
                messages = CreateAiMessages(text, targetLanguage)
            }), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.AppId.Trim());
        return await SendAsync(request, TranslationProviderProfiles.GetDefaultDisplayName(configuration.Provider),
            ExtractOpenAICompatibleTranslatedText, cancellationToken);
    }

    private static async Task<string> TranslateWithOllamaAsync(string text, string targetLanguage, TranslationConfiguration configuration, CancellationToken cancellationToken)
    {
        RequireCredentials(configuration, "Ollama 本地模型", ("模型 ID", configuration.ExtraCredential));
        using var request = new HttpRequestMessage(HttpMethod.Post, configuration.ApiBaseUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                model = configuration.ExtraCredential.Trim(),
                stream = false,
                messages = CreateAiMessages(text, targetLanguage)
            }), Encoding.UTF8, "application/json")
        };
        return await SendAsync(request, "Ollama 本地模型", ExtractOllamaTranslatedText, cancellationToken);
    }

    private static async Task<string> SendAsync(HttpRequestMessage request, string provider, Func<string, string> parser, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Client.SendAsync(request, cancellationToken);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{provider}请求失败（HTTP {(int)response.StatusCode}）：{ExtractErrorMessage(payload)}");
        }
        return parser(payload);
    }

    public static string ExtractBaiduTranslatedText(string response)
    {
        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement root = document.RootElement;
        ThrowIfError(root, "百度翻译");
        return ConcatenateArrayStrings(root, "trans_result", "dst");
    }

    public static string ExtractYoudaoTranslatedText(string response)
    {
        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("errorCode", out JsonElement errorCode) && errorCode.GetString() != "0")
        {
            throw new InvalidOperationException("有道智云翻译请求失败，错误码：" + errorCode.GetString());
        }
        return ConcatenateArrayStrings(root, "translation", null);
    }

    public static string ExtractGoogleTranslatedText(string response)
    {
        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement root = document.RootElement;
        ThrowIfError(root, "Google Cloud Translation");
        return root.GetProperty("data").GetProperty("translations").EnumerateArray()
            .Select(item => item.GetProperty("translatedText").GetString() ?? string.Empty).Aggregate(string.Concat);
    }

    public static string ExtractDeepLTranslatedText(string response)
    {
        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement root = document.RootElement;
        ThrowIfError(root, "DeepL");
        return root.GetProperty("translations").EnumerateArray()
            .Select(item => item.GetProperty("text").GetString() ?? string.Empty).Aggregate(string.Concat);
    }

    public static string ExtractAzureTranslatedText(string response)
    {
        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return string.Empty;
        return root[0].GetProperty("translations").EnumerateArray()
            .Select(item => item.GetProperty("text").GetString() ?? string.Empty).Aggregate(string.Concat);
    }

    public static string ExtractTencentTranslatedText(string response)
    {
        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("Response", out JsonElement result))
        {
            ThrowIfError(result, "腾讯翻译君");
            return result.TryGetProperty("TargetText", out JsonElement text) ? text.GetString() ?? string.Empty : string.Empty;
        }
        ThrowIfError(root, "腾讯翻译君");
        return string.Empty;
    }

    public static string ExtractAlibabaTranslatedText(string response)
    {
        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement root = document.RootElement;
        ThrowIfError(root, "阿里云机器翻译");
        return root.TryGetProperty("Data", out JsonElement data) && data.TryGetProperty("Translated", out JsonElement text)
            ? text.GetString() ?? string.Empty
            : string.Empty;
    }

    public static string ExtractVolcengineTranslatedText(string response)
    {
        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement root = document.RootElement;
        ThrowIfError(root, "火山翻译");
        return ConcatenateArrayStrings(root, "TranslationList", "Translation");
    }

    public static string ExtractHuaweiTranslatedText(string response)
    {
        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement root = document.RootElement;
        ThrowIfError(root, "华为云机器翻译");
        return FindText(root, "translated_text") ?? FindText(root, "translatedText") ?? string.Empty;
    }

    public static string ExtractIFlytekTranslatedText(string response)
    {
        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement root = document.RootElement;
        if (root.TryGetProperty("code", out JsonElement code) && code.GetInt32() != 0)
        {
            throw new InvalidOperationException("讯飞翻译请求失败：" + (FindText(root, "message") ?? code.ToString()));
        }
        string? text = FindText(root, "dst") ?? FindText(root, "text");
        if (string.IsNullOrEmpty(text)) return string.Empty;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(text)); }
        catch (FormatException) { return text; }
    }

    public static string ExtractOpenAICompatibleTranslatedText(string response)
    {
        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement root = document.RootElement;
        ThrowIfError(root, "AI 模型");
        if (!root.TryGetProperty("choices", out JsonElement choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            return string.Empty;
        }
        return choices[0].TryGetProperty("message", out JsonElement message) && message.TryGetProperty("content", out JsonElement content)
            ? content.GetString() ?? string.Empty
            : string.Empty;
    }

    public static string ExtractOllamaTranslatedText(string response)
    {
        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement root = document.RootElement;
        ThrowIfError(root, "Ollama 本地模型");
        return root.TryGetProperty("message", out JsonElement message) && message.TryGetProperty("content", out JsonElement content)
            ? content.GetString() ?? string.Empty
            : string.Empty;
    }

    public static IReadOnlyList<string> ExtractOpenAICompatibleModelIds(string response)
    {
        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement root = document.RootElement;
        ThrowIfError(root, "AI 模型列表");
        if (!root.TryGetProperty("data", out JsonElement models) || models.ValueKind != JsonValueKind.Array) return [];
        return models.EnumerateArray()
            .Where(model => model.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String)
            .Select(model => model.GetProperty("id").GetString() ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> ExtractOllamaModelIds(string response)
    {
        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement root = document.RootElement;
        ThrowIfError(root, "Ollama 模型列表");
        if (!root.TryGetProperty("models", out JsonElement models) || models.ValueKind != JsonValueKind.Array) return [];
        return models.EnumerateArray()
            .Where(model => model.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String)
            .Select(model => model.GetProperty("name").GetString() ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    public static string CreateTencentAuthorization(string secretId, string secretKey, long timestamp, string payload, string host = "tmt.tencentcloudapi.com")
    {
        const string service = "tmt";
        string date = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string payloadHash = Sha256Hex(payload);
        string canonicalHeaders = "content-type:application/json; charset=utf-8\nhost:" + host + "\n";
        const string signedHeaders = "content-type;host";
        string canonicalRequest = "POST\n/\n\n" + canonicalHeaders + "\n" + signedHeaders + "\n" + payloadHash;
        string credentialScope = date + "/" + service + "/tc3_request";
        string stringToSign = "TC3-HMAC-SHA256\n" + timestamp.ToString(CultureInfo.InvariantCulture) + "\n" + credentialScope + "\n" + Sha256Hex(canonicalRequest);
        byte[] secretDate = HmacSha256(Encoding.UTF8.GetBytes("TC3" + secretKey), date);
        byte[] secretService = HmacSha256(secretDate, service);
        byte[] secretSigning = HmacSha256(HmacSha256(secretService, "tc3_request"), stringToSign);
        return "TC3-HMAC-SHA256 Credential=" + secretId + "/" + credentialScope + ", SignedHeaders=" + signedHeaders + ", Signature=" + Convert.ToHexString(secretSigning).ToLowerInvariant();
    }

    public static string CreateAlibabaSignature(string accessKeySecret, IReadOnlyDictionary<string, string> parameters)
    {
        string canonicalizedQuery = BuildQuery(parameters);
        string stringToSign = "GET&%2F&" + PercentEncode(canonicalizedQuery);
        return Convert.ToBase64String(HmacSha1(Encoding.UTF8.GetBytes(accessKeySecret + "&"), stringToSign));
    }

    public static string CreateVolcengineAuthorization(string accessKey, string secretKey, string region, string host, string canonicalQuery, string payloadHash, string date)
    {
        const string service = "translate";
        string shortDate = date[..8];
        string canonicalHeaders = "content-type:application/json; charset=utf-8\nhost:" + host + "\nx-content-sha256:" + payloadHash + "\nx-date:" + date + "\n";
        const string signedHeaders = "content-type;host;x-content-sha256;x-date";
        string canonicalRequest = "POST\n/\n" + canonicalQuery + "\n" + canonicalHeaders + "\n" + signedHeaders + "\n" + payloadHash;
        string credentialScope = shortDate + "/" + region + "/" + service + "/request";
        string stringToSign = "HMAC-SHA256\n" + date + "\n" + credentialScope + "\n" + Sha256Hex(canonicalRequest);
        byte[] dateKey = HmacSha256(Encoding.UTF8.GetBytes(secretKey), shortDate);
        byte[] regionKey = HmacSha256(dateKey, region);
        byte[] serviceKey = HmacSha256(regionKey, service);
        string signature = Convert.ToHexString(HmacSha256(HmacSha256(serviceKey, "request"), stringToSign)).ToLowerInvariant();
        return "HMAC-SHA256 Credential=" + accessKey + "/" + credentialScope + ", SignedHeaders=" + signedHeaders + ", Signature=" + signature;
    }

    public static string CreateHuaweiAuthorization(string accessKey, string secretKey, string host, string path, string payload, string date)
    {
        const string signedHeaders = "content-type;host;x-sdk-date";
        string canonicalHeaders = "content-type:application/json; charset=utf-8\nhost:" + host + "\nx-sdk-date:" + date + "\n";
        string canonicalRequest = "POST\n" + path + "\n\n" + canonicalHeaders + "\n" + signedHeaders + "\n" + Sha256Hex(payload);
        string stringToSign = "SDK-HMAC-SHA256\n" + date + "\n" + Sha256Hex(canonicalRequest);
        string signature = Convert.ToHexString(HmacSha256(Encoding.UTF8.GetBytes(secretKey), stringToSign)).ToLowerInvariant();
        return "SDK-HMAC-SHA256 Access=" + accessKey + ", SignedHeaders=" + signedHeaders + ", Signature=" + signature;
    }

    public static string CreateIFlytekAuthorization(string apiKey, string apiSecret, string host, string path, string date)
    {
        string signatureOrigin = "host: " + host + "\ndate: " + date + "\nPOST " + path + " HTTP/1.1";
        string signature = Convert.ToBase64String(HmacSha256(Encoding.UTF8.GetBytes(apiSecret), signatureOrigin));
        string authorizationOrigin = "api_key=\"" + apiKey + "\", algorithm=\"hmac-sha256\", headers=\"host date request-line\", signature=\"" + signature + "\"";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(authorizationOrigin));
    }

    public static string ToDeepLTargetLanguage(string language) => language switch
    {
        "zh-CN" => "ZH-HANS",
        "ja" => "JA",
        "ko" => "KO",
        "en" => "EN",
        _ => language.ToUpperInvariant()
    };

    public static string ToTencentTargetLanguage(string language) => language switch
    {
        "zh-CN" => "zh",
        "ja" => "ja",
        "ko" => "ko",
        _ => language
    };

    private static string ConcatenateArrayStrings(JsonElement root, string arrayName, string? propertyName)
    {
        if (!root.TryGetProperty(arrayName, out JsonElement results) || results.ValueKind != JsonValueKind.Array) return string.Empty;
        return string.Concat(results.EnumerateArray().Select(item => propertyName == null
            ? item.GetString() ?? string.Empty
            : item.TryGetProperty(propertyName, out JsonElement value) ? value.GetString() ?? string.Empty : string.Empty));
    }

    private static string? FindText(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String) return value.GetString();
            foreach (JsonProperty property in element.EnumerateObject())
            {
                string? nested = FindText(property.Value, propertyName);
                if (nested != null) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                string? nested = FindText(item, propertyName);
                if (nested != null) return nested;
            }
        }
        return null;
    }

    private static object[] CreateAiMessages(string text, string targetLanguage) =>
    [
        new
        {
            role = "system",
            content = "You are a translation engine. Return only the translated text without explanations or quotation marks."
        },
        new
        {
            role = "user",
            content = "Translate the following text into " + ToAiTargetLanguage(targetLanguage) + ":\n" + text
        }
    ];

    private static void ThrowIfError(JsonElement root, string provider)
    {
        string? error = FindText(root, "error_msg") ?? FindText(root, "message") ?? FindText(root, "Message");
        if (!string.IsNullOrWhiteSpace(error)) throw new InvalidOperationException(provider + "请求失败：" + error);
    }

    private static string ExtractErrorMessage(string payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            return FindText(document.RootElement, "error_msg") ?? FindText(document.RootElement, "message") ?? FindText(document.RootElement, "Message") ?? payload;
        }
        catch (JsonException)
        {
            return string.IsNullOrWhiteSpace(payload) ? "服务未返回详细信息。" : payload;
        }
    }

    private static void RequireCredentials(TranslationConfiguration configuration, string provider, params (string Label, string Value)[] credentials)
    {
        string[] missing = credentials.Where(credential => string.IsNullOrWhiteSpace(credential.Value)).Select(credential => credential.Label).ToArray();
        if (missing.Length > 0) throw new InvalidOperationException("请先在设置的“快捷翻译”页面填写" + provider + "的" + string.Join("、", missing) + "。" );
    }

    private static Uri AppendQuery(string endpoint, IReadOnlyDictionary<string, string> parameters) =>
        new(endpoint + (endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?") + BuildQuery(parameters));

    private static string BuildQuery(IEnumerable<KeyValuePair<string, string>> parameters) => string.Join("&", parameters
        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => PercentEncode(pair.Key) + "=" + PercentEncode(pair.Value)));

    private static string PercentEncode(string value) => Uri.EscapeDataString(value).Replace("%7E", "~", StringComparison.OrdinalIgnoreCase).Replace("*", "%2A", StringComparison.Ordinal);

    private static byte[] HmacSha256(byte[] key, string value) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));

    private static byte[] HmacSha1(byte[] key, string value) => HMACSHA1.HashData(key, Encoding.UTF8.GetBytes(value));

    private static string Sha256Hex(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string TruncateYoudaoInput(string text) => text.Length <= 20 ? text : text[..10] + text.Length + text[^10..];

    private static string ToBaiduTargetLanguage(string language) => language switch { "zh-CN" => "zh", "ja" => "jp", "ko" => "kor", _ => language };
    private static string ToYoudaoTargetLanguage(string language) => language switch { "zh-CN" => "zh-CHS", _ => language };
    private static string ToGoogleTargetLanguage(string language) => language switch { "zh-CN" => "zh-CN", _ => language };
    private static string ToAzureTargetLanguage(string language) => language switch { "zh-CN" => "zh-Hans", _ => language };
    private static string ToAlibabaTargetLanguage(string language) => language switch { "zh-CN" => "zh", _ => language };
    private static string ToVolcengineTargetLanguage(string language) => language switch { "zh-CN" => "zh", _ => language };
    private static string ToHuaweiTargetLanguage(string language) => language switch { "zh-CN" => "zh", _ => language };
    private static string ToIFlytekTargetLanguage(string language) => language switch { "zh-CN" => "cn", "ko" => "ko", _ => language };
    private static string ToAiTargetLanguage(string language) => language switch { "zh-CN" => "Simplified Chinese", "ja" => "Japanese", "ko" => "Korean", "en" => "English", _ => language };
}
