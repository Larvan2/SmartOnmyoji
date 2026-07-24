using System.Threading.Channels;
using SmartOnmyoji.Core;
using SmartOnmyoji.Core.Engine;
using SmartOnmyoji.Core.Events;
using SmartOnmyoji.Core.Matching;
using SmartOnmyoji.Core.Targets;
using Xunit;

namespace SmartOnmyoji.Core.Tests;

/// <summary>
/// 真 <see cref="AutomationEngine"/> 的集成单测:用假 capturer/matcher/input 驱动完整循环 + 真状态机,
/// 逐条覆盖 RoundStart / Once / Skip / Stop / 连续同名 / ByRounds 完成 / 取消 / 点击落点 等分支。
/// 全程 <c>Interval(0,0)</c> 无真实等待,快速确定。
/// </summary>
public class AutomationEngineTests
{
    private static readonly WindowInfo Window = new(Handle: 1, Title: "test", ProcessId: 100);

    private static TargetImage Img(string name, TargetFlag flag, int priority, ClickSpec? click = null) =>
        new() { Name = name, FilePath = $"(fake)/{name}.png", Flag = flag, Priority = priority, Click = click };

    private static EngineOptions Options(RunMode mode = RunMode.ByRounds, int rounds = 100,
        int repeatStop = 5, bool enableRepeatStop = true, int deviation = 0) =>
        new()
        {
            Mode = mode,
            Rounds = rounds,
            Interval = new IntervalRange(0, 0),
            SetPriority = null,
            AntiDetection = new AntiDetectionOptions
            {
                ClickDeviation = deviation,
                EnableRepeatStop = enableRepeatStop,
                RepeatSameTargetStop = repeatStop,
            },
        };

    private static async Task<(List<EngineEvent> Events, RecordingInput Input)> RunAsync(
        TargetSet set, string?[] script, EngineOptions options,
        FakeWindowService? windows = null, SequenceCapturer? capturer = null,
        CancellationToken cancellationToken = default)
    {
        windows ??= new FakeWindowService();
        capturer ??= new SequenceCapturer();
        var matcher = new ScriptedMatcher(script);
        var input = new RecordingInput();

        var engine = new AutomationEngine(
            windows, capturer, matcher, input,
            new[] { Window }, set,
            sampler: null, random: new Random(12345));

        var events = new List<EngineEvent>();
        var consumer = Task.Run(async () =>
        {
            await foreach (var evt in engine.Events.ReadAllAsync(CancellationToken.None))
                events.Add(evt);
        });

        await engine.RunAsync(options, cancellationToken);
        await consumer;
        return (events, input);
    }

    [Fact]
    public async Task RoundStart_increments_round_and_emits_event_and_clicks()
    {
        var set = new TargetSet("t", new[] { Img("challenge", TargetFlag.RoundStart, 0) });
        var (events, input) = await RunAsync(set, new string?[] { "challenge" }, Options(rounds: 1));

        Assert.Contains(events, e => e is RoundStarted { Round: 1 });
        Assert.Contains(events, e => e is TargetMatched { Name: "challenge" });
        Assert.Contains(events, e => e is Clicked { Name: "challenge" });
        Assert.Equal(StopReason.Completed, LastStop(events));
        Assert.Single(input.Clicks);
    }

    [Fact]
    public async Task Once_clicks_only_first_time_within_round()
    {
        var set = new TargetSet("t", new[]
        {
            Img("challenge", TargetFlag.RoundStart, 0),
            Img("mark", TargetFlag.Once, 1),
            Img("end", TargetFlag.Stop, 2),
        });
        var (events, input) = await RunAsync(set,
            new string?[] { "challenge", "mark", "mark", "end" }, Options());

        // challenge + 第一次 mark 各点一次;第二次 mark 被 Once 抑制
        Assert.Equal(2, input.Clicks.Count);
        Assert.Single(events, e => e is Clicked { Name: "mark" });
        Assert.Equal(StopReason.StopFlag, LastStop(events));
    }

    [Fact]
    public async Task Skip_matches_but_does_not_click_and_suppresses_once()
    {
        var set = new TargetSet("t", new[]
        {
            Img("challenge", TargetFlag.RoundStart, 0),
            Img("skipimg", TargetFlag.Skip, 1),
            Img("mark", TargetFlag.Once, 2),
            Img("end", TargetFlag.Stop, 3),
        });
        var (events, input) = await RunAsync(set,
            new string?[] { "challenge", "skipimg", "mark", "end" }, Options());

        // 只有 challenge 被点;skip 匹配但不点,且抑制了本回合的 mark
        Assert.Single(input.Clicks);
        Assert.DoesNotContain(events, e => e is Clicked { Name: "skipimg" });
        Assert.DoesNotContain(events, e => e is Clicked { Name: "mark" });
        Assert.Contains(events, e => e is TargetMatched { Name: "skipimg" });
        Assert.Equal(StopReason.StopFlag, LastStop(events));
    }

    [Fact]
    public async Task Stop_flag_terminates_without_clicking()
    {
        var set = new TargetSet("t", new[] { Img("end", TargetFlag.Stop, 0) });
        var (events, input) = await RunAsync(set, new string?[] { "end" }, Options());

        Assert.Empty(input.Clicks);
        Assert.Contains(events, e => e is TargetMatched { Name: "end" });
        Assert.Equal(StopReason.StopFlag, LastStop(events));
    }

    [Fact]
    public async Task Repeated_same_target_terminates()
    {
        var set = new TargetSet("t", new[] { Img("reward", TargetFlag.Normal, 0) });
        var (events, input) = await RunAsync(set,
            new string?[] { "reward", "reward", "reward" }, Options(repeatStop: 3));

        // 前两次点击,第三次触发"连续同名"终止(点击前就被拦下)
        Assert.Equal(2, input.Clicks.Count);
        Assert.Equal(StopReason.RepeatedSameTarget, LastStop(events));
    }

    [Fact]
    public async Task No_match_is_tolerated_and_run_completes()
    {
        var set = new TargetSet("t", new[] { Img("challenge", TargetFlag.RoundStart, 0) });
        // 先一个无匹配周期,再命中 challenge 完成 1 回合
        var (events, input) = await RunAsync(set,
            new string?[] { null, "challenge" }, Options(rounds: 1));

        Assert.Single(input.Clicks);
        Assert.Equal(StopReason.Completed, LastStop(events));
    }

    [Fact]
    public async Task ByRounds_completes_after_reaching_target_rounds()
    {
        // 两个 RoundStart 之间用 reward 隔开:同名相邻的起点图不重复计回合(sameAsLast 判定)
        var set = new TargetSet("t", new[]
        {
            Img("challenge", TargetFlag.RoundStart, 0),
            Img("reward", TargetFlag.Normal, 1),
        });
        var (events, input) = await RunAsync(set,
            new string?[] { "challenge", "reward", "challenge" }, Options(rounds: 2));

        Assert.Contains(events, e => e is RoundStarted { Round: 1 });
        Assert.Contains(events, e => e is RoundStarted { Round: 2 });
        Assert.Equal(StopReason.Completed, LastStop(events));
    }

    [Fact]
    public async Task Cancellation_emits_cancelled_stop()
    {
        var set = new TargetSet("t", new[] { Img("reward", TargetFlag.Normal, 0) });
        using var cts = new CancellationTokenSource();
        // 首次截图即取消 → 本轮结束、进度写入时抛 OCE → Cancelled
        var capturer = new SequenceCapturer { OnCapture = cts.Cancel };
        var (events, _) = await RunAsync(set, new string?[] { "reward" }, Options(rounds: 100),
            capturer: capturer, cancellationToken: cts.Token);

        Assert.Equal(StopReason.Cancelled, LastStop(events));
    }

    [Fact]
    public async Task Normalized_click_spec_maps_to_pixels()
    {
        // 归一化 (0.5, 0.5) → 客户区 1200x600 的 (600,300);deviation=0 时落点即基准点
        var set = new TargetSet("t", new[]
        {
            Img("reward", TargetFlag.Normal, 0,
                click: new ClickSpec(new[] { new NormalizedPoint(0.5, 0.5) })),
            Img("end", TargetFlag.Stop, 1),
        });
        var (_, input) = await RunAsync(set, new string?[] { "reward", "end" }, Options(deviation: 0));

        Assert.Single(input.Clicks);
        Assert.Equal(new Point(600, 300), input.Clicks[0].Point);
    }

    [Fact]
    public async Task Click_marks_thumbnail_with_click_point_and_leaves_noclick_unmarked()
    {
        // reward:Normal 图、无偏移 spec、deviation=0 → 落点即命中中心 (600,300);
        // 缩略图以中心裁 160x120(裁剪原点 520,240),故点击点落在缩略图正中 (80,60)。
        // end:Stop 图不点击 → 缩略图不标红点。
        var set = new TargetSet("t", new[]
        {
            Img("reward", TargetFlag.Normal, 0),
            Img("end", TargetFlag.Stop, 1),
        });
        var (events, _) = await RunAsync(set, new string?[] { "reward", "end" }, Options(deviation: 0));

        var reward = events.OfType<TargetMatched>().First(m => m.Name == "reward");
        Assert.NotNull(reward.Thumbnail);
        Assert.Equal(new Point(80, 60), reward.Thumbnail!.ClickMark);

        var end = events.OfType<TargetMatched>().First(m => m.Name == "end");
        Assert.NotNull(end.Thumbnail);
        Assert.Null(end.Thumbnail!.ClickMark);
    }

    [Fact]
    public async Task Pause_blocks_progress_until_resumed_and_emits_paused_events()
    {
        // 起手即置暂停:引擎应在处理任何窗口前挂起,不截图;订阅方收到 EnginePaused(true) 后立即
        // Resume,借事件流本身做同步(无真实等待),引擎随后继续跑完并按脚本停止。
        var set = new TargetSet("t", new[] { Img("end", TargetFlag.Stop, 0) });
        var windows = new FakeWindowService();
        var captured = false;
        var capturer = new SequenceCapturer { OnCapture = () => captured = true };
        var matcher = new ScriptedMatcher(new string?[] { "end" });
        var input = new RecordingInput();
        var pauseSource = new PauseTokenSource();
        pauseSource.Pause();

        var engine = new AutomationEngine(
            windows, capturer, matcher, input,
            new[] { Window }, set,
            sampler: null, random: new Random(12345), pause: pauseSource.Token);

        var events = new List<EngineEvent>();
        var sawCapturedBeforeResume = false;
        var consumer = Task.Run(async () =>
        {
            await foreach (var evt in engine.Events.ReadAllAsync(CancellationToken.None))
            {
                events.Add(evt);
                if (evt is EnginePaused { Paused: true })
                {
                    sawCapturedBeforeResume = captured;
                    pauseSource.Resume();
                }
            }
        });

        await engine.RunAsync(Options(), CancellationToken.None);
        await consumer;

        Assert.False(sawCapturedBeforeResume);
        Assert.True(captured);
        Assert.Contains(events, e => e is EnginePaused { Paused: true });
        Assert.Contains(events, e => e is EnginePaused { Paused: false });
        Assert.Equal(StopReason.StopFlag, LastStop(events));
    }

    [Fact]
    public async Task Cancellation_while_paused_is_honored()
    {
        // 暂停期间点「停止」(取消)必须立即生效,不能被挂起的 WaitWhilePausedAsync 卡住。
        var set = new TargetSet("t", new[] { Img("reward", TargetFlag.Normal, 0) });
        var windows = new FakeWindowService();
        var captured = false;
        var capturer = new SequenceCapturer { OnCapture = () => captured = true };
        var matcher = new ScriptedMatcher(new string?[] { "reward" });
        var input = new RecordingInput();
        var pauseSource = new PauseTokenSource();
        pauseSource.Pause();
        using var cts = new CancellationTokenSource();

        var engine = new AutomationEngine(
            windows, capturer, matcher, input,
            new[] { Window }, set,
            sampler: null, random: new Random(12345), pause: pauseSource.Token);

        var events = new List<EngineEvent>();
        var consumer = Task.Run(async () =>
        {
            await foreach (var evt in engine.Events.ReadAllAsync(CancellationToken.None))
            {
                events.Add(evt);
                if (evt is EnginePaused { Paused: true })
                    cts.Cancel();
            }
        });

        await engine.RunAsync(Options(rounds: 100), cts.Token);
        await consumer;

        Assert.False(captured);
        Assert.Equal(StopReason.Cancelled, LastStop(events));
    }

    [Fact]
    public async Task Sets_process_priority_at_start_when_configured()
    {
        var set = new TargetSet("t", new[] { Img("end", TargetFlag.Stop, 0) });
        var windows = new FakeWindowService();
        var options = Options() with { SetPriority = ProcessPriority.High };
        await RunAsync(set, new string?[] { "end" }, options, windows: windows);

        Assert.Contains((100, ProcessPriority.High), windows.PriorityCalls);
    }

    private static StopReason? LastStop(IEnumerable<EngineEvent> events) =>
        events.OfType<EngineStopped>().LastOrDefault()?.Reason;
}
