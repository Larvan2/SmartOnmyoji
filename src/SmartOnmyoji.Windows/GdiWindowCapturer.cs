using SmartOnmyoji.Core;
using SmartOnmyoji.Core.Abstractions;

namespace SmartOnmyoji.Windows;

/// <summary>
/// GDI 后台窗口截图:PrintWindow(可被遮挡)优先,失败回退 BitBlt。
/// 捕获<b>客户区</b>,使匹配坐标即为可直接用于后台点击的客户区坐标。
/// </summary>
public sealed class GdiWindowCapturer : IScreenCapturer
{
    public CaptureFrame Capture(nint handle)
    {
        var (width, height, bgra) = CaptureBgra(handle);
        return new CaptureFrame(width, height, ToGray(width, height, bgra));
    }

    /// <summary>捕获客户区 BGRA(top-down,行主序)。供调试存图/彩色核对使用。</summary>
    public (int Width, int Height, byte[] Bgra) CaptureBgra(nint handle)
    {
        if (!NativeMethods.IsWindow(handle))
            throw new ArgumentException("窗口句柄无效或窗口已关闭。", nameof(handle));

        NativeMethods.GetClientRect(handle, out var rect);
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"客户区尺寸异常:{width}x{height}(窗口可能已最小化)。");

        var windowDc = NativeMethods.GetDC(handle); // 客户区 DC
        var memDc = NativeMethods.CreateCompatibleDC(windowDc);
        var bitmap = NativeMethods.CreateCompatibleBitmap(windowDc, width, height);
        var oldBitmap = NativeMethods.SelectObject(memDc, bitmap);
        try
        {
            var printed = NativeMethods.PrintWindow(
                handle, memDc, NativeMethods.PW_RENDERFULLCONTENT | NativeMethods.PW_CLIENTONLY);
            if (!printed)
                NativeMethods.BitBlt(memDc, 0, 0, width, height, windowDc, 0, 0, NativeMethods.SRCCOPY);

            var buffer = new byte[width * height * 4];
            NativeMethods.GetBitmapBits(bitmap, buffer.Length, buffer);
            return (width, height, buffer);
        }
        finally
        {
            NativeMethods.SelectObject(memDc, oldBitmap);
            NativeMethods.DeleteObject(bitmap);
            NativeMethods.DeleteDC(memDc);
            NativeMethods.ReleaseDC(handle, windowDc);
        }
    }

    private static byte[] ToGray(int width, int height, byte[] bgra)
    {
        var gray = new byte[width * height];
        for (int i = 0, j = 0; i < gray.Length; i++, j += 4)
        {
            // BGRA: [j]=B [j+1]=G [j+2]=R ; Y = 0.299R + 0.587G + 0.114B
            gray[i] = (byte)((bgra[j + 2] * 299 + bgra[j + 1] * 587 + bgra[j] * 114) / 1000);
        }
        return gray;
    }
}
