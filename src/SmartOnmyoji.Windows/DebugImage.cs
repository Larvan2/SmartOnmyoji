using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SmartOnmyoji.Windows;

public static class DebugImage
{
    /// <summary>把客户区 BGRA(top-down)缓冲存为 PNG,用于人工核对截图内容与朝向。</summary>
    public static void SavePng(string path, int width, int height, byte[] bgra)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            // 32bpp 无行填充(stride == width*4),BGRA 缓冲可整块拷贝
            Marshal.Copy(bgra, 0, data.Scan0, bgra.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        bitmap.Save(path, ImageFormat.Png);
    }

    /// <summary>把客户区 BGRA(top-down)缓冲编码成 PNG 字节(供 WebUI 内嵌/取模板),不落盘。</summary>
    public static byte[] EncodePng(int width, int height, byte[] bgra)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            Marshal.Copy(bgra, 0, data.Scan0, bgra.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>把行主序灰度缓冲编码成 8bpp 灰阶 PNG 字节(供匹配缩略图推送)。逐行拷贝以处理 stride 填充。</summary>
    public static byte[] EncodeGrayPng(int width, int height, byte[] gray)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
        var palette = bitmap.Palette;
        for (var i = 0; i < 256; i++) palette.Entries[i] = Color.FromArgb(i, i, i);
        bitmap.Palette = palette;

        var data = bitmap.LockBits(
            new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
        try
        {
            for (var row = 0; row < height; row++)
                Marshal.Copy(gray, row * width, data.Scan0 + row * data.Stride, width);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
