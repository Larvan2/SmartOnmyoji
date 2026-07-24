namespace SmartOnmyoji.Windows;

public static class DpiAwareness
{
    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
    private static readonly nint PerMonitorAwareV2 = -4;

    /// <summary>
    /// 声明进程为 Per-Monitor-V2 DPI 感知,确保按物理像素截图与点击。
    /// 取代旧代码里散落且被注释关掉的一堆 DPI 处理。应在任何窗口/截图操作前调用一次。
    /// </summary>
    public static void EnablePerMonitorV2()
    {
        try
        {
            NativeMethods.SetProcessDpiAwarenessContext(PerMonitorAwareV2);
        }
        catch
        {
            // 老系统不支持该 API 时忽略,回退系统默认行为。
        }
    }
}
