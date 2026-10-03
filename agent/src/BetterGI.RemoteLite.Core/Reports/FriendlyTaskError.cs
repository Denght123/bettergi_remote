using System.Text.RegularExpressions;

namespace BetterGI.RemoteLite.Reports;

/// <summary>Messages for phone/notification surfaces; diagnostics stay in the log excerpt.</summary>
public static partial class FriendlyTaskError
{
    public static string Describe(string message)
    {
        if (message.Contains("任务监控超过", StringComparison.Ordinal)) return "任务运行时间过长，暂时无法确认结果，请查看电脑端。";
        if (message.Contains("进程", StringComparison.Ordinal) && message.Contains("退出", StringComparison.Ordinal)) return "BetterGI 在任务完成前退出，请重新打开后重试。";
        if (message.Contains("未进入和合成台交互", StringComparison.Ordinal)) return "未能进入合成台对话，请检查角色位置和游戏画面。";
        if (message.Contains("队伍中没有", StringComparison.Ordinal)) return "当前队伍缺少此任务需要的角色或元素，请检查队伍设置。";
        if (message.Contains("切换队伍失败", StringComparison.Ordinal)) return "切换队伍失败，请检查队伍名称和地图追踪设置。";
        if (message.Contains("未找到对应的秘境", StringComparison.Ordinal)) return "没有找到秘境传送点，请检查秘境选择和地图解锁情况。";
        if (message.Contains("传送失败", StringComparison.Ordinal)) return "传送未成功，请检查传送点是否解锁及当前游戏画面。";
        if (message.Contains("未正常走完", StringComparison.Ordinal) || message.Contains("放弃此路径", StringComparison.Ordinal)) return "当前采集路线未走完，请检查角色是否卡住或路线是否可达。";
        if (message.Contains("超时", StringComparison.Ordinal) || message.Contains("Timeout", StringComparison.OrdinalIgnoreCase)) return "当前步骤等待超时，请检查游戏画面或网络连接后重试。";
        if (message.Contains("识别", StringComparison.Ordinal) && message.Contains("失败", StringComparison.Ordinal)) return "当前步骤画面识别失败，请检查游戏分辨率和是否有其他窗口遮挡。";
        if (message.Contains("FileNotFound", StringComparison.OrdinalIgnoreCase) || message.Contains("DirectoryNotFound", StringComparison.OrdinalIgnoreCase)) return "任务需要的本地文件不存在，请检查脚本、路线或策略文件。";
        if (message.Contains("UnauthorizedAccess", StringComparison.OrdinalIgnoreCase) || message.Contains("拒绝访问", StringComparison.Ordinal)) return "无法访问任务需要的文件，请检查文件权限及是否被其他程序占用。";
        if (message.Contains("Json", StringComparison.OrdinalIgnoreCase) && message.Contains("Exception", StringComparison.Ordinal)) return "任务配置格式有误，请在配置页检查并重新保存。";
        var firstLine = message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        firstLine = LogPrefixRegex().Replace(firstLine.Trim(), string.Empty);
        firstLine = SensitiveFragmentRegex().Replace(firstLine, "（详情见电脑端日志）");
        firstLine = TechnicalFragmentRegex().Replace(firstLine, string.Empty).Trim(' ', ':', '：', '"');
        if (ChineseRegex().IsMatch(firstLine) && !LatinWordRegex().IsMatch(firstLine) &&
            firstLine.Length is > 6 and <= 180 && !firstLine.EndsWith("异常", StringComparison.Ordinal)) return firstLine;
        return message.Contains("脚本", StringComparison.Ordinal)
            ? "脚本执行失败，请检查该脚本的配置和电脑端运行记录。"
            : "当前步骤执行失败，请检查游戏画面及任务配置，详细原因可在电脑端查看。";
    }

    [GeneratedRegex(@"^(?:\[[^\]]+\]\s*)+", RegexOptions.CultureInvariant)]
    private static partial Regex LogPrefixRegex();
    [GeneratedRegex("(?:https?://\\S+|[A-Za-z]:[\\\\/][^\\s\"']+|(?:token|secret|password|key)\\s*[=:]\\s*\\S+|0x[0-9a-fA-F]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveFragmentRegex();
    [GeneratedRegex(@"(?:[A-Za-z_][\w.]*Exception.*|\bat\s+[A-Za-z_].*)", RegexOptions.CultureInvariant)]
    private static partial Regex TechnicalFragmentRegex();
    [GeneratedRegex(@"[\u3400-\u9fff]", RegexOptions.CultureInvariant)]
    private static partial Regex ChineseRegex();
    [GeneratedRegex(@"[A-Za-z_]{4,}", RegexOptions.CultureInvariant)]
    private static partial Regex LatinWordRegex();
}
