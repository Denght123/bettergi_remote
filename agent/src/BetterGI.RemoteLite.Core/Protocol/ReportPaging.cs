using System.Text.Json;
using BetterGI.RemoteLite.Reports;

namespace BetterGI.RemoteLite.Protocol;

public static class ReportPaging
{
    public static RunReportDto Page(RunReportDto report, int offset = 0, int limit = 24, string? runId = null)
    {
        if (runId is not null && runId != report.RunId)
            throw new InvalidOperationException("新的任务报告已生成，请重新读取报告。");
        var dictionaries = new[] { report.Rewards, report.DailyRewards, report.PickupObservations, report.WoodEstimates };
        var entries = dictionaries.SelectMany((map, category) => (map ?? new Dictionary<string,int>())
            .OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => (category, item.Key, item.Value))).ToArray();
        if (offset < 0 || offset > entries.Length || limit is < 1 or > 64)
            throw new InvalidDataException("报告读取位置无效，请重新同步。");
        var count = Math.Min(limit, entries.Length - offset);
        while (true)
        {
            var maps = Enumerable.Range(0, 4).Select(_ => new Dictionary<string,int>(StringComparer.Ordinal)).ToArray();
            foreach (var entry in entries.Skip(offset).Take(count)) maps[entry.category][entry.Key] = entry.Value;
            var page = report with
            {
                Rewards = maps[0], DailyRewards = maps[1], PickupObservations = maps[2], WoodEstimates = maps[3],
                // Raw diagnostics remain on the PC. Repeating every per-task item map in
                // each encrypted page adds no information to the aggregate report.
                LogExcerpt = [],
                Tasks = report.Tasks.Select(task => task with { Rewards = null, PickupObservations = null, WoodEstimates = null }).ToArray(),
                NextLootOffset = offset + count < entries.Length ? offset + count : null,
                TotalLootEntries = entries.Length,
            };
            if (JsonSerializer.SerializeToUtf8Bytes(page, RemoteJson.Options).Length < ProtocolConstants.MaximumPlaintextBytes - 2048) return page;
            if (count <= 1) throw new InvalidDataException("报告中的任务记录过多，暂时无法完整传到手机，请在电脑端查看本地报告。");
            count /= 2;
        }
    }

    public static RunProgressDto Progress(RunProgressDto progress) => progress with
    {
        Tasks = progress.Tasks?.Select(task => task with { Rewards = null, PickupObservations = null, WoodEstimates = null }).ToArray(),
    };
}
