using BetterGI.RemoteLite.Notifications;

namespace BetterGI.RemoteLite.Core.Tests;

public sealed class FeishuNotifierTests
{
    [Fact]
    public void SignatureIsStable()
    {
        var first = FeishuNotifier.CreateSignature("1788364800", "secret");
        var second = FeishuNotifier.CreateSignature("1788364800", "secret");
        Assert.Equal(first, second);
        Assert.NotEmpty(first);
    }
}

