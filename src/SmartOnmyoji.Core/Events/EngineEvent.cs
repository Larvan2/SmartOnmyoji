using SmartOnmyoji.Core.Engine;

namespace SmartOnmyoji.Core.Events;

public enum EngineLogLevel { Debug, Info, Warning, Error }

/// <summary>引擎对外发出的事件基类。引擎不知道 UI 存在;Host 层把事件桥接到 SignalR/日志。</summary>
public abstract record EngineEvent
{
    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;
}

public sealed record RoundStarted(int Round) : EngineEvent;

/// <summary>命中位置附近的灰度缩略图(客户区裁剪)。Core 只持中立灰度表示,由 Host 编码成图给 UI。</summary>
public sealed record GrayThumbnail(int Width, int Height, byte[] Gray)
{
    /// <summary>
    /// 实际点击落点在缩略图内的像素坐标(缩略图坐标系);null 表示本次未点击(Skip/Stop/mark)
    /// 或点击点落在缩略图裁剪框外。UI 据此在缩略图上标红点,直观呈现"点在了哪儿"。
    /// </summary>
    public Point? ClickMark { get; init; }
}

public sealed record TargetMatched(string Name, Point Position, double Score) : EngineEvent
{
    /// <summary>命中处的灰度缩略图,供 UI 展示;为 null 表示本次未附带。</summary>
    public GrayThumbnail? Thumbnail { get; init; }
}
public sealed record Clicked(string Name, IReadOnlyList<Point> Points) : EngineEvent;
public sealed record Waiting(TimeSpan Duration, string Reason) : EngineEvent;
public sealed record ProgressChanged(int Percent) : EngineEvent;
public sealed record EngineStopped(StopReason Reason) : EngineEvent;
public sealed record EngineError(string Message) : EngineEvent;
public sealed record LogMessage(EngineLogLevel Level, string Text) : EngineEvent;
