using System.Text.Json;

namespace BetterGI.RemoteLite.Updates;

public sealed record GitHubUpdateAsset(Version Version, string Tag, string ReleaseUrl, string DownloadUrl, string FileName, string Sha256);

public static class GitHubReleaseParser
{
    public static GitHubUpdateAsset? ParseLatest(string json, Version currentVersion)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        using var document = UpdateFeedReader.ParseObject(json, "GitHub 更新服务");
        var root = document.RootElement;
        var tag = UpdateFeedReader.RequiredString(root, "tag_name", "版本号");
        if (!Version.TryParse(tag.TrimStart('v', 'V').Split('-', '+')[0], out var version))
        {
            throw new InvalidDataException("GitHub Release 版本号无法识别。");
        }
        if (version <= currentVersion) return null;
        var expectedName = $"BetterGI.Remote.Setup.{version}.exe";
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GitHub 版本信息缺少安装包列表。");
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object) continue;
            var name = UpdateFeedReader.OptionalString(asset, "name", "安装包名称");
            if (!string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase)) continue;
            var digest = UpdateFeedReader.OptionalString(asset, "digest", "校验摘要");
            if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || digest.Length != 71)
            {
                throw new InvalidDataException("最新版安装包没有有效的 GitHub SHA-256 摘要，已拒绝自动更新。");
            }
            var sha256 = UpdateFeedReader.ValidDigest(digest[7..]);
            var download = UpdateFeedReader.RequiredString(asset, "browser_download_url", "下载地址");
            if (!Uri.TryCreate(download, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("GitHub 安装包地址不是有效的 HTTPS 地址。");
            return new GitHubUpdateAsset(
                version,
                tag,
                UpdateFeedReader.OptionalString(root, "html_url", "发布地址") ?? string.Empty,
                uri.AbsoluteUri,
                expectedName,
                sha256);
        }
        throw new InvalidDataException($"最新版没有找到受信任的安装包 {expectedName}。");
    }
}
