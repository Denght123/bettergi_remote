using System.Diagnostics;
using System.Net.Http.Headers;
using BetterGI.RemoteLite.Agent.Storage;
using BetterGI.RemoteLite.Updates;

namespace BetterGI.RemoteLite.Agent.Runtime;

internal sealed class UpdateService(HttpClient? httpClient = null)
{
    private readonly HttpClient _httpClient = httpClient ?? CreateClient();

    public async Task<GitHubUpdateAsset?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = Version.Parse(ProductDefaults.ProductVersion);
        return await UpdateFeedReader.ReadWithFallbackAsync(_httpClient, ProductDefaults.UpdateManifestUrl,
            $"https://api.github.com/repos/{ProductDefaults.GitHubRepository}/releases/latest",
            (json, fallback) => fallback ? GitHubReleaseParser.ParseLatest(json, current) : UpdateManifestParser.ParseLatest(json, current),
            $"无法连接 BetterGI Remote 更新服务。请稍后重试，或从 {ProductDefaults.UpdateBaseUrl} 手动下载安装包。",
            cancellationToken).ConfigureAwait(false);
    }

    public Task<string> DownloadAsync(GitHubUpdateAsset update, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        => new VerifiedUpdateDownloader(_httpClient).DownloadAsync(update, Path.Combine(AgentSettingsStore.DataDirectory, "Updates"), progress, cancellationToken);

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
