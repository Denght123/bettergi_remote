using BetterGI.RemoteLite.Updates;

namespace BetterGI.RemoteLite.Core.Tests;

public sealed class UpdateManifestParserTests
{
    private const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void ReturnsNewerTrustedInstaller()
    {
        var result = UpdateManifestParser.ParseLatest(Manifest("0.3.4", Digest), new Version(0, 3, 3));
        Assert.NotNull(result);
        Assert.Equal(new Version(0, 3, 4), result.Version);
        Assert.Equal(Digest.ToUpperInvariant(), result.Sha256);
        Assert.Equal("https://bgiremote.163831.xyz/downloads/BetterGI.Remote.Setup.0.3.4.exe", result.DownloadUrl);
    }

    [Fact]
    public void IgnoresCurrentVersion()
    {
        Assert.Null(UpdateManifestParser.ParseLatest(Manifest("0.3.4", Digest), new Version(0, 3, 4)));
    }

    [Fact]
    public void RejectsInsecureDownload()
    {
        var json = Manifest("0.3.4", Digest).Replace("https://", "http://", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => UpdateManifestParser.ParseLatest(json, new Version(0, 3, 3)));
    }

    private static string Manifest(string version, string digest) => $$"""
        {"version":"{{version}}","tag":"v{{version}}","releaseUrl":"https://github.com/Denght123/bettergi_remote/releases/tag/v{{version}}","downloadUrl":"https://bgiremote.163831.xyz/downloads/BetterGI.Remote.Setup.{{version}}.exe","fileName":"BetterGI.Remote.Setup.{{version}}.exe","sha256":"{{digest}}"}
        """;
}
