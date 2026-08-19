using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace TaskbarInfo
{
    public sealed record InAppUpdateDownloadProgress(long BytesReceived, long TotalBytes)
    {
        public double Fraction => TotalBytes <= 0
            ? 0
            : Math.Clamp((double)BytesReceived / TotalBytes, 0, 1);
    }

    public sealed class InAppUpdateDownloadService
    {
        private const int BufferSize = 64 * 1024;
        private static readonly HttpClient HttpClient = CreateHttpClient();

        public async Task<string> DownloadInstallerAsync(
            UpdatePackage package,
            IProgress<InAppUpdateDownloadProgress>? progress,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(package);
            ValidatePackage(package);

            string cacheDirectory = GetCacheDirectory();
            Directory.CreateDirectory(cacheDirectory);

            string installerPath = Path.Combine(cacheDirectory, BuildInstallerFileName(package));
            if (await HasExpectedHashAsync(installerPath, package.Sha256, cancellationToken))
            {
                progress?.Report(new InAppUpdateDownloadProgress(package.Size, package.Size));
                return installerPath;
            }

            string partPath = installerPath + ".part";
            TryDelete(partPath);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, package.DownloadUrl);
                using HttpResponseMessage response = await HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();

                long totalBytes = package.Size > 0
                    ? package.Size
                    : response.Content.Headers.ContentLength ?? 0;
                long bytesReceived = 0;
                var stopwatch = Stopwatch.StartNew();

                byte[] actualHash;
                using (IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await using (var destination = new FileStream(
                        partPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        BufferSize,
                        FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        byte[] buffer = new byte[BufferSize];
                        while (true)
                        {
                            int bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                            if (bytesRead == 0) break;

                            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                            hash.AppendData(buffer, 0, bytesRead);
                            bytesReceived += bytesRead;

                            if (stopwatch.ElapsedMilliseconds >= 100)
                            {
                                progress?.Report(new InAppUpdateDownloadProgress(bytesReceived, totalBytes));
                                stopwatch.Restart();
                            }
                        }

                        await destination.FlushAsync(cancellationToken);
                    }

                    actualHash = hash.GetHashAndReset();
                }

                if (package.Size > 0 && bytesReceived != package.Size)
                {
                    throw new InvalidDataException("更新安装包的下载大小与 Release 元数据不一致。");
                }

                byte[] expectedHash = Convert.FromHexString(package.Sha256);
                if (actualHash.Length != expectedHash.Length ||
                    !CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                {
                    throw new InvalidDataException("更新安装包的 SHA-256 校验失败。");
                }

                File.Move(partPath, installerPath, true);
                progress?.Report(new InAppUpdateDownloadProgress(bytesReceived, totalBytes));
                PruneCompletedInstallers(cacheDirectory, installerPath);
                return installerPath;
            }
            catch
            {
                TryDelete(partPath);
                throw;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TinyBar-Updater");
            return client;
        }

        private static string GetCacheDirectory() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TinyBar",
            "Updates");

        private static string BuildInstallerFileName(UpdatePackage package)
        {
            string name = Path.GetFileName(package.FileName);
            return $"{package.Sha256[..12]}-{name}";
        }

        private static void ValidatePackage(UpdatePackage package)
        {
            if (!Uri.TryCreate(package.DownloadUrl, UriKind.Absolute, out Uri? downloadUri) ||
                downloadUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException("更新安装包的下载地址无效。", nameof(package));
            }

            string name = Path.GetFileName(package.FileName);
            if ((!name.StartsWith("TinyBar-Setup", StringComparison.OrdinalIgnoreCase) &&
                 !name.StartsWith("TaskbarInfo-Setup", StringComparison.OrdinalIgnoreCase) &&
                 !name.StartsWith("taskbarTool-Setup", StringComparison.OrdinalIgnoreCase)) ||
                !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(name, package.FileName, StringComparison.Ordinal) ||
                package.Sha256.Length != 64 ||
                package.Sha256.Any(c => !Uri.IsHexDigit(c)))
            {
                throw new ArgumentException("更新安装包元数据无效。", nameof(package));
            }
        }

        private static async Task<bool> HasExpectedHashAsync(
            string path,
            string expectedSha256,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(path)) return false;

            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                byte[] actualHash = await SHA256.HashDataAsync(stream, cancellationToken);
                byte[] expectedHash = Convert.FromHexString(expectedSha256);
                return actualHash.Length == expectedHash.Length &&
                    CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static void PruneCompletedInstallers(string cacheDirectory, string currentInstaller)
        {
            try
            {
                var dir = new DirectoryInfo(cacheDirectory);
                var files = dir.EnumerateFiles("TinyBar-Setup*.exe")
                    .Concat(dir.EnumerateFiles("TaskbarInfo-Setup*.exe"))
                    .Where(file => !string.Equals(file.FullName, currentInstaller, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Skip(2);
                foreach (FileInfo file in files)
                {
                    file.Delete();
                }
            }
            catch (IOException)
            {
                // Cache cleanup is best-effort and must not fail an otherwise valid update download.
            }
            catch (UnauthorizedAccessException)
            {
                // The next successful update can retry cache cleanup.
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
