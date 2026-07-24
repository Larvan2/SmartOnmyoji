using SmartOnmyoji.Core.Engine;
using SmartOnmyoji.Core.Events;
using Xunit;

namespace SmartOnmyoji.Core.Tests;

public class FakeAutomationEngineTests
{
    private static async Task<List<EngineEvent>> DrainAsync(IAutomationEngine engine, EngineOptions options, CancellationToken ct)
    {
        var events = new List<EngineEvent>();
        var consume = Task.Run(async () =>
        {
            await foreach (var e in engine.Events.ReadAllAsync(CancellationToken.None))
                events.Add(e);
        }, CancellationToken.None);

        await engine.RunAsync(options, ct);
        await consume;
        return events;
    }

    [Fact]
    public async Task Emits_round_click_and_completes()
    {
        var engine = new FakeAutomationEngine(TimeSpan.FromMilliseconds(1));
        var events = await DrainAsync(engine, new EngineOptions { Mode = RunMode.ByRounds, Rounds = 2 }, CancellationToken.None);

        Assert.Contains(events, e => e is RoundStarted);
        Assert.Contains(events, e => e is Clicked);
        Assert.Contains(events, e => e is EngineStopped { Reason: StopReason.Completed });
    }

    [Fact]
    public async Task Cancellation_stops_with_cancelled_reason()
    {
        var engine = new FakeAutomationEngine(TimeSpan.FromMilliseconds(200));
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(20));

        var events = await DrainAsync(engine, new EngineOptions { Mode = RunMode.ByRounds, Rounds = 1000 }, cts.Token);

        Assert.Contains(events, e => e is EngineStopped { Reason: StopReason.Cancelled });
    }
}
