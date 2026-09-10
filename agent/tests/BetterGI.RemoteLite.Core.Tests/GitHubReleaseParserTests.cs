using BetterGI.RemoteLite.Updates;

namespace BetterGI.RemoteLite.Core.Tests;

public sealed class GitHubReleaseParserTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void ReturnsTrustedNewerInstaller()
    {
        var result = GitHubReleaseParser.ParseLatest(Release("v0.3.1", $"sha256:{Digest}"), new Version(0, 3, 0));
        Assert.NotNull(result);
        Assert.Equal(new Version(0, 3, 1), result.Version);
        Assert.Equal(Digest.ToUpperInvariant(), result.Sha256);
    }

    [Fact]
    public void IgnoresSameOrOlderVersion()
    {
        Assert.Null(GitHubReleaseParser.ParseLatest(Release("v0.3.1", $"sha256:{Digest}"), new Version(0, 3, 1)));
    }

    [Fact]
    public void RejectsMissingDigest()
    {
        Assert.Throws<InvalidDataException>(() => GitHubReleaseParser.ParseLatest(Release("v0.3.1", ""), new Version(0, 3, 0)));
    }

    private static string Release(string tag, string digest) => $$"""
        {"tag_name":"{{tag}}","html_url":"https://github.com/Denght123/bettergi_remote/releases/tag/{{tag}}","assets":[{"name":"BetterGI.Remote.Setup.{{tag.TrimStart('v')}}.exe","browser_download_url":"https://example.invalid/setup.exe","digest":"{{digest}}"}]}
        """;
}
