using System.Globalization;
using System.Text.RegularExpressions;

namespace BetterGI.RemoteLite.BetterGi;

/// <summary>Conservative activity evidence from BetterGI's own, process-scoped log messages.</summary>
public sealed partial class BetterGiActivityTracker(int processId, DateTime processStartedAtLocal)
{
    private bool _belongsToProcess;
    private string _source = string.Empty;
    private bool _flowActive;
    private bool _runnerActive;
    private bool _groupActive;
    private bool _waitingForGame;

    public bool HasEvidence { get; private set; }
    public bool IsBusy => _flowActive || _runnerActive || _groupActive || _waitingForGame;

    public void AcceptLine(string line, DateOnly logDate)
    {
        var header = HeaderRegex().Match(line);
        if (header.Success)
        {
            _belongsToProcess = TimeOnly.TryParse(header.Groups["time"].Value, CultureInfo.InvariantCulture, out var time)
                && logDate.ToDateTime(time) >= processStartedAtLocal.AddSeconds(-2);
            var instance = header.Groups["instance"].Value;
            if (instance.Length > 0)
            {
                var pid = ProcessIdRegex().Match(instance);
                _belongsToProcess &= pid.Success && int.TryParse(pid.Groups[1].Value, out var observedPid) && observedPid == processId;
            }
            _source = header.Groups["source"].Value.Trim();
            return;
        }

        if (!_belongsToProcess || string.IsNullOrWhiteSpace(line)) return;
        var message = line.Trim();
        if (_source.EndsWith(".View.MainWindow", StringComparison.Ordinal) && message == "主窗体实例化")
        {
            HasEvidence = true;
        }
        if (_source.EndsWith(".OneDragonFlowViewModel", StringComparison.Ordinal))
        {
            if (message.StartsWith("启用一条龙配置：", StringComparison.Ordinal))
            {
                _flowActive = true;
                HasEvidence = true;
            }
            if (message.Contains("一条龙和配置组任务结束", StringComparison.Ordinal) ||
                message.Contains("启动阶段被取消", StringComparison.Ordinal) ||
                message.Contains("任务被取消，退出执行", StringComparison.Ordinal) ||
                message.Contains("没有配置,退出执行", StringComparison.Ordinal))
            {
                _flowActive = _runnerActive = _groupActive = _waitingForGame = false;
            }
        }
        if (_source.EndsWith(".TaskRunner", StringComparison.Ordinal))
        {
            if (message.StartsWith("→", StringComparison.Ordinal) && message.Contains("任务启动", StringComparison.Ordinal))
            {
                HasEvidence = true;
                _runnerActive = true;
                _waitingForGame = false;
            }
            if (message.StartsWith("→", StringComparison.Ordinal) && message.Contains("任务结束", StringComparison.Ordinal))
            {
                HasEvidence = true;
                _runnerActive = _waitingForGame = false;
            }
        }
        if (_source.EndsWith(".ScriptService", StringComparison.Ordinal))
        {
            if (message.StartsWith("配置组 ", StringComparison.Ordinal) && message.Contains("开始执行", StringComparison.Ordinal))
            {
                HasEvidence = true;
                _groupActive = true;
                _waitingForGame = false;
            }
            if (message.StartsWith("配置组 ", StringComparison.Ordinal) &&
                (message.Contains("执行结束", StringComparison.Ordinal) || message.Contains("启动阶段被取消", StringComparison.Ordinal)))
            {
                _groupActive = _waitingForGame = false;
            }
        }
        if (_source.EndsWith(".TaskControl", StringComparison.Ordinal))
        {
            if (message.Contains("等待进入主界面后执行任务", StringComparison.Ordinal)) _waitingForGame = true;
            if (message.Contains("检测到停止指令，退出启动等待", StringComparison.Ordinal)) _waitingForGame = false;
        }
        // A continuous scheduler has no reliable final marker in 0.64–0.66. Keep it
        // busy rather than interrupting the gap between two groups or loop iterations.
        if (_source.EndsWith(".ScriptControlViewModel", StringComparison.Ordinal) &&
            message.StartsWith("开始连续执行选中配置组", StringComparison.Ordinal))
        {
            HasEvidence = true;
            _flowActive = true;
        }
    }

    [GeneratedRegex(@"^\[(?<time>\d{2}:\d{2}:\d{2}(?:\.\d+)?)\]\s+\[[A-Z]+\]\s+(?:\[(?<instance>[^\]]+)\]\s+)?(?<source>BetterGenshinImpact\.[\w.]+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"(?:^|:)P(\d+)(?=:|$)", RegexOptions.CultureInvariant)]
    private static partial Regex ProcessIdRegex();
}

/// <summary>Never consider a command accepted until the requested configuration is named by BetterGI.</summary>
public static class BetterGiStartAcknowledgement
{
    public static bool Matches(string line, string configName)
    {
        const string marker = "启用一条龙配置：";
        var trimmed = line.Trim();
        if (!trimmed.StartsWith(marker, StringComparison.Ordinal)) return false;
        return string.Equals(trimmed[marker.Length..].Trim().Trim('"', '“', '”'), configName, StringComparison.Ordinal);
    }
}
