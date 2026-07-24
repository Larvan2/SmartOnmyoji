using System.Runtime.InteropServices;
using OpenCvSharp;
using SmartOnmyoji.Core;

namespace SmartOnmyoji.Vision;

internal static class ImageOps
{
    /// <summary>灰度 <see cref="CaptureFrame"/> → CV_8UC1 Mat(整块拷贝,避免托管数组生命周期问题)。</summary>
    public static Mat ToGrayMat(CaptureFrame frame)
    {
        var mat = new Mat(frame.Height, frame.Width, MatType.CV_8UC1);
        Marshal.Copy(frame.Gray, 0, mat.Data, frame.Gray.Length);
        return mat;
    }

    /// <summary>读取模板为灰度 Mat。用 ImDecode 处理中文路径(对齐旧 cv2.imdecode(np.fromfile))。</summary>
    public static Mat LoadGray(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var mat = Cv2.ImDecode(bytes, ImreadModes.Grayscale);
        if (mat.Empty())
            throw new InvalidOperationException($"无法解码图片:{path}");
        return mat;
    }

    /// <summary>等比压缩(仅在启用压缩时使用)。</summary>
    public static Mat Compress(Mat src, double ratio)
    {
        var dst = new Mat();
        Cv2.Resize(src, dst, new OpenCvSharp.Size((int)(src.Width * ratio), (int)(src.Height * ratio)), 0, 0, InterpolationFlags.Area);
        return dst;
    }
}
