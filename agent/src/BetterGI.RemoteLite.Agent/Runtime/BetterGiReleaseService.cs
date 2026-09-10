using System.Net.Http.Headers;
using System.Text.Json;

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
        Exception? primaryError = null;
        foreach (var url in new[] { ProductDefaults.BetterGiReleaseApiUrl, GitHubApiUrl })
        {
            try
            {
                using var response = await _client.GetAsync(url, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var release = await ParseAsync(response, cancellationToken).ConfigureAwait(false);
                if (release is not null) return release;
                throw new InvalidDataException("版本服务返回了无法识别的数据。");
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
            {
                if (primaryError is null) primaryError = exception;
                else throw new InvalidOperationException("BetterGI 官方版本服务暂时不可用，请稍后重试。", new AggregateException(primaryError, exception));
            }
        }
        return null;
    }

    private static async Task<BetterGiReleaseInfo?> ParseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
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
