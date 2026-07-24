using System.Threading.Channels;
using SmartOnmyoji.Core.Events;
using SmartOnmyoji.Core.Targets;

namespace SmartOnmyoji.Core.Engine;

/// <summary>
/// P0 用的假引擎:不接任何 Win32/CV,按脚本喂给真正的 <see cref="RoundStateMachine"/>,
/// 通过 Channel 发出真实事件流。用于打通"命令 → 引擎 → 事件 → UI"这条管道,
/// 并在没有平台实现时验证 Core 端到端可运行。真引擎在 P4 落地。
/// </summary>
public sealed class FakeAutomationEngine : IAutomationEngine
{
    private readonly Channel<EngineEvent> _channel =
        Channel.CreateUnbounded<EngineEvent>(new UnboundedChannelOptions { SingleReader = false });

    private readonly TimeSpan _stepDelay;

    public FakeAutomationEngine(TimeSpan? stepDelay = null)
        => _stepDelay = stepDelay ?? TimeSpan.FromMilliseconds(200);

    public ChannelReader<EngineEvent> Events => _channel.Reader;

    public async Task RunAsync(EngineOptions options, CancellationToken cancellationToken)
    {
        var writer = _channel.Writer;
        var state = new RoundStateMachine(
            options.AntiDetection.EnableRepeatStop,
            options.AntiDetection.RepeatSameTargetStop);

        // 一个模拟回合的脚本:起点 → 普通奖励 → 标记(每回合一次) → 另一奖励
        var script = new[]
        {
            Target("challenge", TargetFlag.RoundStart),
            Target("reward", TargetFlag.Normal),
            Target("mark", TargetFlag.Once),
            Target("reward2", TargetFlag.Normal),
        };

        try
        {
            await writer.WriteAsync(new LogMessage(EngineLogLevel.Info, "假引擎启动"), cancellationToken);

            var step = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                if (options.Mode == RunMode.ByRounds && state.Round >= options.Rounds)
                    break;

                var hit = script[step % script.Length];
                step++;

                var decision = state.Decide(hit);

                if (decision.RoundStarted is { } round)
                    await writer.WriteAsync(new RoundStarted(round), cancellationToken);

                await writer.WriteAsync(new TargetMatched(hit.Name, new Point(880, 610), 0.93), cancellationToken);

                if (decision.Kind == DecisionKind.Stop)
                {
                    await writer.WriteAsync(new EngineStopped(decision.Reason ?? StopReason.Completed), CancellationToken.None);
                    return;
                }

                if (decision.Kind == DecisionKind.Click)
                    await writer.WriteAsync(new Clicked(hit.Name, new[] { new Point(880, 610) }), cancellationToken);

                if (options.Mode == RunMode.ByRounds && options.Rounds > 0)
                    await writer.WriteAsync(new ProgressChanged(Math.Min(100, state.Round * 100 / options.Rounds)), cancellationToken);

                await Task.Delay(_stepDelay, cancellationToken);
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

        static TargetImage Target(string name, TargetFlag flag) =>
            new() { Name = name, FilePath = $"(fake)/{name}.png", Flag = flag };
    }
}
