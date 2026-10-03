using System.Net;
using System.Text;
using BetterGI.RemoteLite.Updates;

namespace BetterGI.RemoteLite.Core.Tests;

public sealed class UpdateFeedReaderTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly string Release = $$"""{"tag_name":"v0.4.1","assets":[{"name":"BetterGI.Remote.Setup.0.4.1.exe","browser_download_url":"https://example.invalid/setup.exe","digest":"sha256:{{Digest}}"}]}""";

    [Theory]
    [InlineData("text/html", "<!doctype html><html>frontend</html>")]
    [InlineData("application/json", "<html>proxy error</html>")]
    [InlineData("application/json", "{broken")]
    [InlineData("application/json", "[]")]
    [InlineData("application/json", "{\"version\":123}")]
    public async Task InvalidPrimaryUsesTheCorrectGithubFallback(string mediaType, string body)
    {
        using var handler = new Handler(request => request.RequestUri!.Host == "primary.invalid"
            ? Response(body, mediaType) : Response(Release, "application/vnd.github+json"));
        using var client = new HttpClient(handler);
        var asset = await Check(client);
        Assert.Equal(2, handler.Calls);
        Assert.Equal(new Version(0,4,1), asset!.Version);
    }

    [Fact]
    public async Task BothBadSourcesReturnHumanMessageInsteadOfParserDetails()
    {
        using var handler = new Handler(_ => Response("<html>bad gateway</html>", "text/html"));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Check(client));
        Assert.Equal("更新服务暂时不可用，请稍后重试。", error.Message);
        Assert.DoesNotContain("BytePosition", error.Message);
        Assert.Equal(2, ((AggregateException)error.InnerException!).InnerExceptions.Count);
    }

    [Fact]
    public async Task CurrentVersionDoesNotUnnecessarilyTryGithub()
    {
        using var handler = new Handler(_ => Response("{\"version\":\"0.4.0\"}", "application/json"));
        using var client = new HttpClient(handler);
        Assert.Null(await Check(client));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsBadServerOrRetried()
    {
        using var handler = new Handler(_ => Response("{}", "application/json"));
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Check(client,cancellation.Token));
        Assert.Equal(0,handler.Calls);
    }

    [Theory]
    [InlineData("<html>")]
    [InlineData("{}")]
    [InlineData("{\"version\":true}")]
    public void ParsersNeverLeakRawSchemaExceptions(string body)
    {
        Assert.Throws<InvalidDataException>(() => UpdateManifestParser.ParseLatest(body,new Version(0,4,0)));
        Assert.Throws<InvalidDataException>(() => GitHubReleaseParser.ParseLatest(body,new Version(0,4,0)));
    }

    [Fact]
    public void InvalidHexDigestIsRejectedAsDataNotFormatException()
    {
        Assert.Throws<InvalidDataException>(() => GitHubReleaseParser.ParseLatest(Release.Replace(Digest,new string('G',64)),new Version(0,4,0)));
    }

    private static Task<GitHubUpdateAsset?> Check(HttpClient client,CancellationToken token=default)
        => UpdateFeedReader.ReadWithFallbackAsync(client,"https://primary.invalid/latest.json","https://fallback.invalid/latest",
            (json,fallback) => fallback ? GitHubReleaseParser.ParseLatest(json,new Version(0,4,0)) : UpdateManifestParser.ParseLatest(json,new Version(0,4,0)),
            "更新服务暂时不可用，请稍后重试。",token);
    internal static HttpResponseMessage Response(string body,string mediaType) => new(HttpStatusCode.OK) {Content=new StringContent(body,Encoding.UTF8,mediaType)};
    internal sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(respond(request));
        }
    }
}
