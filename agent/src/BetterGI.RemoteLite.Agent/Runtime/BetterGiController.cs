using System.Diagnostics;
using System.Text;
using BetterGI.RemoteLite.Agent.Native;
using BetterGI.RemoteLite.Agent.Storage;
using BetterGI.RemoteLite.BetterGi;
using BetterGI.RemoteLite.Notifications;
using BetterGI.RemoteLite.Protocol;
using BetterGI.RemoteLite.Reports;

namespace BetterGI.RemoteLite.Agent.Runtime;

internal sealed class BetterGiController
{
    private static readonly string[] GameProcessNames = ["YuanShen", "GenshinImpact", "GenshinImpactCloudGame"];
    private readonly AgentSettingsStore _settingsStore;
    private readonly ReportStore _reportStore;
    private readonly FeishuNotifier _feishu;
    private readonly QqEmailNotifier _qqEmail = new();
    private readonly object _sync = new();
    private ActiveRun? _active;

    public BetterGiController(AgentSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        _reportStore = new ReportStore(AgentSettingsStore.ReportsDirectory);
        _feishu = new FeishuNotifier(new HttpClient { Timeout = TimeSpan.FromSeconds(15) });
    }

    public event EventHandler<RunProgressDto>? ProgressChanged;
    public event EventHandler<RunReportDto>? RunCompleted;

    public ReportStore Reports => _reportStore;

    public AgentStatusDto GetStatus(bool peerOnline)
    {
        var settings = _settingsStore.Current;
        var version = BetterGiVersionPolicy.Check(settings.BetterGiExecutablePath);
        var latestVersion = Version.TryParse(settings.LatestKnownBetterGiVersion, out var latest) ? latest : null;
        var installedVersion = Version.TryParse(version.Version, out var installed) ? installed : null;
        ActiveRun? active;
        lock (_sync)
        {
            active = _active;
        }
        return new AgentStatusDto(
            Environment.MachineName,
            settings.PcDeviceId,
            peerOnline,
            SessionProbe.IsUnlocked(),
            version.Configured,
            version.Supported,
            version.Version,
            IsBetterGiRunning(settings.BetterGiExecutablePath),
            GameProcessNames.Any(name => Process.GetProcessesByName(name).Length > 0),
            active is null ? "idle" : "running",
            active?.RunId,
            active?.CurrentTask,
            DateTimeOffset.UtcNow,
            version.Message,
            BetterGiCompatibilityVerified: version.Verified,
            BetterGiLatestVersion: latestVersion?.ToString(),
            BetterGiUpdateAvailable: latestVersion is not null && installedVersion is not null && latestVersion > installedVersion);
    }

    public bool CanMutate(out string? reason)
    {
        var settings = _settingsStore.Current;
        var version = BetterGiVersionPolicy.Check(settings.BetterGiExecutablePath);
        if (!version.Configured || !version.Supported)
        {
            reason = version.Message ?? "BetterGI 未配置。";
            return false;
        }
        if (!SessionProbe.IsUnlocked())
        {
            reason = "Windows 当前已锁屏。";
            return false;
        }
        if (IsBetterGiRunning(settings.BetterGiExecutablePath))
        {
            reason = "请先关闭 BetterGI，再修改配置或开始任务。";
            return false;
        }
        lock (_sync)
        {
            if (_active is not null)
            {
                reason = "已有远程任务正在运行。";
                return false;
            }
        }
        reason = null;
        return true;
    }

    public RemoteConfigStore CreateConfigStore()
    {
        var settings = _settingsStore.Current;
        return new RemoteConfigStore(settings.BetterGiExecutablePath, settings.RemoteConfigName);
    }

    public async Task<RemoteConfigDto> SyncConfigAsync(CancellationToken cancellationToken)
    {
        if (!CanMutate(out var reason))
        {
            throw new InvalidOperationException(reason);
        }
        var settings = _settingsStore.Current;
        return await CreateConfigStore().SynchronizeTasksFromSourceAsync(settings.SourceConfigName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TaskAccepted> StartAsync(string expectedRevision, CancellationToken cancellationToken)
    {
        if (!CanMutate(out var reason))
        {
            throw new InvalidOperationException(reason);
        }
        var settings = _settingsStore.Current;
        var configStore = CreateConfigStore();
        var config = await configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(config.Revision, expectedRevision, StringComparison.Ordinal))
        {
            throw new ConfigConflictException(config.Revision);
        }
        var enabledTasks = config.Tasks.Where(task => task.Enabled).OrderBy(task => task.Order).Select(task => task.Name).ToArray();
        if (enabledTasks.Length == 0)
        {
            throw new InvalidOperationException("远程配置没有启用任何任务。");
        }

        var runId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        var parser = new BetterGiLogParser(runId, startedAt, enabledTasks);
        var logState = CaptureLogState(settings.BetterGiExecutablePath);
        var process = StartBetterGi(settings.BetterGiExecutablePath, settings.RemoteConfigName);
        var active = new ActiveRun(runId, process, parser, startedAt, logState.Path, logState.Offset);
        lock (_sync)
        {
            _active = active;
        }
        _ = MonitorRunAsync(active, settings);
        return new TaskAccepted(runId, startedAt);
    }

    public async Task<TaskStopResult> StopAsync(string? runId, CancellationToken cancellationToken)
    {
        ActiveRun? active;
        lock (_sync)
        {
            active = _active;
        }
        if (active is null || (runId is not null && !string.Equals(active.RunId, runId, StringComparison.Ordinal)))
        {
            return new TaskStopResult(runId, "idle", false);
        }
        var settings = _settingsStore.Current;
        if (!HotkeySender.Send(settings.CancelHotkey))
        {
            throw new InvalidOperationException("取消快捷键无效或发送失败。");
        }
        var completed = await Task.WhenAny(active.Completion.Task, Task.Delay(TimeSpan.FromSeconds(15), cancellationToken)).ConfigureAwait(false);
        return completed == active.Completion.Task
            ? new TaskStopResult(active.RunId, active.Completion.Task.Result.Status, true)
            : new TaskStopResult(active.RunId, "unknown", true);
    }

    private async Task MonitorRunAsync(ActiveRun active, AgentSettings settings)
    {
        var pending = string.Empty;
        var finalStatus = "failed";
        string? finalError = null;
        try
        {
            while (DateTimeOffset.UtcNow - active.StartedAt < TimeSpan.FromHours(6))
            {
                await Task.Delay(1000).ConfigureAwait(false);
                var latest = CaptureLogState(settings.BetterGiExecutablePath);
                if (!string.Equals(latest.Path, active.LogPath, StringComparison.OrdinalIgnoreCase))
                {
                    active.LogPath = latest.Path;
                    active.LogOffset = 0;
                    pending = string.Empty;
                }
                if (active.LogPath is not null && File.Exists(active.LogPath))
                {
                    var chunk = await ReadNewTextAsync(active).ConfigureAwait(false);
                    if (chunk.Length > 0)
                    {
                        var combined = pending + chunk;
                        var lines = combined.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
                        pending = lines[^1];
                        foreach (var line in lines[..^1])
                        {
                            var update = active.Parser.AcceptLine(line, DateTimeOffset.UtcNow);
                            if (update.Progress is not null)
                            {
                                active.CurrentTask = update.Progress.CurrentTask;
                                ProgressChanged?.Invoke(this, update.Progress);
                            }
                            if (update.IsTerminal)
                            {
                                finalStatus = active.Parser.TerminalStatus ?? "unknown";
                                goto Completed;
                            }
                        }
                    }
                }

                // BetterGI 0.64.x may hand the startOneDragon command off to its primary
                // instance and let the Process.Start handle exit. Keep following the log
                // while that real instance is still alive; otherwise successful runs are
                // reported as failed during the game's startup delay.
                if (active.Process.HasExited &&
                    DateTimeOffset.UtcNow - active.StartedAt > TimeSpan.FromSeconds(10) &&
                    !IsBetterGiRunning(settings.BetterGiExecutablePath))
                {
                    finalError = "BetterGI 进程在输出任务完成标志前退出。";
                    finalStatus = "failed";
                    goto Completed;
                }
            }
            finalStatus = "failed";
            finalError = "任务监控超过六小时，结果无法确认。";
        }
        catch (Exception exception)
        {
            finalStatus = "failed";
            finalError = "任务监控异常: " + exception.Message;
        }

    Completed:
        var report = active.Parser.Finish(finalStatus, DateTimeOffset.UtcNow, finalError);
        var localDate = DateOnly.FromDateTime(report.FinishedAt.ToLocalTime().DateTime);
        var dailyRewards = await _reportStore.AggregateRewardsAsync(localDate, report).ConfigureAwait(false);
        report = report with
        {
            DailyRewards = dailyRewards,
            DailyRewardDate = localDate.ToString("yyyy-MM-dd"),
        };
        await _reportStore.SaveAsync(report).ConfigureAwait(false);
        await SendNotificationsAsync(settings, report).ConfigureAwait(false);
        lock (_sync)
        {
            if (_active == active)
            {
                _active = null;
            }
        }
        active.Completion.TrySetResult(report);
        RunCompleted?.Invoke(this, report);
        active.Process.Dispose();
    }

    private async Task TrySendFeishuAsync(AgentSettings settings, RunReportDto report)
    {
        try
        {
            var webhook = SecretProtector.Unprotect(settings.ProtectedFeishuWebhook);
            if (!string.IsNullOrWhiteSpace(webhook))
            {
                await _feishu.SendReportAsync(webhook, SecretProtector.Unprotect(settings.ProtectedFeishuSigningSecret), report).ConfigureAwait(false);
            }
        }
        catch
        {
            // The local report remains authoritative when notification delivery fails.
        }
    }

    private async Task TrySendQqEmailAsync(AgentSettings settings, RunReportDto report)
    {
        try
        {
            var sender = SecretProtector.Unprotect(settings.ProtectedQqEmailAddress);
            var code = SecretProtector.Unprotect(settings.ProtectedQqSmtpAuthorizationCode);
            var recipient = SecretProtector.Unprotect(settings.ProtectedNotificationRecipient);
            if (!string.IsNullOrWhiteSpace(sender) && !string.IsNullOrWhiteSpace(code))
            {
                await _qqEmail.SendReportAsync(new QqSmtpSettings(sender, code, string.IsNullOrWhiteSpace(recipient) ? sender : recipient), report).ConfigureAwait(false);
            }
        }
        catch
        {
            // Notification failure never changes the authoritative local report.
        }
    }

    private async Task SendNotificationsAsync(AgentSettings settings, RunReportDto report)
    {
        await Task.WhenAll(TrySendFeishuAsync(settings, report), TrySendQqEmailAsync(settings, report)).ConfigureAwait(false);
    }

    private static Process StartBetterGi(string executable, string configName)
    {
        var safeName = configName.Replace("\"", string.Empty, StringComparison.Ordinal);
        var info = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            Arguments = $"startOneDragon \"{safeName}\"",
            UseShellExecute = true,
        };
        return Process.Start(info) ?? throw new InvalidOperationException("BetterGI 启动失败。");
    }

    private static bool IsBetterGiRunning(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return false;
        }
        var expected = Path.GetFullPath(executable);
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static LogState CaptureLogState(string executable)
    {
        var directory = Path.Combine(Path.GetDirectoryName(executable) ?? string.Empty, "log");
        if (!Directory.Exists(directory))
        {
            return new LogState(null, 0);
        }
        var path = Directory.EnumerateFiles(directory, "better-genshin-impact*.log")
            .Select(item => new FileInfo(item))
            .OrderByDescending(item => item.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
        return path is null ? new LogState(null, 0) : new LogState(path, new FileInfo(path).Length);
    }

    private static async Task<string> ReadNewTextAsync(ActiveRun active)
    {
        await using var stream = new FileStream(active.LogPath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (active.LogOffset > stream.Length)
        {
            active.LogOffset = 0;
        }
        stream.Position = active.LogOffset;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync().ConfigureAwait(false);
        active.LogOffset = stream.Position;
        return text;
    }

    private sealed class ActiveRun(string runId, Process process, BetterGiLogParser parser, DateTimeOffset startedAt, string? logPath, long logOffset)
    {
        public string RunId { get; } = runId;
        public Process Process { get; } = process;
        public BetterGiLogParser Parser { get; } = parser;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public string? LogPath { get; set; } = logPath;
        public long LogOffset { get; set; } = logOffset;
        public string? CurrentTask { get; set; }
        public TaskCompletionSource<RunReportDto> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record LogState(string? Path, long Offset);
}
