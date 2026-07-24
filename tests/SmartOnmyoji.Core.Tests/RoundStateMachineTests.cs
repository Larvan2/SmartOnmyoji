using SmartOnmyoji.Core.Engine;
using SmartOnmyoji.Core.Targets;
using Xunit;

namespace SmartOnmyoji.Core.Tests;

public class RoundStateMachineTests
{
    private static TargetImage T(string name, TargetFlag flag = TargetFlag.Normal) =>
        new() { Name = name, FilePath = $"{name}.png", Flag = flag };

    [Fact]
    public void Normal_target_is_clicked()
    {
        var sm = new RoundStateMachine();
        Assert.Equal(DecisionKind.Click, sm.Decide(T("reward")).Kind);
    }

    [Fact]
    public void RoundStart_increments_round_and_reports_it()
    {
        var sm = new RoundStateMachine();
        var d = sm.Decide(T("start", TargetFlag.RoundStart));
        Assert.Equal(DecisionKind.Click, d.Kind);
        Assert.Equal(1, d.RoundStarted);
        Assert.Equal(1, sm.Round);
    }

    [Fact]
    public void Same_RoundStart_in_a_row_does_not_double_count()
    {
        var sm = new RoundStateMachine();
        sm.Decide(T("start", TargetFlag.RoundStart));            // round 1
        var d = sm.Decide(T("start", TargetFlag.RoundStart));    // 同一起点图仍在屏幕上
        Assert.Null(d.RoundStarted);
        Assert.Equal(1, sm.Round);
    }

    [Fact]
    public void Once_clicks_only_first_time_per_round()
    {
        var sm = new RoundStateMachine();
        sm.Decide(T("start", TargetFlag.RoundStart));
        Assert.Equal(DecisionKind.Click, sm.Decide(T("mark", TargetFlag.Once)).Kind);
        sm.Decide(T("reward"));                                  // 打断,避免连续同名触发停止
        Assert.Equal(DecisionKind.NoClick, sm.Decide(T("mark", TargetFlag.Once)).Kind);
    }

    [Fact]
    public void New_round_resets_Once()
    {
        var sm = new RoundStateMachine();
        sm.Decide(T("start", TargetFlag.RoundStart));
        sm.Decide(T("mark", TargetFlag.Once));                   // 已点
        sm.Decide(T("reward"));
        var again = sm.Decide(T("start", TargetFlag.RoundStart));// round 2
        Assert.Equal(2, again.RoundStarted);
        Assert.Equal(DecisionKind.Click, sm.Decide(T("mark", TargetFlag.Once)).Kind); // 新回合又可点
    }

    [Fact]
    public void Skip_does_not_click_and_suppresses_Once()
    {
        var sm = new RoundStateMachine();
        sm.Decide(T("start", TargetFlag.RoundStart));
        Assert.Equal(DecisionKind.NoClick, sm.Decide(T("marked", TargetFlag.Skip)).Kind);
        Assert.Equal(DecisionKind.NoClick, sm.Decide(T("mark", TargetFlag.Once)).Kind);
    }

    [Fact]
    public void Stop_flag_stops()
    {
        var sm = new RoundStateMachine();
        var d = sm.Decide(T("end", TargetFlag.Stop));
        Assert.Equal(DecisionKind.Stop, d.Kind);
        Assert.Equal(StopReason.StopFlag, d.Reason);
    }

    [Fact]
    public void Five_consecutive_same_target_stops()
    {
        var sm = new RoundStateMachine(repeatStopEnabled: true, repeatCapacity: 5);
        var d = RoundDecision.Click;
        for (var i = 0; i < 5; i++)
            d = sm.Decide(T("reward"));
        Assert.Equal(DecisionKind.Stop, d.Kind);
        Assert.Equal(StopReason.RepeatedSameTarget, d.Reason);
    }

    [Fact]
    public void NoMatch_resets_repeat_counter()
    {
        var sm = new RoundStateMachine(repeatStopEnabled: true, repeatCapacity: 5);
        for (var i = 0; i < 4; i++) sm.Decide(T("reward"));
        sm.NotifyNoMatch();                                      // 重置
        var d = sm.Decide(T("reward"));                          // 视为第 1 次,不应停止
        Assert.NotEqual(DecisionKind.Stop, d.Kind);
    }
}
