namespace BetterGI.RemoteLite.Security;

public static class BindingLeasePolicy
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(180);
    public static readonly TimeSpan RenewalThreshold = TimeSpan.FromDays(30);
    public static readonly TimeSpan WarningThreshold = TimeSpan.FromDays(14);

    public static DateTimeOffset CreateExpiry(DateTimeOffset now) => now.Add(Lifetime);
    public static bool IsExpired(DateTimeOffset? expiresAt, DateTimeOffset now) => expiresAt is { } expiry && expiry <= now;
    public static bool ShouldRenew(DateTimeOffset? expiresAt, DateTimeOffset now) => expiresAt is { } expiry && expiry > now && expiry - now <= RenewalThreshold;
    public static int? DaysRemaining(DateTimeOffset? expiresAt, DateTimeOffset now) => expiresAt is { } expiry
        ? Math.Max(0, (int)Math.Ceiling((expiry - now).TotalDays))
        : null;
}
