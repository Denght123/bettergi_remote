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

