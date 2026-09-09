using System.Text.Json;

namespace BetterGI.RemoteLite.Updates;

public sealed record GitHubUpdateAsset(Version Version, string Tag, string ReleaseUrl, string DownloadUrl, string FileName, string Sha256);

public static class GitHubReleaseParser
{
    public static GitHubUpdateAsset? ParseLatest(string json, Version currentVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(currentVersion);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString()?.Trim() ?? throw new InvalidDataException("GitHub Release 缺少版本号。");
        if (!Version.TryParse(tag.TrimStart('v', 'V').Split('-', '+')[0], out var version))
        {
            throw new InvalidDataException("GitHub Release 版本号无法识别。");
        }
        if (version <= currentVersion) return null;
        var expectedName = $"BetterGI.Remote.Setup.{version}.exe";
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), expectedName, StringComparison.OrdinalIgnoreCase)) continue;
            var digest = asset.TryGetProperty("digest", out var digestElement) ? digestElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || digest.Length != 71)
            {
                throw new InvalidDataException("最新版安装包没有有效的 GitHub SHA-256 摘要，已拒绝自动更新。");
            }
            _ = Convert.FromHexString(digest[7..]);
            return new GitHubUpdateAsset(
                version,
                tag,
                root.GetProperty("html_url").GetString() ?? string.Empty,
                asset.GetProperty("browser_download_url").GetString() ?? throw new InvalidDataException("最新版缺少下载地址。"),
                expectedName,
                digest[7..].ToUpperInvariant());
        }
        throw new InvalidDataException($"最新版没有找到受信任的安装包 {expectedName}。");
    }
}
