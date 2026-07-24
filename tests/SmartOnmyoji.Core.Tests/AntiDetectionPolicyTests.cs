using SmartOnmyoji.Core.Engine;
using SmartOnmyoji.Core.Humanize;
using Xunit;

namespace SmartOnmyoji.Core.Tests;

/// <summary>
/// <see cref="AntiDetectionPolicy"/> 纯逻辑单测:注入可控时钟 + 确定性随机,
/// 逐分支覆盖 总开关 / 只在开局介入 / 概率随机等待 / 最小间隔保护 / 频次上限 / 窗口滑动重置 / 两层叠加。
/// </summary>
public class AntiDetectionPolicyTests
{
    // 可控时钟:GetUtcNow 返回可设定的时刻,便于测试频次窗口与最小间隔。
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public void Advance(TimeSpan by) => UtcNow += by;
    }

    // 确定性随机:NextDouble 回放给定序列(耗尽后返回 1.0 = 不触发概率),Next 恒返回 0(时长取下界)。
    private sealed class StubRandom : Random
    {
        private readonly Queue<double> _doubles;
        public StubRandom(params double[] doubles) => _doubles = new Queue<double>(doubles);
        public override double NextDouble() => _doubles.Count > 0 ? _doubles.Dequeue() : 1.0;
        public override int Next(int maxValue) => 0;
    }

    private static AntiDetectionPolicy Policy(AntiDetectionOptions options, TestClock clock, StubRandom random) =>
        new(options, clock, random);

    [Fact]
    public void Disabled_returns_no_wait_even_on_round_start()
    {
        var options = new AntiDetectionOptions { Enabled = false };
        var policy = Policy(options, new TestClock(), new StubRandom(0.0));

        Assert.Empty(policy.EvaluateAfterRound(roundStarted: true));
    }

    [Fact]
    public void No_round_start_returns_no_wait()
    {
        var options = new AntiDetectionOptions
        {
            RandomWait = new RandomWaitOptions { Enabled = true, Probability = 1.0 },
            FrequencyCap = new FrequencyCapOptions { Enabled = true, MaxRounds = 0 },
        };
        var policy = Policy(options, new TestClock(), new StubRandom(0.0));

        // 非开局轮:防检测不介入,即使概率必中、频次已"超限"也不产生等待。
        Assert.Empty(policy.EvaluateAfterRound(roundStarted: false));
    }

    [Fact]
    public void RandomWait_triggers_when_roll_below_probability()
    {
        var options = new AntiDetectionOptions
        {
            RandomWait = new RandomWaitOptions
            {
                Enabled = true, Probability = 1.0, MinSeconds = 10, MaxSeconds = 40, MinGapSeconds = 0,
            },
            FrequencyCap = new FrequencyCapOptions { Enabled = false },
        };
        var policy = Policy(options, new TestClock(), new StubRandom(0.0));

        var waits = policy.EvaluateAfterRound(roundStarted: true);

        var wait = Assert.Single(waits);
        Assert.Equal(TimeSpan.FromSeconds(10), wait.Duration); // Next→0 ⇒ 取下界 MinSeconds
        Assert.Contains("随机", wait.Reason);
    }

    [Fact]
    public void RandomWait_skips_when_roll_at_or_above_probability()
    {
        var options = new AntiDetectionOptions
        {
            RandomWait = new RandomWaitOptions { Enabled = true, Probability = 0.5 },
            FrequencyCap = new FrequencyCapOptions { Enabled = false },
        };
        var policy = Policy(options, new TestClock(), new StubRandom(0.9)); // 0.9 ≥ 0.5 → 不触发

        Assert.Empty(policy.EvaluateAfterRound(roundStarted: true));
    }

    [Fact]
    public void RandomWait_respects_min_gap_between_waits()
    {
        var clock = new TestClock();
        var options = new AntiDetectionOptions
        {
            RandomWait = new RandomWaitOptions
            {
                Enabled = true, Probability = 1.0, MinSeconds = 10, MaxSeconds = 10, MinGapSeconds = 150,
            },
            FrequencyCap = new FrequencyCapOptions { Enabled = false },
        };
        var policy = Policy(options, clock, new StubRandom(0.0, 0.0, 0.0)); // 三次都掷中概率

        // 第 1 次:触发等待
        Assert.Single(policy.EvaluateAfterRound(true));

        // 100s 后(< 150s 间隔):概率虽中,但被最小间隔挡下
        clock.Advance(TimeSpan.FromSeconds(100));
        Assert.Empty(policy.EvaluateAfterRound(true));

        // 再 60s(距上次等待 160s ≥ 150s):恢复触发
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Single(policy.EvaluateAfterRound(true));
    }

    [Fact]
    public void FrequencyCap_triggers_only_after_exceeding_max_in_window()
    {
        var options = new AntiDetectionOptions
        {
            RandomWait = new RandomWaitOptions { Enabled = false },
            FrequencyCap = new FrequencyCapOptions
            {
                Enabled = true, WindowMinutes = 10, MaxRounds = 3, ExtraWaitSeconds = 5,
            },
        };
        var policy = Policy(options, new TestClock(), new StubRandom());

        // 窗口内前 3 局:未超限
        for (var i = 0; i < 3; i++)
            Assert.Empty(policy.EvaluateAfterRound(true));

        // 第 4 局:超限 → 强制额外等待 5s
        var wait = Assert.Single(policy.EvaluateAfterRound(true));
        Assert.Equal(TimeSpan.FromSeconds(5), wait.Duration);
        Assert.Contains("频次", wait.Reason);

        // 第 5 局:仍超限 → 继续强制等待
        Assert.Single(policy.EvaluateAfterRound(true));
    }

    [Fact]
    public void FrequencyCap_resets_after_window_expires()
    {
        var clock = new TestClock();
        var options = new AntiDetectionOptions
        {
            RandomWait = new RandomWaitOptions { Enabled = false },
            FrequencyCap = new FrequencyCapOptions
            {
                Enabled = true, WindowMinutes = 10, MaxRounds = 3, ExtraWaitSeconds = 5,
            },
        };
        var policy = Policy(options, clock, new StubRandom());

        for (var i = 0; i < 4; i++) policy.EvaluateAfterRound(true); // 第 4 局已超限
        Assert.Single(policy.EvaluateAfterRound(true));               // 确认仍在超限态

        // 跨过 10 分钟窗口 → 计数重置,重新从 0 累积,不再超限
        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Empty(policy.EvaluateAfterRound(true));
    }

    [Fact]
    public void Both_layers_stack_when_both_fire()
    {
        var options = new AntiDetectionOptions
        {
            RandomWait = new RandomWaitOptions
            {
                Enabled = true, Probability = 1.0, MinSeconds = 10, MaxSeconds = 10, MinGapSeconds = 0,
            },
            FrequencyCap = new FrequencyCapOptions
            {
                Enabled = true, WindowMinutes = 10, MaxRounds = 0, ExtraWaitSeconds = 5,
            },
        };
        var policy = Policy(options, new TestClock(), new StubRandom(0.0));

        var waits = policy.EvaluateAfterRound(true);

        Assert.Equal(2, waits.Count);
        Assert.Contains(waits, w => w.Reason.Contains("随机"));
        Assert.Contains(waits, w => w.Reason.Contains("频次"));
    }
}
