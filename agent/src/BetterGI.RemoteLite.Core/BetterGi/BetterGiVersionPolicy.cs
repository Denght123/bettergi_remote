using System.Diagnostics;
using System.Text.Json;

namespace BetterGI.RemoteLite.BetterGi;

public static class BetterGiVersionPolicy
{
    public static readonly Version VerifiedVersion = new(0, 64, 0);

    public static VersionCheckResult Check(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return new VersionCheckResult(false, false, null, "未配置 BetterGI.exe");
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            var raw = info.ProductVersion ?? info.FileVersion;
            var numeric = raw?.Split('+', '-', StringSplitOptions.RemoveEmptyEntries)[0];
            if (numeric is null || !Version.TryParse(numeric, out var version))
            {
                return new VersionCheckResult(true, false, raw, "无法识别 BetterGI 版本");
            }
            return Assess(version, HasCompatibleConfiguration(executablePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new VersionCheckResult(true, false, null, exception.Message);
        }
    }

    public static VersionCheckResult Assess(Version version, bool compatibleConfiguration)
    {
        ArgumentNullException.ThrowIfNull(version);
        var display = version.ToString();
        if (version.Major == VerifiedVersion.Major && version.Minor == VerifiedVersion.Minor)
        {
            return new VersionCheckResult(true, true, display, null, true);
        }
        if (version < VerifiedVersion)
        {
            return new VersionCheckResult(true, false, display, $"BetterGI {display} 过旧，请升级到 {VerifiedVersion} 或更高版本。", false);
        }
        return compatibleConfiguration
            ? new VersionCheckResult(true, true, display, $"BetterGI {display} 尚未完成正式实机验证，已通过一条龙配置结构兼容检查。建议保持 BetterGI Remote 为最新版。", false)
            : new VersionCheckResult(true, false, display, $"BetterGI {display} 的一条龙配置结构与当前版本不兼容，请先更新 BetterGI Remote。", false);
    }

    private static bool HasCompatibleConfiguration(string executablePath)
    {
        try
        {
            var root = Path.GetDirectoryName(Path.GetFullPath(executablePath)) ?? string.Empty;
            var globalPath = Path.Combine(root, "User", "config.json");
            var oneDragonDirectory = Path.Combine(root, "User", "OneDragon");
            if (!File.Exists(globalPath) || !Directory.Exists(oneDragonDirectory))
            {
                return false;
            }
            using (var global = JsonDocument.Parse(File.ReadAllBytes(globalPath)))
            {
                if (global.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }
            }
            foreach (var path in Directory.EnumerateFiles(oneDragonDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (names.Contains("TaskEnabledList") && names.Contains("TaskOrder") && names.Contains("TaskDefinitions"))
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return false;
        }
    }
}

public sealed record VersionCheckResult(bool Configured, bool Supported, string? Version, string? Message, bool Verified = false);
