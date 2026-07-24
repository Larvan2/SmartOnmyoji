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
/// 仅当显式启用压缩(<see cref="MatchOptions.CompressRatio"/> ≠ 1)时,才在匹配器内部做一次等比还原。
/// </summary>
public sealed class TemplateMatcher : IMatcher, IDisposable
{
    private readonly ConcurrentDictionary<string, Mat> _templateCache = new();

    public MatchResult? Match(CaptureFrame frame, TargetImage target, MatchOptions options)
    {
        using var frameMat = ImageOps.ToGrayMat(frame);
        var template = _templateCache.GetOrAdd(target.FilePath, ImageOps.LoadGray);

        var ratio = options.CompressRatio;
        var compress = ratio is > 0 and not 1.0;

        Mat frameUsed = frameMat, templateUsed = template;
        Mat? frameScaled = null, templateScaled = null;
        if (compress)
        {
            frameUsed = frameScaled = ImageOps.Compress(frameMat, ratio);
            templateUsed = templateScaled = ImageOps.Compress(template, ratio);
        }

        try
        {
            if (templateUsed.Width > frameUsed.Width || templateUsed.Height > frameUsed.Height)
                return null; // 模板比截图大,不可能匹配

            using var result = new Mat();
            Cv2.MatchTemplate(frameUsed, templateUsed, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

            if (maxVal < options.Threshold)
                return null;

            // 命中中心(必要时从压缩空间还原到客户区物理像素)
            var scale = compress ? ratio : 1.0;
            var centerX = (maxLoc.X + templateUsed.Width / 2.0) / scale;
            var centerY = (maxLoc.Y + templateUsed.Height / 2.0) / scale;
            return new MatchResult(target, new SmartOnmyoji.Core.Point((int)centerX, (int)centerY), maxVal);
        }
        finally
        {
            frameScaled?.Dispose();
            templateScaled?.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var mat in _templateCache.Values)
            mat.Dispose();
        _templateCache.Clear();
    }
}
