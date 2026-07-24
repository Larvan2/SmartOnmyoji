using System.Threading.Channels;
using SmartOnmyoji.Core.Abstractions;
using SmartOnmyoji.Core.Events;
using SmartOnmyoji.Core.Humanize;
using SmartOnmyoji.Core.Matching;
using SmartOnmyoji.Core.Targets;

namespace SmartOnmyoji.Core.Engine;

/// <summary>
/// 真正的自动化引擎(P4):串起 后台截图 → 优先级匹配 → 回合状态机裁决 → 拟人化后台点击 → 区间等待 的循环。
/// 全部平台能力经 <see cref="IScreenCapturer"/>/<see cref="IMatcher"/>/<see cref="IInputSender"/>/<see cref="IWindowService"/>
/// 抽象注入,因此可用假实现离线单测;引擎经 <see cref="Events"/> 通道发结构化事件,<b>不知道 UI 存在</b>。
/// 取代旧 run():无 <c>print</c>、无 <c>terminate()</c>、无魔法上限;坐标全程走客户区坐标系、无缩放换算。
/// </summary>
public sealed class AutomationEngine : IAutomationEngine
{
    private readonly Channel<EngineEvent> _channel =
        Channel.CreateUnbounded<EngineEvent>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = true });

    private readonly IWindowService _windows;
    private readonly IScreenCapturer _capturer;
    private readonly IMatcher _matcher;
    private readonly IInputSender _input;
    private readonly OffsetSampler _sampler;
    private readonly IReadOnlyList<WindowInfo> _targets;
    private readonly TargetSet _targetSet;
    private readonly Random _random;
    private readonly IAntiDetectionPolicy? _antiDetectionOverride;
    private readonly PauseToken _pause;

    public AutomationEngine(
        IWindowService windows,
        IScreenCapturer capturer,
        IMatcher matcher,
        IInputSender input,
        IReadOnlyList<WindowInfo> targetWindows,
        TargetSet targetSet,
        OffsetSampler? sampler = null,
        Random? random = null,
        IAntiDetectionPolicy? antiDetection = null,
        PauseToken pause = default)
    {
        _windows = windows;
        _capturer = capturer;
        _matcher = matcher;
        _input = input;
        _targets = targetWindows;
        _targetSet = targetSet;
        _random = random ?? Random.Shared;
        _sampler = sampler ?? new OffsetSampler(_random);
        _antiDetectionOverride = antiDetection;
        _pause = pause;
    }

    public ChannelReader<EngineEvent> Events => _channel.Reader;

    public async Task RunAsync(EngineOptions options, CancellationToken cancellationToken)
    {
        var writer = _channel.Writer;
        // 多开时用一台状态机集中裁决(对齐设计文档 §7);逐窗口独立状态列为后续细化。
        var state = new RoundStateMachine(
            options.AntiDetection.EnableRepeatStop,
            options.AntiDetection.RepeatSameTargetStop);

        // 组级防检测策略:整个运行共享一份状态(计数/频次窗口/上次等待),等待在轮末统一施加。
        var antiDetection = _antiDetectionOverride
            ?? new AntiDetectionPolicy(options.AntiDetection, random: _random);

        var deadline = options.Mode == RunMode.ByMinutes
            ? DateTimeOffset.Now + options.Duration
            : DateTimeOffset.MaxValue;

        try
        {
            await writer.WriteAsync(
                new LogMessage(EngineLogLevel.Info,
                    $"引擎启动:{_targets.Count} 个窗口 · 目标集「{_targetSet.Name}」({_targetSet.Images.Count} 图) · " +
                    (options.Mode == RunMode.ByRounds ? $"按 {options.Rounds} 回合" : $"按 {options.Duration:hh\\:mm\\:ss}")),
                cancellationToken);

            TrySetPriority(options, writer, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                // 仅在回合边界响应暂停:不会打断正在进行中的截图/匹配/点击。
                if (_pause.IsPaused)
                {
                    await writer.WriteAsync(new EnginePaused(true), cancellationToken);
                    await _pause.WaitWhilePausedAsync(cancellationToken);
                    await writer.WriteAsync(new EnginePaused(false), cancellationToken);
                }

                if (Finished(options, state, deadline))
                    break;

                // 一轮:逐窗口处理,聚合"本轮是否有任一窗口开局"(多窗口的 RoundStart 合并为组的一次)。
                var roundStarted = false;
                foreach (var window in _targets)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var outcome = await ProcessWindowAsync(window, options, state, writer, cancellationToken);
                    if (outcome.Stop is { } reason)
                    {
                        await writer.WriteAsync(new EngineStopped(reason), CancellationToken.None);
                        return;
                    }
                    roundStarted |= outcome.RoundStarted;
                }

                cancellationToken.ThrowIfCancellationRequested();

                // 轮末统一施加防检测等待:作用于整个循环("要歇一起歇"),
                // 从根上避免旧版"一个窗口开局、另一个窗口在等"的双开组队错位。
                foreach (var wait in antiDetection.EvaluateAfterRound(roundStarted))
                {
                    await writer.WriteAsync(new Waiting(wait.Duration, wait.Reason), cancellationToken);
                    await Task.Delay(wait.Duration, cancellationToken);
                }

                await DelayIntervalAsync(options.Interval, cancellationToken);
                await writer.WriteAsync(new ProgressChanged(ComputeProgress(options, state, deadline)), cancellationToken);
            }

            await writer.WriteAsync(new EngineStopped(StopReason.Completed), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            await writer.WriteAsync(new EngineStopped(StopReason.Cancelled), CancellationToken.None);
        }
        finally
        {
            writer.TryComplete();
        }
    }

    /// <summary>单窗口一轮的结果:是否要求终止整个运行,以及本轮该窗口是否开局(RoundStart)。</summary>
    private readonly record struct WindowOutcome(StopReason? Stop, bool RoundStarted);

    /// <summary>
    /// 处理单个窗口一轮:截图 → 匹配 → 裁决 → 点击。
    /// <see cref="WindowOutcome.Stop"/> 非 null 表示应终止整个运行(状态机给出 Stop);
    /// <see cref="WindowOutcome.RoundStarted"/> 供引擎在轮末聚合,交给防检测策略按"局"计数。
    /// 单窗口内的异常(句柄失效、截图失败)只告警并跳过(返回 default),不拖垮其它窗口。
    /// </summary>
    private async Task<WindowOutcome> ProcessWindowAsync(
        WindowInfo window, EngineOptions options, RoundStateMachine state,
        ChannelWriter<EngineEvent> writer, CancellationToken ct)
    {
        if (!_windows.IsAlive(window.Handle))
        {
            await writer.WriteAsync(new LogMessage(EngineLogLevel.Warning, $"窗口已失效,跳过:{window.Title}"), ct);
            return default;
        }

        CaptureFrame frame;
        MatchResult? hit;
        try
        {
            frame = _capturer.Capture(window.Handle);
            hit = _matcher.MatchFirst(frame, _targetSet, options.Match);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await writer.WriteAsync(new EngineError($"处理窗口「{window.Title}」出错:{ex.Message}"), ct);
            return default;
        }

        if (hit is null)
        {
            state.NotifyNoMatch();
            return default;
        }

        var decision = state.Decide(hit.Target);

        // 先算好本轮实际点击落点(仅当要点击时),以便在命中缩略图上标出"点在了哪儿";
        // 不点击的分支(Skip/Stop/mark)clickPoint 为 null,缩略图不标红点。
        var clickPoint = decision.Kind == DecisionKind.Click
            ? _sampler.Sample(ResolveClickBase(hit, frame), frame.Width, frame.Height, options.AntiDetection.ClickDeviation)
            : (Point?)null;

        await writer.WriteAsync(
            new TargetMatched(hit.Target.Name, hit.Center, hit.Score) { Thumbnail = CropGray(frame, hit.Center, clickPoint) },
            ct);

        var roundStarted = decision.RoundStarted is not null;
        if (decision.RoundStarted is { } round)
            await writer.WriteAsync(new RoundStarted(round), ct);

        if (decision.Kind == DecisionKind.Stop)
            return new WindowOutcome(decision.Reason ?? StopReason.Completed, roundStarted);

        if (clickPoint is { } cp)
        {
            var outcome = _input.Click(window.Handle, cp);
            if (outcome.Success)
                await writer.WriteAsync(new Clicked(hit.Target.Name, outcome.Points), ct);
            else
                await writer.WriteAsync(new LogMessage(EngineLogLevel.Warning, $"点击未被接收:{hit.Target.Name}"), ct);
        }

        return new WindowOutcome(null, roundStarted);
    }

    private const int ThumbWidth = 160;
    private const int ThumbHeight = 120;

    /// <summary>
    /// 从灰度帧裁一块以命中中心为中心的缩略图(边界截断);帧异常时返回 null。供 UI 展示命中处。
    /// <paramref name="clickPoint"/> 非 null 且落在裁剪框内时,换算成缩略图坐标记入 <see cref="GrayThumbnail.ClickMark"/>,
    /// 供 UI 标红点。
    /// </summary>
    private static GrayThumbnail? CropGray(CaptureFrame frame, Point center, Point? clickPoint)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Gray.Length < frame.Width * frame.Height)
            return null;

        var w = Math.Min(ThumbWidth, frame.Width);
        var h = Math.Min(ThumbHeight, frame.Height);
        var x = Math.Clamp(center.X - w / 2, 0, frame.Width - w);
        var y = Math.Clamp(center.Y - h / 2, 0, frame.Height - h);

        var gray = new byte[w * h];
        for (var row = 0; row < h; row++)
            Array.Copy(frame.Gray, (y + row) * frame.Width + x, gray, row * w, w);

        // 点击落点换算到缩略图坐标系;落在框外则不标记(mark 保持 null)。
        Point? mark = null;
        if (clickPoint is { } cp)
        {
            var mx = cp.X - x;
            var my = cp.Y - y;
            if (mx >= 0 && mx < w && my >= 0 && my < h)
                mark = new Point(mx, my);
        }
        return new GrayThumbnail(w, h, gray) { ClickMark = mark };
    }

    /// <summary>
    /// 决定点击基准点:关键图有偏移点击定义(客户区归一化坐标)则在候选点里随机取一并换算为像素,
    /// 否则用匹配中心点。随后交给 <see cref="OffsetSampler"/> 做拟人化抖动。
    /// </summary>
    private Point ResolveClickBase(MatchResult hit, CaptureFrame frame)
    {
        if (hit.Target.Click is { Points.Count: > 0 } spec)
        {
            var normalized = spec.Points[_random.Next(spec.Points.Count)];
            return normalized.ToPixel(frame.Width, frame.Height);
        }
        return hit.Center;
    }

    private void TrySetPriority(EngineOptions options, ChannelWriter<EngineEvent> writer, CancellationToken ct)
    {
        if (options.SetPriority is not { } priority) return;
        foreach (var pid in _targets.Select(w => w.ProcessId).Distinct())
        {
            try
            {
                _windows.SetPriority(pid, priority);
            }
            catch (Exception ex)
            {
                // 设优先级通常需管理员;失败不该中断运行。
                writer.TryWrite(new LogMessage(EngineLogLevel.Warning, $"设置进程 {pid} 优先级失败(可能需管理员):{ex.Message}"));
            }
        }
    }

    private async Task DelayIntervalAsync(IntervalRange interval, CancellationToken ct)
    {
        var min = Math.Max(0, interval.MinSeconds);
        var max = Math.Max(min, interval.MaxSeconds);
        var seconds = min + _random.NextDouble() * (max - min);
        if (seconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
    }

    private static bool Finished(EngineOptions options, RoundStateMachine state, DateTimeOffset deadline) =>
        options.Mode switch
        {
            RunMode.ByRounds => state.Round >= options.Rounds,
            RunMode.ByMinutes => DateTimeOffset.Now >= deadline,
            _ => false,
        };

    private static int ComputeProgress(EngineOptions options, RoundStateMachine state, DateTimeOffset deadline)
    {
        if (options.Mode == RunMode.ByRounds)
            return options.Rounds <= 0 ? 100 : Math.Clamp(state.Round * 100 / options.Rounds, 0, 100);

        var span = options.Duration.TotalSeconds;
        if (span <= 0) return 100;
        var remaining = (deadline - DateTimeOffset.Now).TotalSeconds;
        return Math.Clamp((int)((span - remaining) / span * 100), 0, 100);
    }
}
