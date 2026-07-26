namespace SmartOnmyoji.Core.Matching;

/// <summary>
/// 模板的<b>分辨率适配缩放</b>:模板是在某个客户区尺寸下截的(<see cref="Targets.TargetImage.BaseSize"/>),
/// 运行时客户区可能是另一尺寸,按比例把模板缩放过去再匹配,同一套模板即可跨分辨率复用,
/// 不必换个分辨率就重截一遍图。
///
/// <para>这是<b>模板匹配路径</b>上对"抗缩放"的解法——比旧版 SIFT 特征匹配便宜得多也稳得多:
/// UI 图标是低纹理、边缘锐利的合成图像,恰是 SIFT 的弱项(特征点少且不稳定);
/// 而缩放后的模板匹配仍是一次 <c>matchTemplate</c>,分数语义也保持不变。</para>
///
/// <para>纯函数,可离线单测;实际重采样由 Vision 层做。</para>
/// </summary>
public static class TemplateScale
{
    /// <summary>缩放比与 1.0 的差在此以内即视为同分辨率,不重采样(省掉无意义的精度抖动)。</summary>
    public const double Epsilon = 0.005;

    /// <summary>宽/高两方向的比例相对差超过此值即视为非等比拉伸,退回保守策略。</summary>
    public const double AspectTolerance = 0.05;

    /// <summary>缩放比的合理区间;超出即认为基准尺寸不可信,放弃缩放(返回 1.0)。</summary>
    public const double MinScale = 0.2;
    public const double MaxScale = 5.0;

    /// <summary>
    /// 求模板到当前帧的缩放比。<paramref name="baseSize"/> 为 null / 非正 / 比例离谱时返回 1.0(不缩放),
    /// 保证旧目标集(没记基准尺寸)的行为与加入本功能前完全一致。
    /// </summary>
    public static double Resolve(Size? baseSize, int frameWidth, int frameHeight)
    {
        if (baseSize is not { Width: > 0, Height: > 0 }) return 1.0;
        if (frameWidth <= 0 || frameHeight <= 0) return 1.0;

        var sx = (double)frameWidth / baseSize.Value.Width;
        var sy = (double)frameHeight / baseSize.Value.Height;

        // 游戏窗口一般等比变化,两方向比例几乎相同 → 取均值抹掉取整噪声。
        // 若明显不等比(窗口被拉伸、或游戏保持内容比例加了黑边),取较小比例:
        // UI 内容通常按较短边等比适配,按较大比例放大必然错过。
        var scale = RelativeGap(sx, sy) > AspectTolerance
            ? Math.Min(sx, sy)
            : (sx + sy) / 2;

        if (scale is < MinScale or > MaxScale) return 1.0;
        return Math.Abs(scale - 1.0) <= Epsilon ? 1.0 : scale;
    }

    /// <summary>
    /// 按缩放比算模板的目标像素尺寸。缩放后任一边不足 1 像素则返回 null,
    /// 表示这张模板在当前分辨率下已无法匹配(调用方当作未命中)。
    /// </summary>
    public static Size? ScaledSize(int width, int height, double scale)
    {
        if (width <= 0 || height <= 0 || scale <= 0) return null;

        // AwayFromZero 而非默认的银行家舍入:同一个 .5 在宽高上得到一致结果,尺寸可预测、好解释。
        var w = (int)Math.Round(width * scale, MidpointRounding.AwayFromZero);
        var h = (int)Math.Round(height * scale, MidpointRounding.AwayFromZero);
        return w < 1 || h < 1 ? null : new Size(w, h);
    }

    private static double RelativeGap(double a, double b) => Math.Abs(a - b) / Math.Max(a, b);
}
