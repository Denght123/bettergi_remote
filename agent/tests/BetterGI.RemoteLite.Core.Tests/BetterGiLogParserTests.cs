using BetterGI.RemoteLite.Reports;

namespace BetterGI.RemoteLite.Core.Tests;

public sealed class BetterGiLogParserTests
{
    [Theory]
    [InlineData("任务中断:任务被取消")]
    [InlineData("任务被取消，退出执行")]
    [InlineData("一条龙在启动阶段被取消")]
    [InlineData("取消执行配置组:任务被取消")]
    public void OfficialManualStopLogsStopTheRunAndSkipRemainingTasks(string marker)
    {
        var start = DateTimeOffset.UtcNow;
        var parser = new BetterGiLogParser("manual-stop", start, ["领取邮件", "自动秘境", "采集组"]);
        parser.AcceptLine("一条龙任务执行: 1/2", start);
        var update=parser.AcceptLine(marker,start.AddSeconds(2));
        Assert.True(update.IsTerminal);
        parser.AcceptLine("一条龙和配置组任务结束",start.AddSeconds(3));
        var report=parser.Finish("success",start.AddSeconds(4));
        Assert.Equal("stopped",report.Status);
        Assert.Equal("stopped",report.Tasks[0].State);
        Assert.All(report.Tasks.Skip(1),task=>Assert.Equal("skipped",task.State));
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void TracksDiverseMaterialNamesBeyondThePreviousSmallRewardLimit()
    {
        var start = DateTimeOffset.UtcNow;
        var parser = new BetterGiLogParser("diverse-materials", start, ["采集组"]);
        parser.AcceptLine("配置组任务执行: 1/1", start);
        for(var i=0;i<200;i++) parser.AcceptLine($"本轮奖励识别结果 采集材料{i} x2", start);
        var report=parser.Finish("success",start);
        Assert.Equal(200, report.Rewards.Count);
        Assert.Equal(2, report.Rewards["采集材料199"]);
        Assert.Equal(200, report.Tasks[0].Rewards!.Count);
    }

    [Fact]
    public void ConvertsScriptStackLocationToAReadableLineWithoutExposingTheTrace()
    {
        var start = DateTimeOffset.UtcNow;
        var parser = new BetterGiLogParser("js", start, ["采集组"]);
        parser.AcceptLine("配置组任务执行: 1/1", start);
        parser.AcceptLine("→ 开始执行JS脚本: \"采集调度\"", start);
        parser.AcceptLine("[ERR] 执行脚本时发生异常", start);
        parser.AcceptLine("    at ScriptEngine.execute (main.js:18:6)", start);
        var report = parser.Finish("failed", start);
        Assert.Equal("采集调度 · 第 18 行", report.Tasks[0].Location);
        Assert.Contains("第 18 行", report.Errors[0]);
        Assert.DoesNotContain("ScriptEngine", report.Errors[0]);
    }

    [Fact]
    public void ReportsNamedScriptLocationAndReadableFailureWithIndependentCounters()
    {
        var start = DateTimeOffset.UtcNow;
        var parser = new BetterGiLogParser("route", start, ["领取邮件", "每日采集组", "自动秘境"]);
        parser.AcceptLine("一条龙任务执行: 1/2", start);
        parser.AcceptLine("配置组任务执行: 1/1", start.AddSeconds(1));
        parser.AcceptLine("配置组 \"每日采集组\" 加载完成，准备执行", start.AddSeconds(2));
        var progress = parser.AcceptLine("→ 开始执行地图追踪任务: \"清心采集路线.json\"", start.AddSeconds(3)).Progress;
        Assert.Equal("每日采集组", progress!.CurrentTask);
        Assert.Equal("清心采集路线.json", progress.CurrentLocation);
        Assert.Equal("沿路线移动与采集", progress.CurrentStep);
        parser.AcceptLine("[12:00:00.123] [ERR] [PC:S1:P123:T4] BetterGenshinImpact.GameTask.AutoPathing.PathExecutor", start);
        parser.AcceptLine("路径点执行超时", start.AddSeconds(4));
        parser.AcceptLine("   at BetterGenshinImpact.GameTask.AutoPathing.PathExecutor.Run()", start);
        parser.AcceptLine("一条龙任务执行: 2/2", start.AddSeconds(5));
        parser.AcceptLine("一条龙和配置组任务结束", start.AddSeconds(6));
        var report = parser.Finish("success", start.AddSeconds(7));
        Assert.Equal("failed", report.Status);
        Assert.Equal("failed", report.Tasks[1].State);
        Assert.Equal("清心采集路线.json", report.Tasks[1].Location);
        Assert.Contains("每日采集组", report.Errors[0]);
        Assert.Contains("超时", report.Errors[0]);
        Assert.DoesNotContain(report.Errors, error => error.Contains("Exception") || error.Contains("PathExecutor"));
    }

    [Fact]
    public void KeepsMaterialsWoodEstimatesAndPickupAttemptsSeparate()
    {
        var start = DateTimeOffset.UtcNow;
        var parser = new BetterGiLogParser("materials", start, ["采集与伐木"]);
        parser.AcceptLine("配置组任务执行: 1/1", start);
        parser.AcceptLine("自动秘境：本轮奖励识别结果 \"清心 x3, 自由的教导 x4, 摩拉 x1000\"", start);
        parser.AcceptLine("交互或拾取：\"薄荷\"", start);
        parser.AcceptLine("交互或拾取：\"薄荷\"", start);
        parser.AcceptLine("自动伐木，启动", start);
        parser.AcceptLine("木材杉木累积获取数量：3", start);
        parser.AcceptLine("木材杉木累积获取数量：6", start);
        parser.AcceptLine("木材杉木累积获取数量：6", start);
        var report = parser.Finish("success", start.AddMinutes(2));
        Assert.Equal(3, report.Rewards["清心"]);
        Assert.Equal(4, report.Rewards["自由的教导"]);
        Assert.Equal(2, report.PickupObservations!["薄荷"]);
        Assert.Equal(6, report.WoodEstimates!["杉木"]);
        Assert.False(report.Rewards.ContainsKey("薄荷"));
        Assert.False(report.Rewards.ContainsKey("杉木"));
        Assert.Equal(report.Rewards, report.Tasks[0].Rewards);
        Assert.Contains("不代表实际入包", report.LootCoverage);
    }

    [Fact]
    public void WoodEstimateResetsOnlyAtASeparateWoodRun()
    {
        var start = DateTimeOffset.UtcNow;
        var parser = new BetterGiLogParser("wood", start, ["伐木组"]);
        parser.AcceptLine("配置组任务执行: 1/1", start);
        parser.AcceptLine("自动伐木，启动", start);
        parser.AcceptLine("木材杉木累积获取数量：9", start);
        parser.AcceptLine("自动伐木，启动", start);
        parser.AcceptLine("木材杉木累积获取数量：3", start);
        Assert.Equal(12, parser.Finish("success", start).WoodEstimates!["杉木"]);
    }

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
