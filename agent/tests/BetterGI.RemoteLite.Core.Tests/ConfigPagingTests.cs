using System.Text.Json;
using BetterGI.RemoteLite.BetterGi;
using BetterGI.RemoteLite.Protocol;

namespace BetterGI.RemoteLite.Core.Tests;

public sealed class ConfigPagingTests
{
    [Fact]
    public void ExpandedCatalogTransfersCompletelyWithinMessageLimit()
    {
        var fields = Enumerable.Range(0, 180).Select(i => new EditableFieldDto(
            $"global.field{i}", "global", "桌面设置", new string('配', 120), EditableFieldType.Text,
            JsonSerializer.SerializeToElement(new string('值', 120)))).ToArray();
        var config = new RemoteConfigDto("远程每日", "rev1", [], fields, "无", DateTimeOffset.UtcNow);
        var collected = new List<string>();
        var offset = 0;
        do
        {
            var page = ConfigPaging.Page(config, offset, 64, "rev1");
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(page, RemoteJson.Options).Length < ProtocolConstants.MaximumPlaintextBytes - 2048);
            Assert.Equal(180, page.TotalFields);
            collected.AddRange(page.Fields.Select(field => field.Path));
            if (page.NextFieldOffset is null) break;
            Assert.True(page.NextFieldOffset > offset);
            offset = page.NextFieldOffset.Value;
        } while (true);
        Assert.Equal(fields.Select(field => field.Path), collected);
    }

    [Fact]
    public void DesktopChangeDuringTransferRejectsMixedRevision()
    {
        var config = new RemoteConfigDto("远程每日", "rev2", [], [], "无", DateTimeOffset.UtcNow);
        Assert.Throws<ConfigConflictException>(() => ConfigPaging.Page(config, 0, 32, "rev1"));
        Assert.Throws<InvalidDataException>(() => ConfigPaging.Page(config, -1));
    }
}
