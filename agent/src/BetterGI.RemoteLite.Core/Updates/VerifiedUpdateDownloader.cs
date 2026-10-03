using System.Security.Cryptography;

namespace BetterGI.RemoteLite.Updates;

public sealed class VerifiedUpdateDownloader(HttpClient client)
{
    public async Task<string> DownloadAsync(GitHubUpdateAsset update, string updatesDirectory,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var expectedName = $"BetterGI.Remote.Setup.{update.Version}.exe";
        if (update.FileName != expectedName) throw new InvalidDataException("更新安装包名称不正确，已取消下载。");
        var digest = UpdateFeedReader.ValidDigest(update.Sha256);
        if (!Uri.TryCreate(update.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("更新安装包必须使用 HTTPS 下载地址。");
        // Use the parsed version, never an unchecked remote tag, for a local directory.
        var directory = Path.Combine(Path.GetFullPath(updatesDirectory), "v" + update.Version);
        Directory.CreateDirectory(directory);
        var finalPath = Path.Combine(directory, expectedName);
        var temporaryPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
                        throw new InvalidDataException("下载服务返回了网页，未得到安装包。请稍后重试。");
                    var total = response.Content.Headers.ContentLength;
                    await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                    await using (var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        var buffer = new byte[81920];
                        long received = 0;
                        int read;
                        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                            received += read;
                            if (total > 0) progress?.Report((int)Math.Min(99, received * 100 / total.Value));
                        }
                        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                        if (total is > 0 && received != total) throw new IOException("安装包下载未完成。");
                    }
                    break;
                }
                catch (Exception exception) when (attempt < 3 && !cancellationToken.IsCancellationRequested &&
                    exception is HttpRequestException or IOException or OperationCanceledException)
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
                }
            }
            // Both the download and verification streams MUST be closed before moving
            // the file. File.OpenRead does not share deletion on Windows.
            await using (var verification = File.OpenRead(temporaryPath))
            {
                var actual = await SHA256.HashDataAsync(verification, cancellationToken).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(digest)))
                    throw new InvalidDataException("安装包 SHA-256 校验失败，已取消更新。");
                verification.Position = 0;
                if (verification.ReadByte() != 'M' || verification.ReadByte() != 'Z')
                    throw new InvalidDataException("下载内容不是有效的 Windows 安装包，已取消更新。");
            }
            File.Move(temporaryPath, finalPath, true);
            progress?.Report(100);
            return finalPath;
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }
}
