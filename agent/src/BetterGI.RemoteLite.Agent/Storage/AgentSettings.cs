namespace BetterGI.RemoteLite.Agent.Storage;

public sealed class AgentSettings
{
    public string PcDeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string BetterGiExecutablePath { get; set; } = string.Empty;
    public string RelayBaseUrl { get; set; } = BetterGI.RemoteLite.Agent.ProductDefaults.RelayBaseUrl;
    public string RemoteConfigName { get; set; } = "远程每日";
    public string CancelHotkey { get; set; } = "Ctrl+Shift+F12";
    public string? ProtectedPairingSecret { get; set; }
    public string? BoundPhoneDeviceId { get; set; }
    public string? ProtectedFeishuWebhook { get; set; }
    public string? ProtectedFeishuSigningSecret { get; set; }

    public bool IsConfigured => File.Exists(BetterGiExecutablePath) && Uri.TryCreate(RelayBaseUrl, UriKind.Absolute, out _);
}
