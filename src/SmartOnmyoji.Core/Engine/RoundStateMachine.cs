using SmartOnmyoji.Core.Targets;

namespace SmartOnmyoji.Core.Engine;

public enum StopReason { Completed, StopFlag, RepeatedSameTarget, Cancelled }

public enum DecisionKind { Click, NoClick, Stop }

/// <summary>状态机对一次命中的裁决。</summary>
public sealed record RoundDecision(DecisionKind Kind, StopReason? Reason = null, int? RoundStarted = null)
{
    public static readonly RoundDecision NoClick = new(DecisionKind.NoClick);
    public static readonly RoundDecision Click = new(DecisionKind.Click);
    public static RoundDecision Stop(StopReason reason) => new(DecisionKind.Stop, Reason: reason);
    public RoundDecision WithRoundStarted(int round) => this with { RoundStarted = round };
}

/// <summary>
/// 回合状态机:把旧 run() 里散落的 flag_mark / rounds / success_target_list 收拢到一处,
/// 让回合流转与点击裁决可单测。匹配成功调用 <see cref="Decide"/>;匹配失败调用 <see cref="NotifyNoMatch"/>。
/// </summary>
public sealed class RoundStateMachine
{
    private readonly bool _repeatStopEnabled;
    private readonly RecentBuffer<string> _recent;
    private bool _onceUsedThisRound;
    private string? _lastName;

    public int Round { get; private set; }

    public RoundStateMachine(bool repeatStopEnabled = true, int repeatCapacity = 5)
    {
        _repeatStopEnabled = repeatStopEnabled;
        _recent = new RecentBuffer<string>(repeatCapacity);
    }

    public RoundDecision Decide(TargetImage hit)
    {
        var name = hit.Name;
        var sameAsLast = _lastName == name;
        _recent.Push(name);
        _lastName = name;

        // 连续 N 次匹配到同一目标 → 终止(卡住 / 无体力一直点的保护)
        if (_repeatStopEnabled && _recent.AllSame())
            return RoundDecision.Stop(StopReason.RepeatedSameTarget);

        switch (hit.Flag)
        {
            case TargetFlag.Stop:
                return RoundDecision.Stop(StopReason.StopFlag);

            case TargetFlag.Skip:
                _onceUsedThisRound = true; // 抑制本回合的 Once
                return RoundDecision.NoClick;

            case TargetFlag.Once:
                if (_onceUsedThisRound) return RoundDecision.NoClick;
                _onceUsedThisRound = true;
                return RoundDecision.Click;

            case TargetFlag.RoundStart:
                // 同一张起点图仍停留在屏幕上时不重复计回合(对齐旧 success_target_list[0]!=[1] 判定)
                if (!sameAsLast)
                {
                    Round++;
                    _onceUsedThisRound = false;
                    return RoundDecision.Click.WithRoundStarted(Round);
                }
                return RoundDecision.Click;

            default:
                return RoundDecision.Click;
        }
    }

    /// <summary>匹配失败:重置"连续同名"判定(镜像旧代码在无匹配时重置数组的行为)。</summary>
    public void NotifyNoMatch() => _recent.Clear();
}
