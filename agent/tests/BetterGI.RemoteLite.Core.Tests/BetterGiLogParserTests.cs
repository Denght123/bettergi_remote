using BetterGI.RemoteLite.Reports;

namespace BetterGI.RemoteLite.Core.Tests;

public sealed class BetterGiLogParserTests
{
    [Fact]
    public void ParsesSuccessfulRunAndRewards()
    {
        var start = new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero);
        var parser = new BetterGiLogParser("run-1", start, ["领取邮件", "自动秘境", "领取每日奖励"]);
        parser.AcceptLine("[08:00:02 INF] 一条龙任务执行: 1/3", start.AddSeconds(2));
        parser.AcceptLine("[08:00:10 INF] 一条龙任务执行: 2/3", start.AddSeconds(10));
        parser.AcceptLine("[08:03:00 INF] 自动秘境：本轮奖励识别结果 摩拉 x60000, 自由的教导 x3", start.AddMinutes(3));
        parser.AcceptLine("[08:05:00 INF] 一条龙任务执行: 3/3", start.AddMinutes(5));
        parser.AcceptLine("[08:05:30 INF] 检查每日奖励：已领取", start.AddMinutes(5.5));
        var terminal = parser.AcceptLine("[08:06:00 INF] 一条龙和配置组任务结束", start.AddMinutes(6));

        Assert.True(terminal.IsTerminal);
        var report = parser.Finish(parser.TerminalStatus!, start.AddMinutes(6));
        Assert.Equal("success", report.Status);
        Assert.All(report.Tasks, task => Assert.Equal("success", task.State));
        Assert.Equal(60000, report.Rewards["摩拉"]);
        Assert.Equal(3, report.Rewards["自由的教导"]);
        Assert.Equal("已领取", report.DailyRewardStatus);
    }

    [Fact]
    public void DoesNotClaimSuccessAfterError()
    {
        var start = DateTimeOffset.UtcNow;
        var parser = new BetterGiLogParser("run-2", start, ["自动秘境"]);
        parser.AcceptLine("一条龙任务执行: 1/1", start);
        parser.AcceptLine("[ERR] 任务执行异常", start.AddSeconds(1));
        parser.AcceptLine("一条龙和配置组任务结束", start.AddSeconds(2));

        var report = parser.Finish(parser.TerminalStatus!, start.AddSeconds(2));
        Assert.Equal("failed", report.Status);
        Assert.Equal("failed", report.Tasks[0].State);
        Assert.NotEmpty(report.Errors);
    }

    [Fact]
    public void ParsesCancellation()
    {
        var start = DateTimeOffset.UtcNow;
        var parser = new BetterGiLogParser("run-3", start, ["自动地脉花"]);
        parser.AcceptLine("一条龙任务执行: 1/1", start);
        parser.AcceptLine("任务被手动取消", start.AddSeconds(2));
        var report = parser.Finish(parser.TerminalStatus!, start.AddSeconds(2));
        Assert.Equal("stopped", report.Status);
        Assert.Equal("stopped", report.Tasks[0].State);
    }

    [Fact]
    public void ParsesRealBetterGi064MailRun()
    {
        var start = new DateTimeOffset(2026, 9, 7, 17, 2, 12, TimeSpan.FromHours(8));
        var parser = new BetterGiLogParser("real-mail", start, ["领取邮件"]);

        parser.AcceptLine("[17:03:21.400] [INF] BetterGenshinImpact.ViewModel.Pages.OneDragonFlowViewModel", start.AddMinutes(1));
        parser.AcceptLine("一条龙任务执行: 1/1", start.AddMinutes(1));
        parser.AcceptLine("邮件：\"全部领取\"", start.AddMinutes(1).AddSeconds(3));
        var terminal = parser.AcceptLine("一条龙和配置组任务结束", start.AddMinutes(1).AddSeconds(22));

        Assert.True(terminal.IsTerminal);
        var report = parser.Finish(parser.TerminalStatus!, start.AddMinutes(1).AddSeconds(22));
        Assert.Equal("success", report.Status);
        Assert.Single(report.Tasks);
        Assert.Equal("success", report.Tasks[0].State);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void ParsesQuotedRealRewardLogAndCurrentDailyRewardText()
    {
        var start = new DateTimeOffset(2026, 9, 10, 14, 0, 0, TimeSpan.FromHours(8));
        var parser = new BetterGiLogParser("real-reward", start, ["自动秘境", "领取每日奖励"]);

        parser.AcceptLine("自动秘境：开始奖励识别", start.AddMinutes(1));
        parser.AcceptLine("自动秘境：本轮奖励识别结果 \"好感经验 x60, 摩拉 x10575, 天授之馨 x1\"", start.AddMinutes(2));
        parser.AcceptLine("检查每日奖励结果：\"今日奖励已领取\"", start.AddMinutes(3));
        parser.AcceptLine("一条龙和配置组任务结束", start.AddMinutes(4));

        var report = parser.Finish(parser.TerminalStatus!, start.AddMinutes(4));
        Assert.Equal(60, report.Rewards["好感经验"]);
        Assert.Equal(10575, report.Rewards["摩拉"]);
        Assert.Equal(1, report.Rewards["天授之馨"]);
        Assert.Equal("已领取", report.DailyRewardStatus);
        Assert.Equal("BetterGI 奖励识别完成", report.RewardRecognitionStatus);
    }

    [Fact]
    public void ReportsPartialRewardRecognition()
    {
        var start = DateTimeOffset.UtcNow;
        var parser = new BetterGiLogParser("partial-reward", start, ["自动秘境"]);

        parser.AcceptLine("自动秘境：奖励识别失败，已跳过本轮奖励汇总", start.AddMinutes(1));
        parser.AcceptLine("自动秘境：本轮奖励识别结果 \"摩拉 x3525\"", start.AddMinutes(2));

        var report = parser.Finish("success", start.AddMinutes(3));
        Assert.Equal(3525, report.Rewards["摩拉"]);
        Assert.Contains("可能不完整", report.RewardRecognitionStatus);
    }
}
