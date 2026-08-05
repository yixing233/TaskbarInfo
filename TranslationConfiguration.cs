namespace TaskbarInfo;

public sealed record TranslationConfiguration(
    string ProviderId,
    string Provider,
    string AppId,
    string AppSecret,
    string ApiBaseUrl,
    string ExtraCredential = "");
