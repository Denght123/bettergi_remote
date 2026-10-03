using System.Text.Json;
using System.Text.RegularExpressions;

namespace BetterGI.RemoteLite.BetterGi;

public static partial class BetterGiStartShortcut
{
    public static string? Read(string executable, string requestedConfig)
    {
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(executable)!, "User", "config.json");
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            return FromConfiguration(document.RootElement, requestedConfig);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return null; }
    }

    public static string? FromConfiguration(JsonElement config, string requestedConfig)
    {
        if (config.ValueKind != JsonValueKind.Object ||
            !config.TryGetProperty("selectedOneDragonFlowConfigName", out var selected) || selected.ValueKind != JsonValueKind.String || selected.GetString() != requestedConfig ||
            !config.TryGetProperty("hotKeyConfig", out var hotkeys) || hotkeys.ValueKind != JsonValueKind.Object ||
            !hotkeys.TryGetProperty("onedragonHotkeyType", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "GlobalRegister" ||
            !hotkeys.TryGetProperty("onedragonHotkey", out var key) || key.ValueKind != JsonValueKind.String)
            return null;
        var shortcut = key.GetString() ?? "";
        var tokens = shortcut.Split('+');
        if (!tokens.Any(token => token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || token.Equals("Control", StringComparison.OrdinalIgnoreCase)) ||
            !tokens.Any(token => token.Equals("Shift", StringComparison.OrdinalIgnoreCase)) || tokens.Distinct(StringComparer.OrdinalIgnoreCase).Count() != tokens.Length)
            return null;
        if (hotkeys.TryGetProperty("cancelTaskHotkey", out var cancel) && cancel.ValueKind == JsonValueKind.String &&
            string.Equals(shortcut, cancel.GetString(), StringComparison.OrdinalIgnoreCase)) return null;
        // Only an already configured global BetterGI one-dragon shortcut. Do not
        // use arbitrary keys, focus-dependent shortcuts or a different selected config.
        return ShortcutRegex().IsMatch(shortcut) ? shortcut.ToUpperInvariant() : null;
    }

    [GeneratedRegex(@"^(?:(?:Ctrl|Control|Shift)\+)+(?:F(?:[1-9]|1[0-9]|2[0-4]))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShortcutRegex();
}
