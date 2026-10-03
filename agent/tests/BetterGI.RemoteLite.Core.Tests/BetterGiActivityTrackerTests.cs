using BetterGI.RemoteLite.BetterGi;

namespace BetterGI.RemoteLite.Core.Tests;

public sealed class BetterGiActivityTrackerTests
{
    private static readonly DateOnly LogDate = new(2026, 10, 3);
    private static BetterGiActivityTracker Tracker() => new(123, LogDate.ToDateTime(new TimeOnly(10, 0)));

    [Fact]
    public void OpenWindowIsNotAnActiveTaskButMissingEvidenceIsNotIdleProof()
    {
        var tracker = Tracker();
        Assert.False(tracker.HasEvidence);
        Feed(tracker, "View.MainWindow", "主窗体实例化");
        Assert.True(tracker.HasEvidence);
        Assert.False(tracker.IsBusy);
    }

    [Fact]
    public void OneDragonRemainsBusyBetweenItsIndependentTasks()
    {
        var tracker = Tracker();
        Feed(tracker, "ViewModel.Pages.OneDragonFlowViewModel", "启用一条龙配置：默认配置");
        Feed(tracker, "GameTask.TaskRunner", "→ \"任务启动！\"");
        Feed(tracker, "GameTask.TaskRunner", "→ \"任务结束\"");
        Assert.True(tracker.IsBusy);
        Feed(tracker, "ViewModel.Pages.OneDragonFlowViewModel", "一条龙和配置组任务结束");
        Assert.False(tracker.IsBusy);
    }

    [Fact]
    public void StandaloneTaskBecomesIdleOnlyWhenRunnerEnds()
    {
        var tracker = Tracker();
        Feed(tracker, "GameTask.TaskRunner", "→ \"任务启动！\"");
        Assert.True(tracker.IsBusy);
        Feed(tracker, "GameTask.TaskRunner", "任务中断:任务被取消");
        Assert.True(tracker.IsBusy);
        Feed(tracker, "GameTask.TaskRunner", "→ \"任务结束\"");
        Assert.False(tracker.IsBusy);
    }

    [Fact]
    public void GroupLoadingAndGameStartupAreBusyBeforeTheRunner()
    {
        var tracker = Tracker();
        Feed(tracker, "GameTask.Common.TaskControl", "当前不在游戏主界面，等待进入主界面后执行任务...");
        Assert.True(tracker.IsBusy);
        Feed(tracker, "Service.ScriptService", "配置组 \"采集\" 加载完成，共2个脚本，开始执行");
        Assert.True(tracker.IsBusy);
        Feed(tracker, "Service.ScriptService", "配置组 \"采集\" 执行结束");
        Assert.False(tracker.IsBusy);
    }

    [Fact]
    public void OtherProcessAndPreviousProcessLifetimeCannotResetBusyState()
    {
        var tracker = Tracker();
        Feed(tracker, "GameTask.TaskRunner", "→ \"任务启动！\"");
        Feed(tracker, "GameTask.TaskRunner", "→ \"任务结束\"", processId: 456);
        Assert.True(tracker.IsBusy);
        Feed(tracker, "GameTask.TaskRunner", "→ \"任务结束\"", time: "09:50:00.000");
        Assert.True(tracker.IsBusy);
    }

    [Fact]
    public void ContinuousSchedulerIsConservativelyBusyBetweenGroups()
    {
        var tracker = Tracker();
        Feed(tracker, "ViewModel.Pages.ScriptControlViewModel", "开始连续执行选中配置组:采集一,采集二");
        Feed(tracker, "Service.ScriptService", "配置组 \"采集一\" 执行结束");
        Feed(tracker, "GameTask.TaskRunner", "→ \"任务结束\"");
        Assert.True(tracker.IsBusy);
    }

    [Fact]
    public void ScriptTextCannotImpersonateRunnerLifecycle()
    {
        var tracker = Tracker();
        Feed(tracker, "GameTask.TaskRunner", "→ \"任务启动！\"");
        Feed(tracker, "Core.Script.Dependence.Log", "→ \"任务结束\"");
        Assert.True(tracker.IsBusy);
    }

    [Theory]
    [InlineData("启用一条龙配置：远程每日", true)]
    [InlineData("启用一条龙配置：\"远程每日\"", true)]
    [InlineData("启用一条龙配置：默认配置", false)]
    [InlineData("启用一条龙配置：远程每日备份", false)]
    [InlineData("参数指定的一条龙配置：远程每日", false)]
    [InlineData("任务启动失败：当前存在正在运行中的独立任务", false)]
    public void StartupAcknowledgementRequiresExactConfiguration(string line, bool expected)
        => Assert.Equal(expected, BetterGiStartAcknowledgement.Matches(line, "远程每日"));

    private static void Feed(BetterGiActivityTracker tracker, string source, string message, int processId = 123, string time = "10:01:00.000")
    {
        tracker.AcceptLine($"[{time}] [INF] [Primary:S1:P{processId}:T1] BetterGenshinImpact.{source}", LogDate);
        tracker.AcceptLine(message, LogDate);
    }
}
