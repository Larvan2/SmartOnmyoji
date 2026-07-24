using SmartOnmyoji.Core.Matching;
using SmartOnmyoji.Core.Targets;

namespace SmartOnmyoji.Core.Abstractions;

/// <summary>窗口枚举、查找、句柄、客户区换算、优先级、前台点选。由 Windows 层实现。</summary>
public interface IWindowService
{
    IReadOnlyList<WindowInfo> Enumerate();
    WindowInfo? Find(string title);
    WindowInfo? GetForeground();
    ClientArea GetClientArea(nint handle);
    bool IsAlive(nint handle);
    void SetPriority(int processId, ProcessPriority priority);
}

/// <summary>后台窗口截图。由 Windows 层(GDI/PrintWindow,后续可换 DXGI)实现。</summary>
public interface IScreenCapturer
{
    CaptureFrame Capture(nint handle);
}

/// <summary>图像匹配。纯函数式:输入帧+目标+选项 → 结果。由 Vision 层(OpenCvSharp)实现。</summary>
public interface IMatcher
{
    MatchResult? Match(CaptureFrame frame, TargetImage target, MatchOptions options);
}

/// <summary>后台点击(客户区坐标)。由 Windows 层(PostMessage/SendMessage)实现。</summary>
public interface IInputSender
{
    ClickOutcome Click(nint handle, Point clientPoint);
}
