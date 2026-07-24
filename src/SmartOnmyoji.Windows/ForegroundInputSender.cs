using SmartOnmyoji.Core;
using SmartOnmyoji.Core.Abstractions;

namespace SmartOnmyoji.Windows;

/// <summary>
/// 前台点击(旧版"兼容模式"):把窗口切到前台 → 移动真实光标到目标屏幕坐标 → 真实鼠标按下/弹起。
/// 适用于后台合成消息点不动的窗口(按钮用真实光标做命中判定的游戏)。
/// 代价:会占用真实鼠标与前台焦点。坐标入参为<b>客户区坐标</b>,内部经 ClientToScreen 换算。
/// </summary>
public sealed class ForegroundInputSender : IInputSender
{
    private readonly Random _random;
    private readonly bool _restoreCursor;

    public ForegroundInputSender(Random? random = null, bool restoreCursor = true)
    {
        _random = random ?? Random.Shared;
        _restoreCursor = restoreCursor;
    }

    public ClickOutcome Click(nint handle, Point clientPoint)
    {
        if (!NativeMethods.IsWindow(handle))
            return new ClickOutcome(false, Array.Empty<Point>());

        // 客户区坐标 → 屏幕坐标(物理像素,进程为 PerMonitor 感知)
        var screen = new POINT { X = clientPoint.X, Y = clientPoint.Y };
        NativeMethods.ClientToScreen(handle, ref screen);

        NativeMethods.GetCursorPos(out var previous);

        NativeMethods.SetForegroundWindow(handle);
        NativeMethods.SetCursorPos(screen.X, screen.Y);
        Thread.Sleep(_random.Next(20, 60)); // 让光标"到位"

        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        Thread.Sleep(_random.Next(50, 150)); // 拟人化按下时长
        NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);

        if (_restoreCursor)
            NativeMethods.SetCursorPos(previous.X, previous.Y);

        return new ClickOutcome(true, new[] { clientPoint });
    }
}
