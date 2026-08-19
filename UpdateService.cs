using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TaskbarInfo
{
    public sealed class UpdateService
    {
        private const string RepoOwner = "yixing233";
        private const string RepoName = "TaskbarInfo";
        private const string DefaultVersion = "1.1.5";

        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public static string RepositoryUrl => $"https://github.com/{RepoOwner}/{RepoName}";
        public static string ReleasesUrl => $"{RepositoryUrl}/releases";
        public static string CurrentVersionDisplay => $"v{ToDisplayVersion(GetCurrentVersion())}";

        public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        {
            var currentVersion = GetCurrentVersion();

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
            using var response = await HttpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return UpdateCheckResult.NoRelease(currentVersion);
            }

            if (!response.IsSuccessStatusCode)
            {
                var fallbackResult = await TryCheckFromLatestReleasePageAsync(currentVersion, cancellationToken);
                if (fallbackResult != null)
                {
                    return fallbackResult;
                }

                return UpdateCheckResult.Failed(
                    currentVersion,
                    $"GitHub 返回 {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var release = await JsonSerializer.DeserializeAsync<GitHubReleaseResponse>(stream, JsonOptions, cancellationToken);
            if (release == null)
            {
                return UpdateCheckResult.Failed(currentVersion, "未能解析 GitHub Release 响应。");
            }

            var latestVersion = TryParseVersion(release.TagName) ?? TryParseVersion(release.Name);
            if (latestVersion == null)
            {
                return UpdateCheckResult.Failed(currentVersion, "最新 Release 的标签不是可比较的版本号。请使用类似 v1.2.3 的标签。");
            }

            var releasePageUrl = string.IsNullOrWhiteSpace(release.HtmlUrl) ? ReleasesUrl : release.HtmlUrl!;
            UpdatePackage? package = SelectUpdatePackage(release.Assets);
            var downloadUrl = package?.DownloadUrl ?? releasePageUrl;
            var releaseTitle = string.IsNullOrWhiteSpace(release.Name) ? release.TagName ?? latestVersion.ToString() : release.Name!;

            return UpdateCheckResult.SuccessResult(
                currentVersion,
                latestVersion,
                releaseTitle,
                release.TagName ?? releaseTitle,
                release.Body ?? string.Empty,
                releasePageUrl,
                downloadUrl,
                package,
                release.PublishedAt);
        }

        private static async Task<UpdateCheckResult?> TryCheckFromLatestReleasePageAsync(Version currentVersion, CancellationToken cancellationToken)
        {
            using var response = await HttpClient.GetAsync($"{RepositoryUrl}/releases/latest", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return UpdateCheckResult.NoRelease(currentVersion);
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var finalUri = response.RequestMessage?.RequestUri?.AbsoluteUri;
            if (string.IsNullOrWhiteSpace(finalUri) || !finalUri.Contains("/releases/tag/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var tag = Uri.UnescapeDataString(finalUri.Split('/').Last());
            var latestVersion = TryParseVersion(tag);
            if (latestVersion == null)
            {
                return null;
            }

            return UpdateCheckResult.SuccessResult(
                currentVersion,
                latestVersion,
                tag,
                tag,
                string.Empty,
                finalUri,
                finalUri,
                null,
                null);
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            var version = ToDisplayVersion(GetCurrentVersion());

            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TaskbarInfo", version));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            return client;
        }

        private static Version GetCurrentVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            return TryParseVersion(informational)
                ?? NormalizeVersion(assembly.GetName().Version)
                ?? new Version(DefaultVersion);
        }

        private static Version? TryParseVersion(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var cleaned = value.Trim();
            if (cleaned.StartsWith('v') || cleaned.StartsWith('V'))
            {
                cleaned = cleaned[1..];
            }

            var suffixIndex = cleaned.IndexOfAny(['-', '+', ' ']);
            if (suffixIndex >= 0)
            {
                cleaned = cleaned[..suffixIndex];
            }

            if (!Version.TryParse(cleaned, out var version))
            {
                return null;
            }

            return NormalizeVersion(version);
        }

        private static Version? NormalizeVersion(Version? version)
        {
            if (version == null)
            {
                return null;
            }

            var build = version.Build >= 0 ? version.Build : 0;
            return new Version(version.Major, version.Minor, build);
        }

        private static string ToDisplayVersion(Version version)
        {
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        private static UpdatePackage? SelectUpdatePackage(IReadOnlyList<GitHubReleaseAssetResponse>? assets)
        {
            if (assets == null || assets.Count == 0)
            {
                return null;
            }

            foreach (GitHubReleaseAssetResponse asset in assets)
            {
                string? name = asset.Name?.Trim();
                if (string.IsNullOrWhiteSpace(name) ||
                    (!name.StartsWith("TinyBar-Setup", StringComparison.OrdinalIgnoreCase) &&
                     !name.StartsWith("TaskbarInfo-Setup", StringComparison.OrdinalIgnoreCase) &&
                     !name.StartsWith("taskbarTool-Setup", StringComparison.OrdinalIgnoreCase)) ||
                    !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                    !Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out Uri? downloadUri) ||
                    downloadUri.Scheme != Uri.UriSchemeHttps ||
                    !TryParseSha256Digest(asset.Digest, out string sha256))
                {
                    continue;
                }

                return new UpdatePackage(name, downloadUri.AbsoluteUri, asset.Size, sha256);
            }

            return null;
        }

        private static bool TryParseSha256Digest(string? digest, out string sha256)
        {
            sha256 = string.Empty;
            if (string.IsNullOrWhiteSpace(digest)) return false;

            const string prefix = "sha256:";
            string candidate = digest.Trim();
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            candidate = candidate[prefix.Length..];
            if (candidate.Length != 64 || candidate.Any(c => !Uri.IsHexDigit(c))) return false;

            sha256 = candidate.ToLowerInvariant();
            return true;
        }

        private sealed class GitHubReleaseResponse
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("body")]
            public string? Body { get; set; }

            [JsonPropertyName("html_url")]
            public string? HtmlUrl { get; set; }

            [JsonPropertyName("published_at")]
            public DateTimeOffset? PublishedAt { get; set; }

            [JsonPropertyName("assets")]
            public List<GitHubReleaseAssetResponse>? Assets { get; set; }
        }

        private sealed class GitHubReleaseAssetResponse
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }

            [JsonPropertyName("size")]
            public long Size { get; set; }

            [JsonPropertyName("digest")]
            public string? Digest { get; set; }
        }
    }

    public sealed class UpdateCheckResult
    {
        private UpdateCheckResult()
        {
        }

        public bool Success { get; private init; }
        public bool HasUpdate { get; private init; }
        public bool NoReleasePublished { get; private init; }
        public Version CurrentVersion { get; private init; } = new Version(1, 1, 0);
        public Version? LatestVersion { get; private init; }
        public string CurrentVersionDisplay => $"v{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";
        public string LatestVersionDisplay => LatestVersion == null ? "未知" : $"v{LatestVersion.Major}.{LatestVersion.Minor}.{LatestVersion.Build}";
        public string ReleaseName { get; private init; } = string.Empty;
        public string ReleaseTag { get; private init; } = string.Empty;
        public string ReleaseNotes { get; private init; } = string.Empty;
        public string ReleasePageUrl { get; private init; } = UpdateService.ReleasesUrl;
        public string DownloadUrl { get; private init; } = UpdateService.ReleasesUrl;
        public UpdatePackage? Package { get; private init; }
        public DateTimeOffset? PublishedAt { get; private init; }
        public string? ErrorMessage { get; private init; }

        public static UpdateCheckResult SuccessResult(
            Version currentVersion,
            Version latestVersion,
            string releaseName,
            string releaseTag,
            string releaseNotes,
            string releasePageUrl,
            string downloadUrl,
            UpdatePackage? package,
            DateTimeOffset? publishedAt)
        {
            return new UpdateCheckResult
            {
                Success = true,
                HasUpdate = latestVersion > currentVersion,
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                ReleaseName = releaseName,
                ReleaseTag = releaseTag,
                ReleaseNotes = releaseNotes,
                ReleasePageUrl = releasePageUrl,
                DownloadUrl = downloadUrl,
                Package = package,
                PublishedAt = publishedAt
            };
        }

        public static UpdateCheckResult NoRelease(Version currentVersion)
        {
            return new UpdateCheckResult
            {
                Success = true,
                NoReleasePublished = true,
                CurrentVersion = currentVersion
            };
        }

        public static UpdateCheckResult Failed(Version currentVersion, string errorMessage)
        {
            return new UpdateCheckResult
            {
                Success = false,
                CurrentVersion = currentVersion,
                ErrorMessage = errorMessage
            };
        }
    }

    public sealed record UpdatePackage(string FileName, string DownloadUrl, long Size, string Sha256);
}
