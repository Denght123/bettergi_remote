using System.Diagnostics;

namespace BetterGI.RemoteLite.BetterGi;

public static class BetterGiVersionPolicy
{
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
            var supported = version.Major == 0 && version.Minor == 64;
            return new VersionCheckResult(true, supported, version.ToString(), supported ? null : "首版仅支持 BetterGI 0.64.x");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new VersionCheckResult(true, false, null, exception.Message);
        }
    }
}

public sealed record VersionCheckResult(bool Configured, bool Supported, string? Version, string? Message);

