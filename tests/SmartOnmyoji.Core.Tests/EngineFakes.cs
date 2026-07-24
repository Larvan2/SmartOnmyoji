using SmartOnmyoji.Core;
using SmartOnmyoji.Core.Abstractions;
using SmartOnmyoji.Core.Matching;
using SmartOnmyoji.Core.Targets;

namespace SmartOnmyoji.Core.Tests;

/// <summary>
/// 假截图器:每次 <see cref="Capture"/> 产出一帧,并在 gray[0] 打上自增的"周期号",
/// 供 <see cref="ScriptedMatcher"/> 判断进入了新的一轮(引擎每个外层循环 = 每窗口一次 Capture)。
/// </summary>
public sealed class SequenceCapturer : IScreenCapturer
{
    public int Width { get; init; } = 1200;
    public int Height { get; init; } = 600;

    /// <summary>可注入的副作用(如在首次截图时取消 CTS),用于取消测试。</summary>
    public Action? OnCapture { get; init; }

    private int _cycle;

    public CaptureFrame Capture(nint handle)
    {
        OnCapture?.Invoke();
        var gray = new byte[Width * Height];
        gray[0] = (byte)(_cycle++ & 0xFF); // 周期号(256 内不重复,测试足够)
        return new CaptureFrame(Width, Height, gray);
    }
}

/// <summary>
/// 脚本化匹配器:按"每周期应命中的目标名"数组回放。<c>null</c> 表示该周期无匹配。
/// 借帧上的周期号推进脚本;同一帧内(MatchFirst 会按优先级对每张图各调一次 Match)用同一脚本项。
/// </summary>
public sealed class ScriptedMatcher : IMatcher
{
    private readonly string?[] _script;
    private int _index = -1;
    private int _lastFrameId = -1;

    public ScriptedMatcher(params string?[] script) => _script = script;

    public MatchResult? Match(CaptureFrame frame, TargetImage target, MatchOptions options)
    {
        int frameId = frame.Gray[0];
        if (frameId != _lastFrameId)
        {
            _lastFrameId = frameId;
            _index++;
        }

        if (_index < 0 || _index >= _script.Length) return null;

        var expected = _script[_index];
        return expected == target.Name
            ? new MatchResult(target, new Point(600, 300), 0.95)
            : null;
    }
}

/// <summary>记录所有点击,断言点击次数/落点。</summary>
public sealed class RecordingInput : IInputSender
{
    public List<(nint Handle, Point Point)> Clicks { get; } = new();

    /// <summary>置 false 模拟点击未被接收。</summary>
    public bool Succeed { get; init; } = true;

    public ClickOutcome Click(nint handle, Point clientPoint)
    {
        Clicks.Add((handle, clientPoint));
        return new ClickOutcome(Succeed, Succeed ? new[] { clientPoint } : Array.Empty<Point>());
    }
}

/// <summary>假窗口服务:引擎只用到 IsAlive / SetPriority,其余给出最小实现。</summary>
public sealed class FakeWindowService : IWindowService
{
    public bool Alive { get; set; } = true;
    public List<(int Pid, ProcessPriority Priority)> PriorityCalls { get; } = new();

    public bool IsAlive(nint handle) => Alive;
    public void SetPriority(int processId, ProcessPriority priority) => PriorityCalls.Add((processId, priority));

    public IReadOnlyList<WindowInfo> Enumerate() => Array.Empty<WindowInfo>();
    public WindowInfo? Find(string title) => null;
    public WindowInfo? GetForeground() => null;
    public ClientArea GetClientArea(nint handle) => new(1200, 600, new Point(0, 0));
}
