namespace BetterGI.RemoteLite.Agent;

internal static class ProductDefaults
{
    private const string ProductionRelayBaseUrl = "https://bgiremote.163831.xyz";
    public static string RelayBaseUrl => Environment.GetEnvironmentVariable("BGRL_DEFAULT_RELAY_BASE_URL")?.Trim().TrimEnd('/') is { Length: > 0 } value
        ? value
        : ProductionRelayBaseUrl;
    public const string ProductName = "BetterGI Remote";
    public const string ProductVersion = "0.3.2";
    public const string ControlEntryUrl = ProductionRelayBaseUrl;
    public const string GitHubRepository = "Denght123/bettergi_remote";
    public const string GitHubReleasesUrl = "https://github.com/Denght123/bettergi_remote/releases";
}
