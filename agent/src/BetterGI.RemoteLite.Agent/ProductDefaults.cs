namespace BetterGI.RemoteLite.Agent;

internal static class ProductDefaults
{
    private const string ProductionRelayBaseUrl = "https://bgiremote.163831.xyz";
    public static string RelayBaseUrl => Environment.GetEnvironmentVariable("BGRL_DEFAULT_RELAY_BASE_URL")?.Trim().TrimEnd('/') is { Length: > 0 } value
        ? value
        : ProductionRelayBaseUrl;
    public const string ProductName = "BetterGI Remote";
    public const string ProductVersion = "0.4.1";
    public static string ControlEntryUrl => RelayBaseUrl;
    public const string GitHubRepository = "Denght123/bettergi_remote";
    public const string GitHubReleasesUrl = "https://github.com/Denght123/bettergi_remote/releases";
    // Control relays (including local test relays) are independent from release feeds.
    public const string UpdateBaseUrl = ProductionRelayBaseUrl;
    public static string UpdateManifestUrl => $"{UpdateBaseUrl}/updates/latest.json";
    public static string BetterGiReleaseApiUrl => $"{UpdateBaseUrl}/api/bettergi/latest";
}
