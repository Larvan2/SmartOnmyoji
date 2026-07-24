using SmartOnmyoji.Core.Engine;

namespace SmartOnmyoji.Core.Humanize;

/// <summary>轮末防检测的一次等待决策:等多久、为什么。引擎据此发 <c>Waiting</c> 事件并延时。</summary>
public sealed record WaitDecision(TimeSpan Duration, string Reason);

/// <summary>
/// 防检测等待策略。抽象成接口是为了让引擎的"轮末接线"能用桩实现零延迟单测,
/// 真实决策逻辑由 <see cref="AntiDetectionPolicy"/> 独立单测。
/// </summary>
public interface IAntiDetectionPolicy
{
    /// <summary>
    /// 引擎在<b>一轮多窗口全部处理完毕后</b>调用一次。
    /// <paramref name="roundStarted"/> = 这一轮是否有任一窗口开局(RoundStart,多窗口已在引擎侧合并为组的一次)。
    /// 返回本轮轮末应施加的等待(0/1/2 个);等待作用于整个循环,故双开"要歇一起歇"。
    /// </summary>
    IReadOnlyList<WaitDecision> EvaluateAfterRound(bool roundStarted);
}

/// <summary>
/// 两层叠加的防检测等待策略(P5),都以"开局(RoundStart)"为计数节拍:
/// <list type="number">
/// <item>概率随机等待:打破节奏规律。每次开局按概率命中则等一段随机秒数,带两次等待最小间隔保护。</item>
/// <item>时间窗口频次上限:总量安全阀。滑动窗口内开局数超阈值后,之后每次开局强制额外等待。</item>
/// </list>
/// 只在"开局"这个节拍点介入——战斗中/领奖中的轮不产生等待,从而不打断组队配合。
/// 纯逻辑:自己不 sleep,只返回决策;时钟经 <see cref="TimeProvider"/> 注入,可离线单测。
/// 对齐旧 <c>ModuleRunThread.run</c> 的 <c>success_match_then_wait</c> 两段逻辑,但去掉魔法上限、集中一处、可调。
/// </summary>
public sealed class AntiDetectionPolicy : IAntiDetectionPolicy
{
    private readonly AntiDetectionOptions _options;
    private readonly TimeProvider _clock;
    private readonly Random _random;

    // 层1 状态:上次随机等待时刻(MinGap 保护;null = 从未等过)
    private DateTimeOffset? _lastRandomWaitAt;

    // 层2 状态:频次滑动窗口
    private DateTimeOffset _windowStart;
    private int _windowRounds;
    private bool _windowInitialized;

    public AntiDetectionPolicy(AntiDetectionOptions options, TimeProvider? clock = null, Random? random = null)
    {
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _random = random ?? Random.Shared;
    }

    public IReadOnlyList<WaitDecision> EvaluateAfterRound(bool roundStarted)
    {
        // 防检测只在"开局"节拍介入;总开关关闭或本轮未开局则不产生任何等待。
        if (!_options.Enabled || !roundStarted)
            return Array.Empty<WaitDecision>();

        var now = _clock.GetUtcNow();
        var decisions = new List<WaitDecision>(2);

        AppendRandomWait(now, decisions);
        AppendFrequencyCap(now, decisions);

        return decisions.Count == 0 ? Array.Empty<WaitDecision>() : decisions;
    }

    // 层1:概率随机等待(带最小间隔保护)
    private void AppendRandomWait(DateTimeOffset now, List<WaitDecision> decisions)
    {
        var rw = _options.RandomWait;
        if (!rw.Enabled || rw.Probability <= 0)
            return;
        if (_random.NextDouble() >= rw.Probability)
            return;

        // 距上次随机等待不足 MinGapSeconds 则跳过,避免短时间内连等两次
        if (_lastRandomWaitAt is { } last && (now - last).TotalSeconds < rw.MinGapSeconds)
            return;

        var lo = Math.Max(0, rw.MinSeconds);
        var hi = Math.Max(lo, rw.MaxSeconds);
        var seconds = lo + _random.Next(hi - lo + 1);
        if (seconds <= 0)
            return;

        decisions.Add(new WaitDecision(TimeSpan.FromSeconds(seconds), "随机等待(防节奏规律)"));
        _lastRandomWaitAt = now;
    }

    // 层2:时间窗口频次上限(总量安全阀)
    private void AppendFrequencyCap(DateTimeOffset now, List<WaitDecision> decisions)
    {
        var cap = _options.FrequencyCap;
        if (!cap.Enabled)
            return;

        // 窗口首次使用,或已过期 → 重开窗口
        if (!_windowInitialized || (now - _windowStart).TotalMinutes >= cap.WindowMinutes)
        {
            _windowStart = now;
            _windowRounds = 0;
            _windowInitialized = true;
        }

        _windowRounds++;

        if (_windowRounds > cap.MaxRounds && cap.ExtraWaitSeconds > 0)
        {
            decisions.Add(new WaitDecision(
                TimeSpan.FromSeconds(cap.ExtraWaitSeconds),
                $"频次上限:{cap.WindowMinutes} 分钟内已 {_windowRounds} 局,强制减速"));
        }
    }
}
