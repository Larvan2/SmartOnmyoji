namespace SmartOnmyoji.Core;

/// <summary>整数像素坐标(默认为目标窗口客户区坐标系)。</summary>
public readonly record struct Point(int X, int Y);

public readonly record struct Size(int Width, int Height);

/// <summary>
/// 客户区归一化坐标,X/Y ∈ [0,1]。乘以当前客户区尺寸即得像素坐标,分辨率/缩放无关。
/// 用于表达偏移点击点,取代旧 img_pos.json 里绝对像素 + real_pos + scal_rate 的缩放换算。
/// </summary>
public readonly record struct NormalizedPoint(double X, double Y)
{
    public Point ToPixel(int width, int height) => new((int)(X * width), (int)(Y * height));
}

/// <summary>窗口矩形(屏幕坐标)。</summary>
public readonly record struct Rect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>
/// 目标窗口客户区:尺寸 + 客户区左上角在屏幕上的原点。
/// 用于客户区坐标 ↔ 屏幕坐标 的换算,取代旧代码里 <c>-40</c> 之类的硬编码修正。
/// </summary>
public readonly record struct ClientArea(int Width, int Height, Point ScreenOrigin);

public sealed record WindowInfo(nint Handle, string Title, int ProcessId);

public enum ProcessPriority { Idle, BelowNormal, Normal, AboveNormal, High, Realtime }

/// <summary>
/// 一帧灰度截图。像素按行主序,长度 = <see cref="Width"/> * <see cref="Height"/>。
/// Vision 层负责转换为 OpenCV Mat;Core 只持有中立表示,不依赖任何 CV 库。
/// </summary>
public sealed record CaptureFrame(int Width, int Height, byte[] Gray);

public sealed record ClickOutcome(bool Success, IReadOnlyList<Point> Points);
