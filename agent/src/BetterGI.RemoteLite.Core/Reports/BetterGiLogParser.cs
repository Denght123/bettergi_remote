using System.Text.RegularExpressions;
using BetterGI.RemoteLite.Protocol;

namespace BetterGI.RemoteLite.Reports;

public sealed partial class BetterGiLogParser
{
    private readonly string _runId;
    private readonly DateTimeOffset _startedAt;
    private readonly List<MutableTaskResult> _tasks;
    private readonly Dictionary<string, int> _rewards = new(StringComparer.CurrentCulture);
    private readonly List<string> _errors = [];
    private readonly Queue<string> _excerpt = new();
    private int _currentTaskIndex = -1;
    private string? _dailyRewardStatus;
    private string? _terminalStatus;

    public BetterGiLogParser(string runId, DateTimeOffset startedAt, IReadOnlyList<string> enabledTasks)
    {
        _runId = runId;
        _startedAt = startedAt;
        _tasks = enabledTasks.Select(name => new MutableTaskResult(name, "pending")).ToList();
    }

    public bool IsTerminal => _terminalStatus is not null;
    public string? TerminalStatus => _terminalStatus;

    public LogParseUpdate AcceptLine(string line, DateTimeOffset observedAt)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return new LogParseUpdate(null, false);
        }

        AddExcerpt(line);
        var changed = false;
        var ordinal = TaskOrdinalRegex().Match(line);
        if (ordinal.Success && int.TryParse(ordinal.Groups[1].Value, out var oneBased))
        {
            changed |= StartTask(oneBased - 1);
        }

        var group = GroupStartRegex().Match(line);
        if (group.Success)
        {
            changed |= StartNamedTask(group.Groups[1].Value.Trim());
        }

        if (line.Contains("检查每日奖励：已领取", StringComparison.Ordinal))
        {
            _dailyRewardStatus = "已领取";
            changed = true;
        }
        else if (line.Contains("检查到每日奖励未领取", StringComparison.Ordinal))
        {
            _dailyRewardStatus = "未领取";
            MarkTaskWarning("领取每日奖励", "BetterGI 检查到每日奖励未领取");
            changed = true;
        }

        var reward = RewardRegex().Match(line);
        if (reward.Success)
        {
            foreach (Match item in RewardItemRegex().Matches(reward.Groups[1].Value))
            {
                var name = item.Groups[1].Value.Trim(' ', ',', '，');
                if (name.Length == 0 || !int.TryParse(item.Groups[2].Value, out var count))
                {
                    continue;
                }
                _rewards[name] = _rewards.GetValueOrDefault(name) + count;
            }
            changed = true;
        }

        if (IsErrorLine(line))
        {
            var message = CleanLogLine(line);
            if (!_errors.Contains(message, StringComparer.Ordinal))
            {
                _errors.Add(message);
            }
            if (_currentTaskIndex >= 0 && _currentTaskIndex < _tasks.Count)
            {
                _tasks[_currentTaskIndex].State = "failed";
                _tasks[_currentTaskIndex].Message = message;
            }
            changed = true;
        }

        if (SuccessRegex().IsMatch(line))
        {
            CompleteStartedTasks();
            _terminalStatus = _errors.Count == 0 ? "success" : "failed";
            changed = true;
        }
        else if (CancelRegex().IsMatch(line))
        {
            if (_currentTaskIndex >= 0 && _currentTaskIndex < _tasks.Count && _tasks[_currentTaskIndex].State == "running")
            {
                _tasks[_currentTaskIndex].State = "stopped";
            }
            _terminalStatus = "stopped";
            changed = true;
        }

        var progress = changed ? BuildProgress(observedAt, CleanLogLine(line)) : null;
        return new LogParseUpdate(progress, IsTerminal);
    }

    public RunReportDto Finish(string status, DateTimeOffset finishedAt, string? error = null)
    {
        if (error is not null && !_errors.Contains(error, StringComparer.Ordinal))
        {
            _errors.Add(error);
        }
        _terminalStatus ??= status;
        if (_terminalStatus == "success")
        {
            CompleteStartedTasks();
        }
        else if (_terminalStatus == "failed" && _currentTaskIndex >= 0 && _currentTaskIndex < _tasks.Count && _tasks[_currentTaskIndex].State == "running")
        {
            _tasks[_currentTaskIndex].State = "failed";
        }

        return new RunReportDto(
            _runId,
            _terminalStatus,
            _startedAt,
            finishedAt,
            Math.Max(0, (finishedAt - _startedAt).TotalSeconds),
            _tasks.Select(task => new RunTaskResult(task.Name, task.State, task.Message)).ToArray(),
            new Dictionary<string, int>(_rewards, StringComparer.CurrentCulture),
            _dailyRewardStatus,
            _errors.ToArray(),
            _excerpt.ToArray());
    }

    private bool StartTask(int index)
    {
        if (index < 0 || index >= _tasks.Count)
        {
            return false;
        }
        if (_currentTaskIndex >= 0 && _currentTaskIndex < _tasks.Count && _tasks[_currentTaskIndex].State == "running")
        {
            _tasks[_currentTaskIndex].State = "success";
        }
        _currentTaskIndex = index;
        if (_tasks[index].State == "pending")
        {
            _tasks[index].State = "running";
        }
        return true;
    }

    private bool StartNamedTask(string name)
    {
        var index = _tasks.FindIndex(task => string.Equals(task.Name, name, StringComparison.CurrentCulture));
        return index >= 0 && StartTask(index);
    }

    private void CompleteStartedTasks()
    {
        foreach (var task in _tasks)
        {
            if (task.State == "running")
            {
                task.State = "success";
            }
        }
    }

    private void MarkTaskWarning(string name, string message)
    {
        var task = _tasks.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        if (task is not null && task.State != "failed")
        {
            task.State = "warning";
            task.Message = message;
        }
    }

    private RunProgressDto BuildProgress(DateTimeOffset observedAt, string message)
    {
        var completed = _tasks.Count(task => task.State is "success" or "warning");
        var current = _currentTaskIndex >= 0 && _currentTaskIndex < _tasks.Count ? _tasks[_currentTaskIndex].Name : null;
        return new RunProgressDto(_runId, _terminalStatus ?? "running", current, completed, _tasks.Count, message, observedAt);
    }

    private void AddExcerpt(string line)
    {
        _excerpt.Enqueue(CleanLogLine(line));
        while (_excerpt.Count > 80)
        {
            _excerpt.Dequeue();
        }
    }

    private static bool IsErrorLine(string line)
        => line.Contains("[ERR]", StringComparison.OrdinalIgnoreCase) ||
           line.Contains("[FTL]", StringComparison.OrdinalIgnoreCase) ||
           line.Contains("任务执行异常", StringComparison.Ordinal) ||
           line.Contains("未预期的异常", StringComparison.Ordinal) ||
           line.Contains("一条龙启动失败", StringComparison.Ordinal);

    private static string CleanLogLine(string line)
    {
        var value = line.Trim();
        return value.Length <= 500 ? value : value[..500];
    }

    private sealed class MutableTaskResult(string name, string state)
    {
        public string Name { get; } = name;
        public string State { get; set; } = state;
        public string? Message { get; set; }
    }

    [GeneratedRegex(@"(?:一条龙任务执行|配置组任务执行):\s*(\d+)\s*/\s*(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex TaskOrdinalRegex();

    [GeneratedRegex(@"配置组(.+?)启动", RegexOptions.CultureInvariant)]
    private static partial Regex GroupStartRegex();

    [GeneratedRegex(@"本轮奖励识别结果\s+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex RewardRegex();

    [GeneratedRegex(@"([^,，]+?)\s+x(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RewardItemRegex();

    [GeneratedRegex(@"一条龙和配置组任务结束", RegexOptions.CultureInvariant)]
    private static partial Regex SuccessRegex();

    [GeneratedRegex(@"任务(?:被手动取消|手动取消|被取消)|一条龙在启动阶段被取消", RegexOptions.CultureInvariant)]
    private static partial Regex CancelRegex();
}

public sealed record LogParseUpdate(RunProgressDto? Progress, bool IsTerminal);

