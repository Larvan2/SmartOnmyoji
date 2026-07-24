using System.Text.Json;
using System.Threading.Channels;
using SmartOnmyoji.Core;
using SmartOnmyoji.Core.Abstractions;
using SmartOnmyoji.Core.Config;
using SmartOnmyoji.Core.Engine;
using SmartOnmyoji.Core.Events;
using SmartOnmyoji.Core.Targets;
using SmartOnmyoji.Vision;
using SmartOnmyoji.Windows;

namespace SmartOnmyoji.Host.Ui;

public sealed record StartResult(bool Ok, string? Error)
{
    public static StartResult Success() => new(true, null);
    public static StartResult Fail(string error) => new(false, error);
}

/// <summary>
/// WebUI 与引擎之间的单例桥。持有引擎生命周期(启动/停止),把 <see cref="AutomationEngine"/> 的
/// <see cref="EngineEvent"/> 序列化后扇出给所有 SSE 订阅者,并维护一份可查询的运行状态。
/// 引擎本身仍<b>不知道 UI 存在</b>——这里是唯一把事件流接到 HTTP 的地方(对齐设计文档 §10)。
/// </summary>
public sealed class EngineManager
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private const int RecentMax = 200;

    private readonly IWindowService _windows;
    private readonly object _gate = new();
    private readonly HashSet<Channel<string>> _subscribers = new();
    private readonly LinkedList<string> _recent = new();

    private CancellationTokenSource? _cts;

    // 运行状态(全部在 _gate 下读写)
    private bool _running;
    private string? _targetSet;
    private int _windowCount;
    private int _round;
    private int _progress;
    private DateTimeOffset? _startedAt;

    public EngineManager(IWindowService windows) => _windows = windows;

    public StatusDto Status()
    {
        lock (_gate)
            return new StatusDto(_running, _targetSet, _windowCount, _round, _progress, _startedAt);
    }

    public StartResult Start(StartRequest req)
    {
        lock (_gate)
        {
            if (_running) return StartResult.Fail("引擎已在运行,先停止再启动。");

            var all = _windows.Enumerate();
            var targets = new List<WindowInfo>();
            foreach (var h in req.WindowHandles)
            {
                if (!TryParseHandle(h, out var handle)) return StartResult.Fail($"句柄格式非法: {h}");
                var w = all.FirstOrDefault(x => x.Handle == handle);
                if (w is null) return StartResult.Fail($"找不到窗口 {h}(可能已关闭,请刷新)。");
                targets.Add(w);
            }
            if (targets.Count == 0) return StartResult.Fail("未选择任何游戏窗口。");

            var built = TargetSetCatalog.Build(req.TargetSet);
            if (built is null) return StartResult.Fail($"目标集「{req.TargetSet}」下没有可用模板(jpg/png)。");

            var options = OptionsMapping.ToOptions(req.Options);
            var errors = EngineOptionsValidation.Validate(options);
            if (errors.Count > 0) return StartResult.Fail("配置校验失败:" + string.Join("; ", errors));

            // 记住本次(合法)运行参数到 config.json,作为前端防抖写回之外的兜底。
            OptionsStore.Save(req.Options);

            _cts = new CancellationTokenSource();
            _running = true;
            _targetSet = req.TargetSet;
            _windowCount = targets.Count;
            _round = 0;
            _progress = 0;
            _startedAt = DateTimeOffset.Now;

            var token = _cts.Token;
            _ = Task.Run(() => RunAsync(options, targets, built.Value.Set, built.Value.Warnings, token));
        }

        BroadcastState();
        return StartResult.Success();
    }

    public bool Stop()
    {
        lock (_gate)
        {
            if (!_running || _cts is null) return false;
            _cts.Cancel();
            return true;
        }
    }

    /// <summary>订阅事件流:先补一份状态快照 + 最近事件,再进入实时推送。返回读端与退订句柄。</summary>
    public (ChannelReader<string> Reader, IDisposable Subscription) Subscribe()
    {
        var ch = Channel.CreateBounded<string>(
            new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

        lock (_gate)
        {
            _subscribers.Add(ch);
            ch.Writer.TryWrite(StateEnvelope());
            foreach (var r in _recent) ch.Writer.TryWrite(r);
        }
        return (ch.Reader, new Subscription(this, ch));
    }

    private async Task RunAsync(
        EngineOptions options,
        IReadOnlyList<WindowInfo> targets,
        TargetSet set,
        IReadOnlyList<string> warnings,
        CancellationToken ct)
    {
        var matcher = new TemplateMatcher();
        try
        {
            foreach (var w in warnings)
                Publish(LogEnvelope("Warning", "[目标集] " + w));

            var engine = new AutomationEngine(
                _windows, new GdiWindowCapturer(), matcher, new SendMessageInput(), targets, set);

            var consume = Task.Run(async () =>
            {
                await foreach (var evt in engine.Events.ReadAllAsync(CancellationToken.None))
                    OnEngineEvent(evt);
            });

            await engine.RunAsync(options, ct);
            await consume;
        }
        catch (Exception ex)
        {
            Publish(LogEnvelope("Error", "引擎异常:" + ex.Message));
        }
        finally
        {
            matcher.Dispose();
            lock (_gate)
            {
                _running = false;
                _cts?.Dispose();
                _cts = null;
            }
            BroadcastState();
        }
    }

    private void OnEngineEvent(EngineEvent evt)
    {
        var stateChanged = false;
        lock (_gate)
        {
            switch (evt)
            {
                case RoundStarted r: _round = r.Round; stateChanged = true; break;
                case ProgressChanged p: _progress = p.Percent; stateChanged = true; break;
            }
        }
        Publish(EventEnvelope(evt));
        if (stateChanged) BroadcastState();
    }

    private void Publish(string json)
    {
        lock (_gate)
        {
            _recent.AddLast(json);
            while (_recent.Count > RecentMax) _recent.RemoveFirst();
            foreach (var s in _subscribers) s.Writer.TryWrite(json);
        }
    }

    private void BroadcastState()
    {
        var json = StateEnvelope();
        lock (_gate)
            foreach (var s in _subscribers) s.Writer.TryWrite(json);
    }

    private string StateEnvelope()
    {
        var d = new Dictionary<string, object?>
        {
            ["type"] = "state",
            ["running"] = _running,
            ["targetSet"] = _targetSet,
            ["windowCount"] = _windowCount,
            ["round"] = _round,
            ["progress"] = _progress,
            ["startedAt"] = _startedAt,
        };
        return JsonSerializer.Serialize(d, JsonOpts);
    }

    private static string LogEnvelope(string level, string text) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = "log",
            ["level"] = level,
            ["text"] = text,
            ["at"] = DateTimeOffset.Now,
        }, JsonOpts);

    private static string EventEnvelope(EngineEvent e)
    {
        var d = new Dictionary<string, object?> { ["at"] = e.At };
        switch (e)
        {
            case RoundStarted r:
                d["type"] = "round"; d["round"] = r.Round; break;
            case TargetMatched m:
                d["type"] = "matched"; d["name"] = m.Name; d["x"] = m.Position.X; d["y"] = m.Position.Y; d["score"] = m.Score;
                if (m.Thumbnail is { } t)
                {
                    d["thumb"] = "data:image/png;base64," + Convert.ToBase64String(DebugImage.EncodeGrayPng(t.Width, t.Height, t.Gray));
                    // 点击落点归一化到缩略图尺寸(0~1),前端百分比定位红点,随缩放自适应。
                    if (t.ClickMark is { } mk && t.Width > 0 && t.Height > 0)
                        d["mark"] = new { x = (double)mk.X / t.Width, y = (double)mk.Y / t.Height };
                }
                break;
            case Clicked c:
                d["type"] = "clicked"; d["name"] = c.Name;
                d["points"] = c.Points.Select(p => new { x = p.X, y = p.Y }).ToArray(); break;
            case Waiting w:
                d["type"] = "waiting"; d["seconds"] = w.Duration.TotalSeconds; d["reason"] = w.Reason; break;
            case ProgressChanged p:
                d["type"] = "progress"; d["percent"] = p.Percent; break;
            case EngineStopped s:
                d["type"] = "stopped"; d["reason"] = s.Reason.ToString(); break;
            case EngineError err:
                d["type"] = "error"; d["message"] = err.Message; break;
            case LogMessage l:
                d["type"] = "log"; d["level"] = l.Level.ToString(); d["text"] = l.Text; break;
            default:
                d["type"] = "unknown"; break;
        }
        return JsonSerializer.Serialize(d, JsonOpts);
    }

    private static bool TryParseHandle(string s, out nint handle)
    {
        handle = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var hex = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        try
        {
            handle = (nint)Convert.ToInt64(hex ? s[2..] : s, hex ? 16 : 10);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void Unsubscribe(Channel<string> ch)
    {
        lock (_gate)
        {
            _subscribers.Remove(ch);
            ch.Writer.TryComplete();
        }
    }

    private sealed class Subscription(EngineManager owner, Channel<string> ch) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.Unsubscribe(ch);
        }
    }
}
