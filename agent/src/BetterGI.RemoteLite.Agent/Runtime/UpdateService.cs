using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using BetterGI.RemoteLite.Agent.Storage;
using BetterGI.RemoteLite.Updates;

namespace BetterGI.RemoteLite.Agent.Runtime;

internal sealed class UpdateService(HttpClient? httpClient = null)
{
    private readonly HttpClient _httpClient = httpClient ?? CreateClient();

    public async Task<GitHubUpdateAsset?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = Version.Parse(ProductDefaults.ProductVersion);
        Exception? officialError = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await _httpClient.GetAsync(ProductDefaults.UpdateManifestUrl, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return UpdateManifestParser.ParseLatest(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false), current);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            officialError = exception;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await _httpClient.GetAsync($"https://api.github.com/repos/{ProductDefaults.GitHubRepository}/releases/latest", timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return GitHubReleaseParser.ParseLatest(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false), current);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            throw new InvalidOperationException($"无法连接 BetterGI Remote 更新服务。请稍后重试，或从 {ProductDefaults.ControlEntryUrl} 手动下载安装包。", new AggregateException(officialError!, exception));
        }
    }

    public async Task<string> DownloadAsync(GitHubUpdateAsset update, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(AgentSettingsStore.DataDirectory, "Updates", update.Tag);
        Directory.CreateDirectory(directory);
        var finalPath = Path.Combine(directory, update.FileName);
        var temporaryPath = finalPath + ".download";
        try
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var response = await _httpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength;
                    await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    await using var output = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                    var buffer = new byte[81920];
                    long received = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        received += read;
                        if (total > 0) progress?.Report((int)Math.Min(100, received * 100 / total.Value));
                    }
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (Exception exception) when (
                    attempt < 3 &&
                    !cancellationToken.IsCancellationRequested &&
                    exception is HttpRequestException or IOException or TaskCanceledException)
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
                }
            }
            await using var verification = File.OpenRead(temporaryPath);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(verification, cancellationToken).ConfigureAwait(false));
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(update.Sha256)))
            {
                throw new InvalidDataException("安装包 SHA-256 校验失败，已取消更新。");
            }
            File.Move(temporaryPath, finalPath, true);
            return finalPath;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static void LaunchInstaller(string installerPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS",
            UseShellExecute = true,
        });
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BetterGI-Remote", ProductDefaults.ProductVersion));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
