using System.Text.Json;

namespace BetterGI.RemoteLite.Updates;

public static class UpdateManifestParser
{
    public static GitHubUpdateAsset? ParseLatest(string json, Version currentVersion)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        using var document = UpdateFeedReader.ParseObject(json, "Remote 更新服务");
        var root = document.RootElement;
        var versionText = UpdateFeedReader.RequiredString(root, "version", "版本号");
        if (!Version.TryParse(versionText.Split('-', '+')[0], out var version))
        {
            throw new InvalidDataException("更新清单版本号无法识别。");
        }
        if (version <= currentVersion) return null;

        var expectedName = $"BetterGI.Remote.Setup.{version}.exe";
        var fileName = UpdateFeedReader.RequiredString(root, "fileName", "安装包名称");
        if (!string.Equals(fileName, expectedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"更新清单没有匹配的安装包 {expectedName}。");
        }
        var downloadUrl = UpdateFeedReader.RequiredString(root, "downloadUrl", "下载地址");
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("更新清单中的安装包地址不是有效的 HTTPS 地址。");
        }
        var sha256 = UpdateFeedReader.ValidDigest(UpdateFeedReader.RequiredString(root, "sha256", "校验摘要"));
        var tag = UpdateFeedReader.OptionalString(root, "tag", "标签");
        var releaseUrl = UpdateFeedReader.OptionalString(root, "releaseUrl", "发布地址");
        return new GitHubUpdateAsset(
            version,
            string.IsNullOrWhiteSpace(tag) ? $"v{version}" : tag,
            releaseUrl ?? string.Empty,
            downloadUri.AbsoluteUri,
            expectedName,
            sha256.ToUpperInvariant());
    }
}
