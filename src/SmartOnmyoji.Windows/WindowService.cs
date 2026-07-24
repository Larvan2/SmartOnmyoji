using System.Text;
using SmartOnmyoji.Core;
using SmartOnmyoji.Core.Abstractions;

namespace SmartOnmyoji.Windows;

/// <summary>子窗口诊断信息:句柄、窗口类名、客户区尺寸、是否可见。</summary>
public sealed record ChildWindow(nint Handle, string ClassName, int ClientWidth, int ClientHeight, bool Visible);

/// <summary>窗口几何 + DPI 诊断:定位后台点击坐标空间问题。</summary>
public sealed record GeometryInfo(
    Rect WindowRect, int ClientWidth, int ClientHeight, Point ClientOrigin,
    int LeftBorder, int TopBar,
    uint WindowDpi, uint SystemDpi, int WindowAwareness, int ProcessAwareness);

public sealed class WindowService : IWindowService
{
    public IReadOnlyList<WindowInfo> Enumerate()
    {
        var list = new List<WindowInfo>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (NativeMethods.IsWindowVisible(hWnd))
            {
                var title = GetTitle(hWnd);
                if (!string.IsNullOrEmpty(title))
                {
                    NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
                    list.Add(new WindowInfo(hWnd, title, (int)pid));
                }
            }
            return true;
        }, 0);
        return list;
    }

    public WindowInfo? Find(string title)
    {
        var hWnd = NativeMethods.FindWindowW(null, title);
        if (hWnd == 0) return null;
        NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
        return new WindowInfo(hWnd, title, (int)pid);
    }

    public WindowInfo? GetForeground()
    {
        var hWnd = NativeMethods.GetForegroundWindow();
        if (hWnd == 0) return null;
        NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
        return new WindowInfo(hWnd, GetTitle(hWnd), (int)pid);
    }

    /// <summary>
    /// 客户区尺寸 + 客户区左上角的屏幕原点。全链路统一使用客户区坐标系,
    /// 取代旧代码里 <c>-40</c> 之类跨坐标系的硬编码修正。
    /// </summary>
    public ClientArea GetClientArea(nint handle)
    {
        NativeMethods.GetClientRect(handle, out var rect);
        var origin = new POINT { X = 0, Y = 0 };
        NativeMethods.ClientToScreen(handle, ref origin);
        return new ClientArea(rect.Right - rect.Left, rect.Bottom - rect.Top, new Point(origin.X, origin.Y));
    }

    /// <summary>
    /// 枚举子窗口(诊断用):部分客户端(如雷电模拟器的 <c>TheRender</c>、某些 NetEase 客户端)把游戏渲染/输入
    /// 放在子窗口里,后台点击需发到子句柄。用来判断当前窗口的输入是否该走某个子窗口。
    /// </summary>
    public IReadOnlyList<ChildWindow> EnumerateChildren(nint parent)
    {
        var list = new List<ChildWindow>();
        NativeMethods.EnumChildWindows(parent, (h, _) =>
        {
            NativeMethods.GetClientRect(h, out var cr);
            list.Add(new ChildWindow(h, GetClassName(h), cr.Right - cr.Left, cr.Bottom - cr.Top,
                NativeMethods.IsWindowVisible(h)));
            return true;
        }, 0);
        return list;
    }

    /// <summary>量取窗口几何与 DPI(诊断后台点击坐标空间)。</summary>
    public GeometryInfo DescribeGeometry(nint handle)
    {
        NativeMethods.GetWindowRect(handle, out var wr);
        NativeMethods.GetClientRect(handle, out var cr);
        var origin = new POINT { X = 0, Y = 0 };
        NativeMethods.ClientToScreen(handle, ref origin);

        var windowRect = new Rect(wr.Left, wr.Top, wr.Right, wr.Bottom);
        var clientW = cr.Right - cr.Left;
        var clientH = cr.Bottom - cr.Top;

        return new GeometryInfo(
            windowRect, clientW, clientH, new Point(origin.X, origin.Y),
            LeftBorder: origin.X - wr.Left,
            TopBar: origin.Y - wr.Top,
            WindowDpi: NativeMethods.GetDpiForWindow(handle),
            SystemDpi: NativeMethods.GetDpiForSystem(),
            WindowAwareness: NativeMethods.GetAwarenessFromDpiAwarenessContext(NativeMethods.GetWindowDpiAwarenessContext(handle)),
            ProcessAwareness: NativeMethods.GetAwarenessFromDpiAwarenessContext(NativeMethods.GetThreadDpiAwarenessContext()));
    }

    public bool IsAlive(nint handle) => handle != 0 && NativeMethods.IsWindow(handle);

    public void SetPriority(int processId, ProcessPriority priority)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_SET_INFORMATION, false, (uint)processId);
        if (handle == 0) return;
        try
        {
            NativeMethods.SetPriorityClass(handle, ToPriorityClass(priority));
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    private static string GetTitle(nint hWnd)
    {
        var length = NativeMethods.GetWindowTextLengthW(hWnd);
        if (length <= 0) return string.Empty;
        var builder = new StringBuilder(length + 1);
        NativeMethods.GetWindowTextW(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetClassName(nint hWnd)
    {
        var builder = new StringBuilder(256);
        var n = NativeMethods.GetClassNameW(hWnd, builder, builder.Capacity);
        return n > 0 ? builder.ToString() : string.Empty;
    }

    private static uint ToPriorityClass(ProcessPriority priority) => priority switch
    {
        ProcessPriority.Idle => NativeMethods.IDLE_PRIORITY_CLASS,
        ProcessPriority.BelowNormal => NativeMethods.BELOW_NORMAL_PRIORITY_CLASS,
        ProcessPriority.Normal => NativeMethods.NORMAL_PRIORITY_CLASS,
        ProcessPriority.AboveNormal => NativeMethods.ABOVE_NORMAL_PRIORITY_CLASS,
        ProcessPriority.High => NativeMethods.HIGH_PRIORITY_CLASS,
        ProcessPriority.Realtime => NativeMethods.REALTIME_PRIORITY_CLASS,
        _ => NativeMethods.NORMAL_PRIORITY_CLASS,
    };
}
