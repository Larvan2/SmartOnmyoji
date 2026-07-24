using SmartOnmyoji.Core;
using SmartOnmyoji.Core.Abstractions;

namespace SmartOnmyoji.Windows;

/// <summary>
/// 后台点击:向目标窗口发送鼠标消息(lParam = <b>客户区坐标</b>,不再有 <c>-40</c> 之类的偏移修正)。
/// 沿用旧版验证可用的 WM_ACTIVATE + WM_LBUTTONDOWN/UP 序列,按下时长随机化。
/// </summary>
public sealed class SendMessageInput : IInputSender
{
    private readonly Random _random;

    public SendMessageInput(Random? random = null) => _random = random ?? Random.Shared;

    public ClickOutcome Click(nint handle, Point clientPoint)
    {
        if (!NativeMethods.IsWindow(handle))
            return new ClickOutcome(false, Array.Empty<Point>());

        var lParam = MakeLParam(clientPoint.X, clientPoint.Y);
        NativeMethods.SendMessageW(handle, NativeMethods.WM_ACTIVATE, NativeMethods.WA_ACTIVE, 0);
        NativeMethods.SendMessageW(handle, NativeMethods.WM_LBUTTONDOWN, 0, lParam);
        Thread.Sleep(_random.Next(50, 150)); // 拟人化按下时长
        NativeMethods.SendMessageW(handle, NativeMethods.WM_LBUTTONUP, 0, lParam);

        return new ClickOutcome(true, new[] { clientPoint });
    }

    private static nint MakeLParam(int x, int y) => (nint)((y << 16) | (x & 0xFFFF));
}
