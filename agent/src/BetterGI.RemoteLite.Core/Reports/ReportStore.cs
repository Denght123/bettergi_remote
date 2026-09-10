using System.Text.Json;
using BetterGI.RemoteLite.Protocol;

namespace BetterGI.RemoteLite.Reports;

public sealed class ReportStore(string directory)
{
    private readonly string _directory = Path.GetFullPath(directory);
    private readonly SemaphoreSlim _sync = new(1, 1);

    public async Task SaveAsync(RunReportDto report, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = Path.Combine(_directory, $"{report.StartedAt:yyyyMMdd_HHmmss}_{report.RunId}.json");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(report, new JsonSerializerOptions(RemoteJson.Options) { WriteIndented = true });
            var temporary = path + ".tmp";
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, true);
            Prune();
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<RunReportDto?> LatestAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory))
        {
            return null;
        }
        var path = Directory.EnumerateFiles(_directory, "*.json")
            .OrderByDescending(item => item, StringComparer.Ordinal)
            .FirstOrDefault();
        if (path is null)
        {
            return null;
        }
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<RunReportDto>(stream, RemoteJson.Options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, int>> AggregateRewardsAsync(DateOnly localDate, RunReportDto current, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        var totals = new Dictionary<string, long>(StringComparer.CurrentCulture);
        AddRewards(totals, current.Rewards);
        if (!Directory.Exists(_directory))
        {
            return ToIntTotals(totals);
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var path in Directory.EnumerateFiles(_directory, "*.json").OrderByDescending(item => item, StringComparer.Ordinal).Take(100))
            {
                try
                {
                    await using var stream = File.OpenRead(path);
                    var report = await JsonSerializer.DeserializeAsync<RunReportDto>(stream, RemoteJson.Options, cancellationToken).ConfigureAwait(false);
                    if (report is null || string.Equals(report.RunId, current.RunId, StringComparison.Ordinal) ||
                        DateOnly.FromDateTime(report.FinishedAt.ToLocalTime().DateTime) != localDate)
                    {
                        continue;
                    }
                    AddRewards(totals, report.Rewards);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
                {
                    // A damaged historical report must not hide today's valid reward total.
                }
            }
            return ToIntTotals(totals);
        }
        finally
        {
            _sync.Release();
        }
    }

    private static void AddRewards(Dictionary<string, long> totals, IReadOnlyDictionary<string, int> rewards)
    {
        foreach (var reward in rewards)
        {
            if (reward.Value <= 0 || string.IsNullOrWhiteSpace(reward.Key)) continue;
            totals[reward.Key] = totals.GetValueOrDefault(reward.Key) + reward.Value;
        }
    }

    private static IReadOnlyDictionary<string, int> ToIntTotals(Dictionary<string, long> totals)
        => totals.ToDictionary(
            item => item.Key,
            item => (int)Math.Min(int.MaxValue, item.Value),
            StringComparer.CurrentCulture);

    private void Prune()
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var files = Directory.EnumerateFiles(_directory, "*.json")
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.Name, StringComparer.Ordinal)
            .ToArray();
        foreach (var info in files.Where((info, index) => index >= 100 || info.LastWriteTimeUtc < cutoff))
        {
            info.Delete();
        }
    }
}
