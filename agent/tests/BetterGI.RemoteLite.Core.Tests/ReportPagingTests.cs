using System.Text.Json;
using BetterGI.RemoteLite.Protocol;

namespace BetterGI.RemoteLite.Core.Tests;

public sealed class ReportPagingTests
{
    [Fact]
    public void LongMaterialsReportTransfersCompletelyWithoutRawDiagnostics()
    {
        var materials = Enumerable.Range(0,160).ToDictionary(i => new string('材',80) + i, i => i + 1);
        var report = new RunReportDto("run", "success", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, [], materials, null, [], ["raw stack"],
            DailyRewards: materials, PickupObservations: materials, WoodEstimates: materials);
        var totals = new[] { new Dictionary<string,int>(), new Dictionary<string,int>(), new Dictionary<string,int>(), new Dictionary<string,int>() };
        var offset = 0;
        do
        {
            var page = ReportPaging.Page(report, offset);
            Assert.Empty(page.LogExcerpt);
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(page, RemoteJson.Options).Length < ProtocolConstants.MaximumPlaintextBytes - 2048);
            var maps = new[] { page.Rewards, page.DailyRewards, page.PickupObservations, page.WoodEstimates };
            for(var i=0;i<4;i++) foreach(var pair in maps[i]!) totals[i].Add(pair.Key,pair.Value);
            if(page.NextLootOffset is null) break;
            Assert.True(page.NextLootOffset > offset);
            offset = page.NextLootOffset.Value;
        } while(true);
        Assert.All(totals, map => Assert.Equal(materials.OrderBy(x => x.Key), map.OrderBy(x => x.Key)));
        Assert.Throws<InvalidOperationException>(() => ReportPaging.Page(report, 0, 24, "another-run"));
    }
}
