using BetterGI.RemoteLite.BetterGi;

namespace BetterGI.RemoteLite.Core.Tests;

public sealed class BetterGiVersionPolicyTests
{
    [Fact]
    public void VerifiedMinorIsFullySupported()
    {
        var result = BetterGiVersionPolicy.Assess(new Version(0, 64, 8), compatibleConfiguration: false);

        Assert.True(result.Supported);
        Assert.True(result.Verified);
        Assert.Null(result.Message);
    }

    [Fact]
    public void FutureVersionUsesCompatibleStructureProbe()
    {
        var result = BetterGiVersionPolicy.Assess(new Version(0, 65, 0), compatibleConfiguration: true);

        Assert.True(result.Supported);
        Assert.False(result.Verified);
        Assert.Contains("尚未完成正式实机验证", result.Message);
    }

    [Fact]
    public void FutureVersionWithChangedStructureIsBlocked()
    {
        var result = BetterGiVersionPolicy.Assess(new Version(1, 0, 0), compatibleConfiguration: false);

        Assert.False(result.Supported);
        Assert.Contains("配置结构", result.Message);
    }

    [Fact]
    public void OlderVersionIsBlocked()
    {
        var result = BetterGiVersionPolicy.Assess(new Version(0, 63, 9), compatibleConfiguration: true);

        Assert.False(result.Supported);
        Assert.Contains("过旧", result.Message);
    }
}
