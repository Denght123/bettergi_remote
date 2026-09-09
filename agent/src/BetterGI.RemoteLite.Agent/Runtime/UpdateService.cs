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
        using var response = await _httpClient.GetAsync($"https://api.github.com/repos/{ProductDefaults.GitHubRepository}/releases/latest", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return GitHubReleaseParser.ParseLatest(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false),
            Version.Parse(ProductDefaults.ProductVersion));
    }

    public async Task<string> DownloadAsync(GitHubUpdateAsset update, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(AgentSettingsStore.DataDirectory, "Updates", update.Tag);
        Directory.CreateDirectory(directory);
        var finalPath = Path.Combine(directory, update.FileName);
        var temporaryPath = finalPath + ".download";
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
