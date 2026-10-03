using System.Net.Http.Headers;
using System.Text.Json;
using BetterGI.RemoteLite.Updates;

namespace BetterGI.RemoteLite.Agent.Runtime;

internal sealed class BetterGiReleaseService
{
    public const string ReleasesUrl = "https://github.com/babalae/better-genshin-impact/releases/latest";
    private const string GitHubApiUrl = "https://api.github.com/repos/babalae/better-genshin-impact/releases/latest";
    private readonly HttpClient _client;

    public BetterGiReleaseService(HttpClient? client = null)
    {
        _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BetterGI-Remote", ProductDefaults.ProductVersion));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<BetterGiReleaseInfo?> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        return await UpdateFeedReader.ReadWithFallbackAsync(_client, ProductDefaults.BetterGiReleaseApiUrl,
            GitHubApiUrl, (json, _) => Parse(json), "BetterGI 官方版本服务暂时不可用，请稍后重试。", cancellationToken).ConfigureAwait(false);
    }

    private static BetterGiReleaseInfo Parse(string json)
    {
        using var document = UpdateFeedReader.ParseObject(json, "BetterGI 版本服务");
        var tag = UpdateFeedReader.RequiredString(document.RootElement, "tag_name", "版本号").TrimStart('v', 'V').Split('-', '+')[0];
        if (!Version.TryParse(tag, out var version)) throw new InvalidDataException("BetterGI 最新版本号无法识别。");
        var url = UpdateFeedReader.OptionalString(document.RootElement, "html_url", "发布地址");
        if (!string.IsNullOrWhiteSpace(url) && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidDataException("BetterGI 发布地址不是有效的 HTTPS 地址。");
        return new BetterGiReleaseInfo(version, string.IsNullOrWhiteSpace(url) ? ReleasesUrl : url);
    }
}

internal sealed record BetterGiReleaseInfo(Version Version, string ReleaseUrl);
