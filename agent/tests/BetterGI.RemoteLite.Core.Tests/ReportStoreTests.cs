using BetterGI.RemoteLite.Protocol;
using BetterGI.RemoteLite.Reports;

namespace BetterGI.RemoteLite.Core.Tests;

public sealed class ReportStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgrl-report-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AggregatesRewardsFromRunsOnSameLocalDay()
    {
        var store = new ReportStore(_directory);
        var morning = Report("morning", new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.FromHours(8)), new Dictionary<string, int> { ["摩拉"] = 10000 });
        var evening = Report("evening", new DateTimeOffset(2026, 9, 10, 20, 0, 0, TimeSpan.FromHours(8)), new Dictionary<string, int> { ["摩拉"] = 3525, ["天授之馨"] = 1 });
        var yesterday = Report("yesterday", new DateTimeOffset(2026, 9, 9, 20, 0, 0, TimeSpan.FromHours(8)), new Dictionary<string, int> { ["摩拉"] = 99999 });
        await store.SaveAsync(morning);
        await store.SaveAsync(yesterday);

        var totals = await store.AggregateRewardsAsync(new DateOnly(2026, 9, 10), evening);

        Assert.Equal(13525, totals["摩拉"]);
        Assert.Equal(1, totals["天授之馨"]);
    }

    private static RunReportDto Report(string id, DateTimeOffset finishedAt, IReadOnlyDictionary<string, int> rewards)
        => new(id, "success", finishedAt.AddMinutes(-10), finishedAt, 600, [], rewards, null, [], []);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
