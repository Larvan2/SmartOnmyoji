using System.Collections.Concurrent;
using OpenCvSharp;
using SmartOnmyoji.Core;
using SmartOnmyoji.Core.Abstractions;
using SmartOnmyoji.Core.Matching;
using SmartOnmyoji.Core.Targets;

namespace SmartOnmyoji.Vision;

/// <summary>
/// 模板匹配(TM_CCOEFF_NORMED)。
/// 因为截取的是客户区物理像素、模板也在同分辨率截取,命中中心<b>直接就是可点击的客户区坐标</b>,
/// 无需旧代码那套按屏幕尺寸 / DPI 缩放的坐标换算。
///
/// <para><b>分辨率适配</b>:模板记了截取时的客户区尺寸(<see cref="TargetImage.BaseSize"/>)时,
/// 按当前帧尺寸把模板缩放过去再匹配——同一套模板可跨分辨率复用,不必换分辨率就重截图
/// (见 <see cref="TemplateScale"/>)。没记基准尺寸的旧目标集不缩放,行为不变。</para>
///
/// <para>仅当显式启用压缩(<see cref="MatchOptions.CompressRatio"/> ≠ 1)时,才额外压缩截图;
/// 此时模板的总缩放 = 分辨率适配比 × 压缩比,而<b>命中坐标只需按压缩比还原</b>
/// ——模板缩放改的是模板大小,命中位置始终落在截图坐标系里。</para>
/// </summary>
public sealed class TemplateMatcher : IMatcher, IDisposable
{
    private readonly ConcurrentDictionary<string, Mat> _templateCache = new();

    /// <summary>分辨率适配后的模板,按(原图路径 + 目标像素尺寸)缓存,避免每轮每图重采样。</summary>
    private readonly ConcurrentDictionary<(string Path, int Width, int Height), Mat> _scaledCache = new();

    public MatchResult? Match(CaptureFrame frame, TargetImage target, MatchOptions options)
    {
        using var frameMat = ImageOps.ToGrayMat(frame);
        var template = _templateCache.GetOrAdd(target.FilePath, ImageOps.LoadGray);

        var ratio = options.CompressRatio;
        var compress = ratio is > 0 and not 1.0;
        var frameScale = compress ? ratio : 1.0;

        // 模板要同时跟上「当前分辨率」和「截图压缩」两件事。
        var templateScale = TemplateScale.Resolve(target.BaseSize, frame.Width, frame.Height) * frameScale;

        Mat? frameScaled = null;
        var frameUsed = compress ? frameScaled = ImageOps.Compress(frameMat, ratio) : frameMat;

        try
        {
            var templateUsed = ResolveTemplate(target.FilePath, template, templateScale);
            if (templateUsed is null)
                return null; // 缩放后不足 1 像素,这张图在当前分辨率下无法匹配

            if (templateUsed.Width > frameUsed.Width || templateUsed.Height > frameUsed.Height)
                return null; // 模板比截图大,不可能匹配

            using var result = new Mat();
            Cv2.MatchTemplate(frameUsed, templateUsed, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

            if (maxVal < options.Threshold)
                return null;

            // 命中中心(必要时从压缩空间还原到客户区物理像素)
            var centerX = (maxLoc.X + templateUsed.Width / 2.0) / frameScale;
            var centerY = (maxLoc.Y + templateUsed.Height / 2.0) / frameScale;
            return new MatchResult(target, new SmartOnmyoji.Core.Point((int)centerX, (int)centerY), maxVal);
        }
        finally
        {
            frameScaled?.Dispose();
        }
    }

    /// <summary>取缩放到目标尺寸的模板;尺寸不变时直接复用原图,不进缓存也不重采样。</summary>
    private Mat? ResolveTemplate(string path, Mat original, double scale)
    {
        var size = TemplateScale.ScaledSize(original.Width, original.Height, scale);
        if (size is null) return null;

        var (width, height) = (size.Value.Width, size.Value.Height);
        if (width == original.Width && height == original.Height) return original;

        return _scaledCache.GetOrAdd(
            (path, width, height),
            static (key, src) => ImageOps.ResizeTo(src, key.Width, key.Height),
            original);
    }

    public void Dispose()
    {
        foreach (var mat in _templateCache.Values)
            mat.Dispose();
        _templateCache.Clear();

        foreach (var mat in _scaledCache.Values)
            mat.Dispose();
        _scaledCache.Clear();
    }
}
