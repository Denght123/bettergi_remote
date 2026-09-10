using System.Text.Json;

namespace BetterGI.RemoteLite.Updates;

public static class UpdateManifestParser
{
    public static GitHubUpdateAsset? ParseLatest(string json, Version currentVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(currentVersion);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var versionText = root.GetProperty("version").GetString()?.Trim()
            ?? throw new InvalidDataException("更新清单缺少版本号。");
        if (!Version.TryParse(versionText.Split('-', '+')[0], out var version))
        {
            throw new InvalidDataException("更新清单版本号无法识别。");
        }
        if (version <= currentVersion) return null;

        var expectedName = $"BetterGI.Remote.Setup.{version}.exe";
        var fileName = root.GetProperty("fileName").GetString()?.Trim();
        if (!string.Equals(fileName, expectedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"更新清单没有匹配的安装包 {expectedName}。");
        }
        var downloadUrl = root.GetProperty("downloadUrl").GetString()?.Trim();
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("更新清单中的安装包地址不是有效的 HTTPS 地址。");
        }
        var sha256 = root.GetProperty("sha256").GetString()?.Trim();
        if (sha256?.Length != 64)
        {
            throw new InvalidDataException("更新清单缺少有效的 SHA-256 摘要。");
        }
        _ = Convert.FromHexString(sha256);
        var tag = root.TryGetProperty("tag", out var tagElement) ? tagElement.GetString()?.Trim() : null;
        var releaseUrl = root.TryGetProperty("releaseUrl", out var releaseElement) ? releaseElement.GetString()?.Trim() : null;
        return new GitHubUpdateAsset(
            version,
            string.IsNullOrWhiteSpace(tag) ? $"v{version}" : tag,
            releaseUrl ?? string.Empty,
            downloadUri.AbsoluteUri,
            expectedName,
            sha256.ToUpperInvariant());
    }
}
