using System.Text.Json;
using BetterGI.RemoteLite.Protocol;

namespace BetterGI.RemoteLite.Agent.Storage;

public sealed class AgentSettingsStore
{
    private readonly object _sync = new();
    private AgentSettings _current;

    public AgentSettingsStore()
    {
        Directory.CreateDirectory(DataDirectory);
        _current = LoadFromDisk();
    }

    public static string DataDirectory { get; } = Environment.GetEnvironmentVariable("BGRL_DATA_DIRECTORY")?.Trim() is { Length: > 0 } customDirectory
        ? Path.GetFullPath(customDirectory)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BetterGI Remote Lite");
    public static string ReportsDirectory => Path.Combine(DataDirectory, "Reports");
    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public AgentSettings Current
    {
        get
        {
            lock (_sync)
            {
                return Clone(_current);
            }
        }
    }

    public void Save(AgentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_sync)
        {
            Directory.CreateDirectory(DataDirectory);
            var temporary = SettingsPath + ".tmp";
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions(RemoteJson.Options) { WriteIndented = true });
            File.WriteAllText(temporary, json);
            File.Move(temporary, SettingsPath, true);
            _current = Clone(settings);
        }
    }

    public void Update(Action<AgentSettings> update)
    {
        lock (_sync)
        {
            var next = Clone(_current);
            update(next);
            Save(next);
        }
    }

    private AgentSettings LoadFromDisk()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AgentSettings();
            }
            var settings = JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(SettingsPath), RemoteJson.Options) ?? new AgentSettings();
            var migrated = false;
            if (ShouldMigrateRelay(settings.RelayBaseUrl))
            {
                settings.RelayBaseUrl = BetterGI.RemoteLite.Agent.ProductDefaults.RelayBaseUrl;
                settings.BoundPhoneDeviceId = null;
                settings.BoundAt = null;
                settings.BindingExpiresAt = null;
                migrated = true;
            }
            if (!string.IsNullOrWhiteSpace(settings.BoundPhoneDeviceId) && settings.BindingExpiresAt is null)
            {
                settings.BoundAt = DateTimeOffset.UtcNow;
                settings.BindingExpiresAt = BetterGI.RemoteLite.Security.BindingLeasePolicy.CreateExpiry(DateTimeOffset.UtcNow);
                migrated = true;
            }
            if (migrated)
            {
                PersistMigration(settings);
            }
            return settings;
        }
        catch (Exception)
        {
            var broken = SettingsPath + ".broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            if (File.Exists(SettingsPath))
            {
                File.Move(SettingsPath, broken, true);
            }
            return new AgentSettings();
        }
    }

    private static bool ShouldMigrateRelay(string? relayBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(relayBaseUrl)) return true;
        return Uri.TryCreate(relayBaseUrl, UriKind.Absolute, out var relay) &&
               relay.Host.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase);
    }

    private static void PersistMigration(AgentSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions(RemoteJson.Options) { WriteIndented = true });
        var temporary = SettingsPath + ".migration.tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, SettingsPath, true);
    }

    private static AgentSettings Clone(AgentSettings value)
        => JsonSerializer.Deserialize<AgentSettings>(JsonSerializer.Serialize(value, RemoteJson.Options), RemoteJson.Options)
           ?? new AgentSettings();
}
