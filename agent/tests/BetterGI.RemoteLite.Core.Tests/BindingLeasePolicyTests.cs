using BetterGI.RemoteLite.Security;

namespace BetterGI.RemoteLite.Core.Tests;

public sealed class BindingLeasePolicyTests
{
    [Fact]
    public void NewBindingLastsAtLeastThirtyDays()
    {
        var now = DateTimeOffset.Parse("2026-09-09T00:00:00Z");
        Assert.True(BindingLeasePolicy.CreateExpiry(now) - now >= TimeSpan.FromDays(30));
    }

    [Fact]
    public void ActiveBindingRenewsNearExpiry()
    {
        var now = DateTimeOffset.Parse("2026-09-09T00:00:00Z");
        Assert.True(BindingLeasePolicy.ShouldRenew(now.AddDays(10), now));
        Assert.False(BindingLeasePolicy.ShouldRenew(now.AddDays(60), now));
        Assert.True(BindingLeasePolicy.IsExpired(now.AddSeconds(-1), now));
    }
}
