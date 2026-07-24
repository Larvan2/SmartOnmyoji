using SmartOnmyoji.Core;
using SmartOnmyoji.Core.Engine;
using SmartOnmyoji.Core.Events;
using SmartOnmyoji.Core.Humanize;
using SmartOnmyoji.Core.Targets;
using Xunit;

namespace SmartOnmyoji.Core.Tests;

/// <summary>
/// 引擎 × 防检测的接线单测:用桩 <see cref="IAntiDetectionPolicy"/> 验证
/// 「轮末统一施加等待」「开局信号 roundStarted 正确上报」「双开一局合并为组的一轮」,
/// 与 <see cref="AntiDetectionPolicyTests"/>(纯策略逻辑)分工。桩等待时长为 0,测试零延迟。
/// </summary>
public class AutomationEngineAntiDetectionTests
{
    /// <summary>记录每轮 EvaluateAfterRound 的 roundStarted 入参;开局轮返回预设等待。</summary>
    private sealed class StubAntiDetection : IAntiDetectionPolicy
    {
        public List<bool> Calls { get; } = new();
        private readonly IReadOnlyList<WaitDecision> _whenRoundStarted;
        public StubAntiDetection(params WaitDecision[] whenRoundStarted) => _whenRoundStarted = whenRoundStarted;

        public IReadOnlyList<WaitDecision> EvaluateAfterRound(bool roundStarted)
        {
            Calls.Add(roundStarted);
            return roundStarted ? _whenRoundStarted : Array.Empty<WaitDecision>();
        }
    }

    private static WindowInfo Window(nint handle) => new(handle, $"win{handle}", (int)handle);

    private static TargetImage Img(string name, TargetFlag flag) =>
        new() { Name = name, FilePath = $"(fake)/{name}.png", Flag = flag };

    private static EngineOptions Options(int rounds = 1, int repeatStop = 5, bool enableRepeatStop = true) =>
        new()
        {
            Mode = RunMode.ByRounds,
            Rounds = rounds,
            Interval = new IntervalRange(0, 0),
            SetPriority = null,
            AntiDetection = new AntiDetectionOptions
            {
                EnableRepeatStop = enableRepeatStop,
                RepeatSameTargetStop = repeatStop,
            },
        };

    private static async Task<List<EngineEvent>> RunAsync(
        TargetSet set, string?[] script, WindowInfo[] windows, EngineOptions options, IAntiDetectionPolicy stub)
    {
        var engine = new AutomationEngine(
            new FakeWindowService(), new SequenceCapturer(), new ScriptedMatcher(script),
            new RecordingInput(), windows, set,
            sampler: null, random: new Random(1), antiDetection: stub);

        var events = new List<EngineEvent>();
        var consumer = Task.Run(async () =>
        {
            await foreach (var e in engine.Events.ReadAllAsync(CancellationToken.None))
                events.Add(e);
        });

        await engine.RunAsync(options, CancellationToken.None);
        await consumer;
        return events;
    }

    [Fact]
    public async Task Round_end_applies_wait_and_emits_waiting_event()
    {
        var set = new TargetSet("t", new[] { Img("challenge", TargetFlag.RoundStart) });
        var stub = new StubAntiDetection(new WaitDecision(TimeSpan.Zero, "test-wait"));

        var events = await RunAsync(set, new string?[] { "challenge" },
            new[] { Window(1) }, Options(rounds: 1), stub);

        Assert.Contains(events, e => e is Waiting { Reason: "test-wait" });
        Assert.Contains(true, stub.Calls); // 开局轮以 roundStarted=true 调策略
    }

    [Fact]
    public async Task Non_round_start_rounds_report_false_and_emit_no_wait()
    {
        var set = new TargetSet("t", new[] { Img("reward", TargetFlag.Normal) });
        var stub = new StubAntiDetection(new WaitDecision(TimeSpan.Zero, "test-wait"));

        // reward 三连 → 第 3 次触发"连续同名"终止;前两轮均非开局(roundStarted=false)。
        var events = await RunAsync(set, new string?[] { "reward", "reward", "reward" },
            new[] { Window(1) }, Options(rounds: 100, repeatStop: 3), stub);

        Assert.DoesNotContain(events, e => e is Waiting);
        Assert.NotEmpty(stub.Calls);
        Assert.All(stub.Calls, called => Assert.False(called));
    }

    [Fact]
    public async Task Two_windows_starting_same_round_count_as_one_group_round()
    {
        var set = new TargetSet("t", new[] { Img("challenge", TargetFlag.RoundStart) });
        var stub = new StubAntiDetection(new WaitDecision(TimeSpan.Zero, "test-wait"));

        // 双开:同一轮里两个窗口各匹配到 challenge。整组只算一轮 →
        // 策略只以 roundStarted=true 被调用一次,轮末只等一次("要歇一起歇")。
        var events = await RunAsync(set, new string?[] { "challenge", "challenge" },
            new[] { Window(1), Window(2) }, Options(rounds: 1), stub);

        Assert.Equal(new[] { true }, stub.Calls);
        Assert.Single(events, e => e is Waiting);
    }
}
