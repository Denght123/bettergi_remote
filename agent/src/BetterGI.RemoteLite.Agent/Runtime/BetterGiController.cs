using System.Diagnostics;
using System.Globalization;
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
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private ActiveRun? _active;
    private string? _operationState;
    private string? _operationMessage;

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
        string? operationState;
        string? operationMessage;
        lock (_sync)
        {
            active = _active;
            operationState = _operationState;
            operationMessage = _operationMessage;
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
            active is null ? operationState ?? "idle" : active.StopRequested ? "stopping" : active.Acknowledged ? "running" : "starting",
            active?.RunId,
            active?.CurrentTask,
            DateTimeOffset.UtcNow,
            operationMessage ?? (active is { Acknowledged: false } ? "正在让 BetterGI 应用配置并启动" : version.Message),
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
        lock (_sync)
        {
            if (_active is not null)
            {
                reason = "已有远程任务正在运行。";
                return false;
            }
            if (_operationState is not null)
            {
                reason = _operationMessage ?? "正在处理上一次操作，请稍候。";
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
        return await MutateConfigurationAsync(token => CreateConfigStore().SynchronizeTasksFromSourceAsync(
            _settingsStore.Current.SourceConfigName, token), cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> MutateConfigurationAsync<T>(Func<CancellationToken, Task<T>> mutation, CancellationToken cancellationToken)
    {
        await EnterOperationAsync("updating", "正在让 BetterGI 应用配置", cancellationToken).ConfigureAwait(false);
        var executable = _settingsStore.Current.BetterGiExecutablePath;
        var reopen = false;
        try
        {
            reopen = await CloseIdleInstanceAsync(executable, cancellationToken).ConfigureAwait(false);
            return await mutation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (reopen && !IsBetterGiRunning(executable)) StartBetterGi(executable, null).Dispose();
            }
            finally { ExitOperation(); }
        }
    }

    public async Task<TaskAccepted> StartAsync(string expectedRevision, CancellationToken cancellationToken)
    {
        await EnterOperationAsync("starting", "正在让 BetterGI 应用配置并启动", cancellationToken).ConfigureAwait(false);
        var settings = _settingsStore.Current;
        var reopen = false;
        try
        {
            var configStore = CreateConfigStore();
            var config = await configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(config.Revision, expectedRevision, StringComparison.Ordinal))
                throw new ConfigConflictException(config.Revision);
            var enabledTasks = config.Tasks.Where(task => task.Enabled).OrderBy(task => task.Order).Select(task => task.Name).ToArray();
            if (enabledTasks.Length == 0) throw new InvalidOperationException("远程配置没有启用任何任务。");

            // 0.65/0.66 (and current upstream main) forward activation to HomePage,
            // which handles only plain "start"; task.* IPC is explicitly unregistered.
            // Reuse a verified registered shortcut for the correct selected config when
            // available; otherwise gracefully reopen to run the real CLI and reload caches.
            var shortcut = BetterGiStartShortcut.Read(settings.BetterGiExecutablePath, settings.RemoteConfigName);
            var useOpenInstance = shortcut is not null && IsBetterGiRunning(settings.BetterGiExecutablePath);
            reopen = await CloseIdleInstanceAsync(settings.BetterGiExecutablePath, cancellationToken, closeInstance: !useOpenInstance).ConfigureAwait(false);
            config = await configStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(config.Revision, expectedRevision, StringComparison.Ordinal))
                throw new ConfigConflictException(config.Revision);
            cancellationToken.ThrowIfCancellationRequested();

            var runId = Guid.NewGuid().ToString("N");
            var startedAt = DateTimeOffset.UtcNow;
            var parser = new BetterGiLogParser(runId, startedAt, enabledTasks);
            var logState = CaptureLogState(settings.BetterGiExecutablePath);
            var process = useOpenInstance
                ? Process.GetProcessesByName(Path.GetFileNameWithoutExtension(settings.BetterGiExecutablePath)).Single()
                : StartBetterGi(settings.BetterGiExecutablePath, settings.RemoteConfigName);
            if (useOpenInstance && !HotkeySender.Send(shortcut!))
            {
                process.Dispose();
                throw new InvalidOperationException("BetterGI 的一条龙快捷键发送失败，请检查电脑端的快捷键设置后重试。");
            }
            reopen = false;
            var active = new ActiveRun(runId, process, parser, startedAt, logState.Path, logState.Offset) { TaskCount = enabledTasks.Length };
            lock (_sync) { _active = active; }
            _ = MonitorRunAsync(active, settings);
            return new TaskAccepted(runId, startedAt);
        }
        finally
        {
            try
            {
                if (reopen && !IsBetterGiRunning(settings.BetterGiExecutablePath))
                    StartBetterGi(settings.BetterGiExecutablePath, null).Dispose();
            }
            finally { ExitOperation(); }
        }
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
        if (!active.Acknowledged)
        {
            throw new InvalidOperationException("BetterGI 还在启动，请待任务接收后再停止；此时不会发送可能影响其他程序的快捷键。");
        }
        if (!SessionProbe.IsUnlocked()) throw new InvalidOperationException("Windows 已锁屏，暂时无法发送取消快捷键。请解锁电脑后重试。");
        lock (_sync)
        {
            if (_active != active) return new TaskStopResult(runId, "idle", false);
            if (active.StopRequested) return new TaskStopResult(active.RunId, "stopping", false);
            active.StopRequested = true;
        }
        var settings = _settingsStore.Current;
        if (!HotkeySender.Send(settings.CancelHotkey))
        {
            active.StopRequested = false;
            throw new InvalidOperationException("取消快捷键无效或发送失败。");
        }
        var stopping = new RunProgressDto(active.RunId, "stopping", active.CurrentTask, active.LastProgress?.CompletedTasks ?? 0,
            active.TaskCount, "停止请求已发送，正在等待 BetterGI 确认", DateTimeOffset.UtcNow,
            CurrentStep: active.LastProgress?.CurrentStep, CurrentLocation: active.LastProgress?.CurrentLocation, Tasks: active.LastProgress?.Tasks);
        ProgressChanged?.Invoke(this, stopping);
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
                            if (!active.Acknowledged)
                            {
                                if (!BetterGiStartAcknowledgement.Matches(line, settings.RemoteConfigName)) continue;
                                active.Acknowledged = true;
                                active.LastProgress = new RunProgressDto(active.RunId, "running", null, 0,
                                    active.TaskCount, "BetterGI 已接收任务，正在准备游戏", DateTimeOffset.UtcNow);
                                ProgressChanged?.Invoke(this, active.LastProgress);
                            }
                            var update = active.Parser.AcceptLine(line, DateTimeOffset.UtcNow);
                            if (update.Progress is not null)
                            {
                                active.CurrentTask = update.Progress.CurrentTask;
                                active.LastProgress = active.StopRequested && !update.IsTerminal ? update.Progress with { State = "stopping" } : update.Progress;
                                ProgressChanged?.Invoke(this, active.LastProgress);
                            }
                            if (update.IsTerminal)
                            {
                                finalStatus = active.Parser.TerminalStatus ?? "unknown";
                                goto Completed;
                            }
                        }
                    }
                }

                if (!active.Acknowledged && DateTimeOffset.UtcNow - active.StartedAt > TimeSpan.FromSeconds(90))
                {
                    finalError = "BetterGI 没有确认接收远程配置。请在电脑端检查是否有启动提示、权限窗口或配置加载失败，然后重试。";
                    goto Completed;
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
            Debug.WriteLine(exception);
            finalError = "读取 BetterGI 的任务记录时出现问题，请检查电脑端是否仍在运行；本次结果暂时无法确认。";
        }

    Completed:
        var report = active.Parser.Finish(finalStatus, DateTimeOffset.UtcNow, finalError);
        try
        {
            var localDate = DateOnly.FromDateTime(report.FinishedAt.ToLocalTime().DateTime);
            var dailyRewards = await _reportStore.AggregateRewardsAsync(localDate, report).ConfigureAwait(false);
            report = report with { DailyRewards = dailyRewards, DailyRewardDate = localDate.ToString("yyyy-MM-dd") };
            await _reportStore.SaveAsync(report).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Debug.WriteLine(exception);
            report = report with { Errors = report.Errors.Append("本次任务已结束，但本地报告未能保存，今日累计数量暂时无法确认。请检查电脑磁盘和目录权限。").ToArray(), DailyRewards = null };
        }
        finally
        {
            lock (_sync)
            {
                if (_active == active) _active = null;
            }
            active.Completion.TrySetResult(report);
            active.Process.Dispose();
        }
        RunCompleted?.Invoke(this, report);
        // Notification delivery must not keep a finished run busy or delay the phone report.
        _ = SendNotificationsAsync(settings, report);
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

    private async Task EnterOperationAsync(string state, string message, CancellationToken cancellationToken)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("正在处理上一次操作，请稍候再试。");
        try
        {
            if (!CanMutate(out var reason)) throw new InvalidOperationException(reason);
            lock (_sync)
            {
                _operationState = state;
                _operationMessage = message;
            }
        }
        catch
        {
            _operationGate.Release();
            throw;
        }
    }

    private void ExitOperation()
    {
        lock (_sync)
        {
            _operationState = null;
            _operationMessage = null;
        }
        _operationGate.Release();
    }

    private static async Task<bool> CloseIdleInstanceAsync(string executable, CancellationToken cancellationToken, bool closeInstance = true)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable));
        try
        {
            var alive = processes.Where(process => !process.HasExited).ToArray();
            if (alive.Length == 0) return false;
            if (alive.Length > 1) throw new InvalidOperationException("检测到多个 BetterGI 实例，请先在电脑端结束分身或重复实例，再从手机启动。");
            var process = alive[0];
            string? runningPath;
            DateTime startedAt;
            try
            {
                runningPath = process.MainModule?.FileName;
                startedAt = process.StartTime;
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                throw new InvalidOperationException("无法确认 BetterGI 当前运行状态，请以相同权限运行 BetterGI Remote 后重试。", exception);
            }
            if (!string.Equals(runningPath, Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase) ||
                process.SessionId != Process.GetCurrentProcess().SessionId)
                throw new InvalidOperationException("当前运行的是其他位置或桌面会话的 BetterGI，请先在电脑端确认要使用的实例。");

            await EnsureIdleFromLogsAsync(executable, process.Id, startedAt, cancellationToken).ConfigureAwait(false);
            // Recheck after yielding: a task may have been started from the PC while
            // the phone was validating its configuration.
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            await EnsureIdleFromLogsAsync(executable, process.Id, startedAt, cancellationToken).ConfigureAwait(false);
            if (process.HasExited) return true;
            if (!closeInstance) return false;
            var globalPath = Path.Combine(Path.GetDirectoryName(executable)!, "User", "config.json");
            using (var config = System.Text.Json.JsonDocument.Parse(await File.ReadAllBytesAsync(globalPath, cancellationToken).ConfigureAwait(false)))
            {
                if (config.RootElement.TryGetProperty("commonConfig", out var common) &&
                    common.TryGetProperty("exitToTray", out var tray) && tray.ValueKind == System.Text.Json.JsonValueKind.True)
                    throw new InvalidOperationException("BetterGI 设置了关闭时最小化到托盘，当前版本不能自动让它重新载入配置。请在电脑的通用设置中关闭该选项后重试；程序和现有任务没有被强制结束。");
            }
            if (!BetterGiWindowCloser.RequestClose(process.Id, process.MainWindowHandle))
                throw new InvalidOperationException("BetterGI 当前无法正常应用配置，请在电脑端处理打开的对话框后重试。");
            // Once a close request has been sent, finish observing it even if the
            // phone disconnects, so the caller can reliably restore an idle window.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("BetterGI 尚未完成正常退出。请检查电脑端是否开启“关闭时最小化到托盘”，或是否有未处理的对话框、桌面分身；此时没有启动新任务。");
            }
            return true;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static async Task EnsureIdleFromLogsAsync(string executable, int processId, DateTime startedAt, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetDirectoryName(executable)!, "log");
        var tracker = new BetterGiActivityTracker(processId, startedAt);
        try
        {
            if (Directory.Exists(directory))
            {
                foreach (var path in Directory.EnumerateFiles(directory, "better-genshin-impact*.log").Order(StringComparer.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (name.Length < 8 || !DateOnly.TryParseExact(name[^8..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                        date < DateOnly.FromDateTime(startedAt)) continue;
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line) tracker.AcceptLine(line, date);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("暂时读不到 BetterGI 的运行记录，无法确认是否有电脑端任务；请稍后重试。", exception);
        }
        if (tracker.IsBusy)
            throw new InvalidOperationException("BetterGI 仍有电脑端任务或连续调度正在执行，请等待结束或先在电脑端停止，再从手机启动。");
        if (!tracker.HasEvidence)
            throw new InvalidOperationException("暂时无法确认 BetterGI 是否空闲，请等待启动完成，或在电脑端正常退出一次后重试。");
    }

    private static Process StartBetterGi(string executable, string? configName)
    {
        if (configName is not null && (configName.Contains('"') || configName.Contains('\r') || configName.Contains('\n')))
            throw new InvalidOperationException("远程配置名称包含无法用于启动的字符，请重新选择配置。");
        var info = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            Arguments = configName is null ? string.Empty : $"startOneDragon \"{configName}\"",
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
        public bool Acknowledged { get; set; }
        public bool StopRequested { get; set; }
        public RunProgressDto? LastProgress { get; set; }
        public int TaskCount { get; init; }
        public TaskCompletionSource<RunReportDto> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record LogState(string? Path, long Offset);
}
