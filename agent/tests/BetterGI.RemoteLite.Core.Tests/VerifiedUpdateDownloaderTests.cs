using System.Security.Cryptography;
using System.Text;
using BetterGI.RemoteLite.Updates;

namespace BetterGI.RemoteLite.Core.Tests;

public sealed class VerifiedUpdateDownloaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),"bgrl-download-"+Guid.NewGuid().ToString("N"));
    private static readonly byte[] Payload=Encoding.UTF8.GetBytes("MZ verified installer sample");
    private static GitHubUpdateAsset Asset(string? digest=null,string tag="v0.4.1") => new(new Version(0,4,1),tag,"","https://example.invalid/setup.exe","BetterGI.Remote.Setup.0.4.1.exe",digest??Convert.ToHexString(SHA256.HashData(Payload)));

    [Fact]
    public async Task VerifiedFileCanBeMovedAndOpenedExclusivelyOnWindows()
    {
        using var handler=new UpdateFeedReaderTests.Handler(_=>UpdateFeedReaderTests.Response(Encoding.UTF8.GetString(Payload),"application/octet-stream"));
        using var client=new HttpClient(handler);
        var path=await new VerifiedUpdateDownloader(client).DownloadAsync(Asset(tag:"../../unsafe"),_root);
        Assert.Equal(Path.Combine(_root,"v0.4.1","BetterGI.Remote.Setup.0.4.1.exe"),path);
        using var stream=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        Assert.Equal(Payload.Length,stream.Length);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!,"*.download"));
    }

    [Fact]
    public async Task HtmlCannotReplaceAnExistingTrustedInstaller()
    {
        var destination=Path.Combine(_root,"v0.4.1");
        Directory.CreateDirectory(destination);
        var final=Path.Combine(destination,"BetterGI.Remote.Setup.0.4.1.exe");
        await File.WriteAllBytesAsync(final,Payload);
        using var handler=new UpdateFeedReaderTests.Handler(_=>UpdateFeedReaderTests.Response("<html>frontend</html>","text/html"));
        using var client=new HttpClient(handler);
        var error=await Assert.ThrowsAsync<InvalidDataException>(()=>new VerifiedUpdateDownloader(client).DownloadAsync(Asset(),_root));
        Assert.Contains("网页",error.Message);
        Assert.Equal(Payload,await File.ReadAllBytesAsync(final));
        Assert.Empty(Directory.GetFiles(destination,"*.download"));
    }

    [Fact]
    public async Task BadDigestDoesNotPublishDownloadedFile()
    {
        using var handler=new UpdateFeedReaderTests.Handler(_=>UpdateFeedReaderTests.Response(Encoding.UTF8.GetString(Payload),"application/octet-stream"));
        using var client=new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(()=>new VerifiedUpdateDownloader(client).DownloadAsync(Asset(new string('0',64)),_root));
        Assert.Empty(Directory.GetFiles(_root,"*.exe",SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(_root,"*.download",SearchOption.AllDirectories));
    }

    public void Dispose() { if(Directory.Exists(_root))Directory.Delete(_root,true); }
}
