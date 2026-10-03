using System.Text.RegularExpressions;
using BetterGI.RemoteLite.Protocol;

namespace BetterGI.RemoteLite.Reports;

public sealed partial class BetterGiLogParser
{
    private readonly string _runId;
    private readonly DateTimeOffset _startedAt;
    private readonly List<MutableTaskResult> _tasks;
    private readonly Dictionary<string, int> _rewards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _pickupObservations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _woodEstimates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _woodSessionTotals = new(StringComparer.Ordinal);
    private readonly List<string> _errors = [];
    private readonly Queue<string> _excerpt = new();
    private int _currentTaskIndex = -1;
    private string? _currentScript;
    private string? _currentStep;
    private string? _currentLocation;
    private string? _dailyRewardStatus;
    private string? _terminalStatus;
    private bool _pendingError;
    private bool _rewardRecognitionAttempted;
    private int _rewardRecognitionSuccesses;
    private int _rewardRecognitionFailures;
    private bool _lootTruncated;

    public BetterGiLogParser(string runId, DateTimeOffset startedAt, IReadOnlyList<string> enabledTasks)
    {
        _runId = runId;
        _startedAt = startedAt;
        _tasks = enabledTasks.Select(name => new MutableTaskResult(name)).ToList();
    }

    public bool IsTerminal => _terminalStatus is not null;
    public string? TerminalStatus => _terminalStatus;
    public string? CurrentStep => _currentStep;
    public string? CurrentLocation => _currentLocation;

    public LogParseUpdate AcceptLine(string line, DateTimeOffset observedAt)
    {
        if (string.IsNullOrWhiteSpace(line) || IsTerminal) return new LogParseUpdate(null, IsTerminal);
        AddExcerpt(line);
        var text = line.Trim();
        var header = LogHeaderRegex().Match(text);
        var isError = _pendingError;
        if (header.Success)
        {
            isError = header.Groups[1].Value is "ERR" or "FTL";
            text = text[header.Length..].Trim();
            // BetterGI writes SourceContext on a header line and the message on the next.
            if (text.Length == 0 || SourceContextRegex().IsMatch(text))
            {
                _pendingError = isError;
                return new LogParseUpdate(null, false);
            }
        }
        _pendingError = false;
        if (_currentScript is not null && CurrentTask is { State: "failed" } failed && ScriptLineRegex().Match(text) is { Success: true } scriptLine)
        {
            var previous = failed.Message;
            var number = scriptLine.Groups[1].Success ? scriptLine.Groups[1].Value : scriptLine.Groups[2].Value;
            _currentLocation = _currentScript + " · 第 " + number + " 行";
            failed.Location = _currentLocation;
            failed.Message = $"{failed.Name} / {_currentLocation}：脚本在此处执行失败，请检查对应参数或脚本内容。";
            var errorIndex = previous is null ? -1 : _errors.IndexOf(previous);
            if (errorIndex >= 0) _errors[errorIndex] = failed.Message;
            return new LogParseUpdate(BuildProgress(observedAt, failed.Message), false);
        }
        if (StackLineRegex().IsMatch(text)) return new LogParseUpdate(null, false);

        var changed = false;
        string? message = null;
        if (TaskOrdinalRegex().IsMatch(text))
        {
            // Built-ins and groups have independent counters, not combined-list indices.
            changed = StartTask(_currentTaskIndex + 1);
            message = CurrentTask is null ? "正在开始下一项任务" : $"开始执行：{CurrentTask.Name}";
        }

        var group = GroupStartRegex().Match(text);
        if (group.Success)
        {
            var name = DisplayName(group.Groups[1].Value);
            var index = CurrentTask?.Name == name ? _currentTaskIndex : _tasks.FindIndex(_currentTaskIndex + 1, task => task.Name == name);
            if (index >= 0) changed |= StartTask(index);
            changed |= SetStep("准备配置组脚本");
            message = $"配置组“{name}”已加载，准备执行";
        }

        var script = ScriptStartRegex().Match(text);
        if (script.Success)
        {
            _currentScript = DisplayName(script.Groups[2].Value);
            _currentLocation = _currentScript;
            changed |= SetStep(script.Groups[1].Value switch
            {
                "地图追踪任务" => "沿路线移动与采集",
                "键鼠脚本" => "执行键鼠操作",
                _ => "执行脚本",
            });
            message = $"正在执行：{_currentScript}";
        }

        if (text.Contains("自动伐木，启动", StringComparison.Ordinal))
        {
            _woodSessionTotals.Clear();
            changed |= SetStep("准备伐木");
        }
        var step = PhaseFromLog(text);
        if (step is not null)
        {
            changed |= SetStep(step);
            message = step;
        }
        var destination = DestinationRegex().Match(text);
        if (destination.Success)
        {
            _currentLocation = DisplayName(destination.Groups[1].Value);
            changed |= SetStep("前往任务地点");
            message = $"前往：{_currentLocation}";
        }

        if (text.Contains("检查每日奖励：已领取", StringComparison.Ordinal) || text.Contains("今日奖励已领取", StringComparison.Ordinal))
        {
            _dailyRewardStatus = "已领取";
            changed = true;
            message = "每日奖励已领取";
        }
        else if (text.Contains("检查到每日奖励未领取", StringComparison.Ordinal) || text.Contains("今日奖励未领取", StringComparison.Ordinal))
        {
            _dailyRewardStatus = "未领取";
            var task = _tasks.LastOrDefault(item => item.Name == "领取每日奖励");
            if (task is not null && task.State != "failed")
            {
                task.State = "warning";
                task.Message = "BetterGI 检查到每日奖励未领取";
            }
            changed = true;
            message = "每日奖励尚未领取，请检查任务结果";
        }

        if (text.Contains("开始奖励识别", StringComparison.Ordinal)) _rewardRecognitionAttempted = true;
        var recognitionFailed = text.Contains("奖励识别失败", StringComparison.Ordinal) ||
                                text.Contains("奖励识别结果为空", StringComparison.Ordinal) ||
                                text.Contains("已跳过本轮奖励识别", StringComparison.Ordinal);
        if (recognitionFailed)
        {
            _rewardRecognitionAttempted = true;
            _rewardRecognitionFailures++;
            changed = true;
            message = "本轮奖励没有识别完整，任务仍可继续";
        }

        var reward = RewardRegex().Match(text);
        if (reward.Success)
        {
            var parsedAny = false;
            foreach (Match item in RewardItemRegex().Matches(reward.Groups[1].Value))
            {
                var name = DisplayName(item.Groups[1].Value);
                if (name.Length == 0 || !int.TryParse(item.Groups[2].Value, out var count) || count <= 0) continue;
                AddCount(_rewards, name, count);
                if (CurrentTask is { } current) AddCount(current.Rewards, name, count);
                parsedAny = true;
            }
            _rewardRecognitionAttempted = true;
            if (parsedAny) _rewardRecognitionSuccesses++; else _rewardRecognitionFailures++;
            changed = true;
            message = parsedAny ? "本轮获得的物品已记录" : "本轮奖励数量暂时无法确认";
        }

        var wood = WoodTotalRegex().Match(text);
        if (wood.Success && int.TryParse(wood.Groups[2].Value, out var woodCount) && woodCount >= 0)
        {
            var name = DisplayName(wood.Groups[1].Value);
            var previous = _woodSessionTotals.GetValueOrDefault(name);
            // Cumulative estimates: credit only growth; only a start marker resets them.
            if (name.Length > 0 && woodCount > previous)
            {
                var delta = woodCount - previous;
                _woodSessionTotals[name] = woodCount;
                AddCount(_woodEstimates, name, delta);
                if (CurrentTask is { } current) AddCount(current.WoodEstimates, name, delta);
                changed = true;
                message = $"木材估算已更新：{name}";
            }
        }

        var pickup = PickupRegex().Match(text);
        if (pickup.Success)
        {
            var name = DisplayName(pickup.Groups[1].Value);
            if (name.Length > 0)
            {
                // AutoPick logs a key press, including dialogue/options, not a receipt.
                AddCount(_pickupObservations, name, 1);
                if (CurrentTask is { } current) AddCount(current.PickupObservations, name, 1);
                changed = true;
                message = $"交互或拾取记录：{name}";
            }
        }

        if (!recognitionFailed && (isError || KnownFailureRegex().IsMatch(text)))
        {
            message = RecordFailure(text);
            changed = true;
        }
        if (ScriptEndRegex().IsMatch(text))
        {
            message = $"脚本“{_currentScript ?? "当前脚本"}”已结束";
            _currentScript = null;
            changed |= SetStep("等待下一项脚本");
        }
        if (text.Contains("一条龙和配置组任务结束", StringComparison.Ordinal))
        {
            CompleteCurrentTask();
            _terminalStatus = _errors.Count == 0 ? "success" : "failed";
            changed = true;
            message = _errors.Count == 0 ? "所有任务已结束" : "任务已结束，部分步骤未完成，请查看报告";
        }
        else if (CancelRegex().IsMatch(text))
        {
            if (CurrentTask is { State: "running" } current) current.State = "stopped";
            _terminalStatus = "stopped";
            changed = true;
            message = "任务已停止";
        }
        return new LogParseUpdate(changed ? BuildProgress(observedAt, message ?? _currentStep) : null, IsTerminal);
    }

    public RunReportDto Finish(string status, DateTimeOffset finishedAt, string? error = null)
    {
        if (error is not null) RecordFailure(error);
        _terminalStatus ??= status;
        if (_terminalStatus == "success") CompleteCurrentTask();
        else if (CurrentTask is { State: "running" } current) current.State = _terminalStatus == "stopped" ? "stopped" : "failed";
        if (_terminalStatus is "stopped" or "failed")
            foreach (var task in _tasks.Where(task => task.State == "pending")) task.State = "skipped";
        return new RunReportDto(_runId, _terminalStatus, _startedAt, finishedAt,
            Math.Max(0, (finishedAt - _startedAt).TotalSeconds), TaskSnapshot(), Snapshot(_rewards),
            _dailyRewardStatus, _errors.ToArray(), _excerpt.ToArray(), ParserVersion: "adaptive-v3",
            RewardRecognitionStatus: RewardRecognitionStatus(),
            PickupObservations: Snapshot(_pickupObservations), WoodEstimates: Snapshot(_woodEstimates),
            LootCoverage: "奖励数量来自 BetterGI 奖励识别；木材为 BetterGI 画面识别与轮次估算；交互或拾取仅表示操作记录，可能包含对话，不代表实际入包数量。未提供数量的采集物、邮件和其他物品不按零计算。" +
                (_lootTruncated ? " 物品种类达到记录容量上限，本次统计不完整，请查看电脑端记录。" : ""));
    }

    private MutableTaskResult? CurrentTask => _currentTaskIndex >= 0 && _currentTaskIndex < _tasks.Count ? _tasks[_currentTaskIndex] : null;
    private bool StartTask(int index)
    {
        if (index < 0 || index >= _tasks.Count || index == _currentTaskIndex) return false;
        CompleteCurrentTask();
        _currentTaskIndex = index;
        _currentScript = null;
        _currentStep = "准备执行";
        _currentLocation = null;
        _woodSessionTotals.Clear();
        if (_tasks[index].State == "pending") _tasks[index].State = "running";
        _tasks[index].Step = _currentStep;
        return true;
    }
    private void CompleteCurrentTask()
    {
        if (CurrentTask is { State: "running" } current) current.State = "success";
    }
    private bool SetStep(string step)
    {
        var changed = _currentStep != step || CurrentTask?.Location != _currentLocation;
        _currentStep = step;
        if (CurrentTask is { } current && current.State != "failed")
        {
            current.Step = step;
            current.Location = _currentLocation;
        }
        return changed;
    }
    private string RecordFailure(string text)
    {
        var context = string.Join(" / ", new[] { CurrentTask?.Name, _currentLocation, _currentStep }.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct());
        var message = (context.Length > 0 ? context + "：" : "任务启动或准备阶段：") + FriendlyTaskError.Describe(text);
        if (!_errors.Contains(message, StringComparer.Ordinal) && _errors.Count < 30) _errors.Add(message);
        if (CurrentTask is { } current)
        {
            if (current.State != "failed")
            {
                current.Message = message;
                current.Step = _currentStep;
                current.Location = _currentLocation;
            }
            current.State = "failed";
        }
        return message;
    }
    private string? RewardRecognitionStatus()
    {
        if (!_rewardRecognitionAttempted) return "本次任务未提供奖励数量识别结果，未记录不代表没有获得物品";
        if (_rewardRecognitionSuccesses > 0 && _rewardRecognitionFailures == 0) return "BetterGI 奖励识别完成";
        if (_rewardRecognitionSuccesses > 0) return "部分轮次识别失败，今日数量可能不完整";
        return "BetterGI 未能识别奖励，请检查画面分辨率和奖励识别设置";
    }
    public RunProgressDto BuildProgress(DateTimeOffset observedAt, string? message = null)
        => new(_runId, _terminalStatus ?? "running", CurrentTask?.Name,
            _tasks.Count(task => task.State is "success" or "warning" or "failed" or "stopped"), _tasks.Count,
            message ?? _currentStep, observedAt, CurrentStep: _currentStep, CurrentLocation: _currentLocation, Tasks: TaskSnapshot());
    private RunTaskResult[] TaskSnapshot() => _tasks.Select(task => new RunTaskResult(task.Name, task.State, task.Message,
        Step: task.Step, Location: task.Location, Rewards: Snapshot(task.Rewards),
        PickupObservations: Snapshot(task.PickupObservations), WoodEstimates: Snapshot(task.WoodEstimates))).ToArray();
    private static Dictionary<string, int> Snapshot(Dictionary<string, int> values) => new(values, StringComparer.Ordinal);
    private void AddCount(Dictionary<string, int> values, string name, int count)
    {
        if (values.Count >= 2048 && !values.ContainsKey(name)) { _lootTruncated = true; return; }
        values[name] = (int)Math.Min(int.MaxValue, (long)values.GetValueOrDefault(name) + count);
    }
    private void AddExcerpt(string line)
    {
        _excerpt.Enqueue(line.Length <= 500 ? line.Trim() : line[..500].Trim());
        while (_excerpt.Count > 40) _excerpt.Dequeue();
    }
    private static string DisplayName(string name)
    {
        var value = name.Trim(' ', ',', '，', '"', '“', '”');
        return value.Length <= 80 ? value : value[..80];
    }
    private static string? PhaseFromLog(string text)
    {
        if (text.Contains("走到钥匙处启动", StringComparison.Ordinal)) return "启动秘境挑战";
        if (text.Contains("执行战斗策略", StringComparison.Ordinal)) return "执行战斗";
        if (text.Contains("寻找石化古树", StringComparison.Ordinal)) return "寻找秘境领奖位置";
        if (text.Contains("走到石化古树", StringComparison.Ordinal)) return "前往秘境领奖位置";
        if (text.Contains("领取奖励", StringComparison.Ordinal) && !text.Contains("异常", StringComparison.Ordinal)) return "领取奖励";
        if (text.Contains("开始奖励识别", StringComparison.Ordinal)) return "识别本轮获得物品";
        if (text.Contains("切换队伍", StringComparison.Ordinal) && !text.Contains("失败", StringComparison.Ordinal)) return "切换任务队伍";
        if (text.Contains("小范围内自动拾取", StringComparison.Ordinal) || text.Contains("长按E转圈拾取", StringComparison.Ordinal)) return "采集与拾取周边物品";
        if (text.Contains("疑似卡死", StringComparison.Ordinal)) return "角色可能卡住，正在尝试脱离";
        if (text.Contains("传送失败，重试", StringComparison.Ordinal)) return "传送未成功，正在重试";
        if (text.Contains("前往七天神像复活", StringComparison.Ordinal)) return "前往七天神像恢复队伍";
        if (text.Contains("邮件：", StringComparison.Ordinal)) return "领取邮件物品";
        if (WoodRoundRegex().Match(text) is { Success: true } wood) return $"进行第 {wood.Groups[1].Value} 次伐木";
        if (text.Contains("开始第", StringComparison.Ordinal) && text.Contains("次讨伐", StringComparison.Ordinal)) return "开始下一轮首领讨伐";
        return null;
    }
    private sealed class MutableTaskResult(string name)
    {
        public string Name { get; } = name;
        public string State { get; set; } = "pending";
        public string? Message { get; set; }
        public string? Step { get; set; }
        public string? Location { get; set; }
        public Dictionary<string, int> Rewards { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> PickupObservations { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> WoodEstimates { get; } = new(StringComparer.Ordinal);
    }

    [GeneratedRegex(@"^(?:\[[\d:. ]+\]\s*)?\[(?:[\d:. ]+\s)?(INF|WRN|ERR|FTL|DBG)\]\s*(?:\[[A-Za-z][A-Za-z0-9]*:S\d+:P\d+:T\d+\]\s*)?", RegexOptions.CultureInvariant)]
    private static partial Regex LogHeaderRegex();
    [GeneratedRegex(@"^(?:BetterGenshinImpact|System|Microsoft)\.[\w.]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceContextRegex();
    [GeneratedRegex(@"^(?:at\s+|在\s+|---|[A-Za-z_][\w.]*Exception(?:\s*:|$))", RegexOptions.CultureInvariant)]
    private static partial Regex StackLineRegex();
    [GeneratedRegex(@"(?:\.js:(\d+)(?::\d+)?|\bline\s*[:=]?\s*(\d+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptLineRegex();
    [GeneratedRegex(@"^(?:一条龙任务执行|配置组任务执行)[:：]\s*\d+\s*/\s*\d+", RegexOptions.CultureInvariant)]
    private static partial Regex TaskOrdinalRegex();
    [GeneratedRegex("^配置组\\s*[\"“]?(.+?)[\"”]?\\s*(?:加载完成，|启动)", RegexOptions.CultureInvariant)]
    private static partial Regex GroupStartRegex();
    [GeneratedRegex("^→\\s*开始执行(地图追踪任务|JS脚本|键鼠脚本)[:：]\\s*[\"“]?(.+?)[\"”]?$", RegexOptions.CultureInvariant)]
    private static partial Regex ScriptStartRegex();
    [GeneratedRegex(@"^→\s*脚本执行结束[:：]", RegexOptions.CultureInvariant)]
    private static partial Regex ScriptEndRegex();
    [GeneratedRegex(@"(?:自动秘境：传送到秘境|自动首领讨伐：前往)\s*(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DestinationRegex();
    [GeneratedRegex(@"本轮奖励识别结果\s+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex RewardRegex();
    [GeneratedRegex("([^,，]+?)\\s+[x×](\\d+)(?=\\s*[,，\"”]|\\s*$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RewardItemRegex();
    [GeneratedRegex(@"^木材(.+?)累积获取数量[:：]\s*(\d+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex WoodTotalRegex();
    [GeneratedRegex(@"^第\s*(\d+)\s*次伐木", RegexOptions.CultureInvariant)]
    private static partial Regex WoodRoundRegex();
    [GeneratedRegex(@"^交互或拾取[:：]\s*(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex PickupRegex();
    [GeneratedRegex(@"任务执行异常|未预期的异常|一条龙启动失败|执行脚本时发生异常|执行配置组任务时失败|此追踪脚本未正常走完|路径点执行超时|执行超时，放弃此次追踪|重试多次后仍然失败，放弃此路径点", RegexOptions.CultureInvariant)]
    private static partial Regex KnownFailureRegex();
    [GeneratedRegex(@"任务(?:被手动取消|手动取消|被取消)|一条龙在启动阶段被取消|取消执行配置组", RegexOptions.CultureInvariant)]
    private static partial Regex CancelRegex();
}

public sealed record LogParseUpdate(RunProgressDto? Progress, bool IsTerminal);
