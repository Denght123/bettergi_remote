using System.Net.Http.Headers;
using System.Text.Json;

namespace BetterGI.RemoteLite.Agent.Runtime;

internal sealed class BetterGiReleaseService
{
    public const string ReleasesUrl = "https://github.com/babalae/better-genshin-impact/releases/latest";
    private const string ApiUrl = "https://api.github.com/repos/babalae/better-genshin-impact/releases/latest";
    private readonly HttpClient _client;

    public BetterGiReleaseService(HttpClient? client = null)
    {
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BetterGI-Remote", ProductDefaults.ProductVersion));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<BetterGiReleaseInfo?> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _client.GetAsync(ApiUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("tag_name", out var tagNode))
        {
            return null;
        }
        var tag = tagNode.GetString()?.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(tag, out var version))
        {
            return null;
        }
        var url = document.RootElement.TryGetProperty("html_url", out var urlNode)
            ? urlNode.GetString()
            : null;
        return new BetterGiReleaseInfo(version, string.IsNullOrWhiteSpace(url) ? ReleasesUrl : url);
    }
}

internal sealed record BetterGiReleaseInfo(Version Version, string ReleaseUrl);
