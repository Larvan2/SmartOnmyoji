using SmartOnmyoji.Core.Matching;
using Xunit;

namespace SmartOnmyoji.Core.Tests;

/// <summary>
/// 模板分辨率适配的缩放比解析。这是"模板在 A 分辨率截的、想在 B 分辨率跑"的核心裁决,
/// 每个分支都要能在离线下讲清楚——尤其是各种"拿不准就别缩放"的兜底。
/// </summary>
public class TemplateScaleTests
{
    [Fact]
    public void Missing_base_size_never_scales()
    {
        // 旧目标集/导入数据没记基准尺寸 → 行为必须与加入本功能前完全一致。
        Assert.Equal(1.0, TemplateScale.Resolve(null, 1200, 600));
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(1200, 0)]
    [InlineData(-1200, -600)]
    public void Non_positive_base_size_never_scales(int width, int height)
    {
        Assert.Equal(1.0, TemplateScale.Resolve(new Size(width, height), 1200, 600));
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(1200, 0)]
    public void Non_positive_frame_never_scales(int frameWidth, int frameHeight)
    {
        Assert.Equal(1.0, TemplateScale.Resolve(new Size(1200, 600), frameWidth, frameHeight));
    }

    [Fact]
    public void Same_resolution_returns_one()
    {
        Assert.Equal(1.0, TemplateScale.Resolve(new Size(1200, 600), 1200, 600));
    }

    [Fact]
    public void Proportional_enlargement_scales_up()
    {
        // 1200×600 截的模板拿到 1600×800 上跑 → 放大 4/3
        Assert.Equal(4.0 / 3.0, TemplateScale.Resolve(new Size(1200, 600), 1600, 800), 6);
    }

    [Fact]
    public void Proportional_reduction_scales_down()
    {
        Assert.Equal(0.5, TemplateScale.Resolve(new Size(1200, 600), 600, 300), 6);
    }

    [Fact]
    public void Near_identical_ratio_snaps_to_one()
    {
        // 差几个像素(边框取整之类)不值得重采样一遍模板。
        Assert.Equal(1.0, TemplateScale.Resolve(new Size(1200, 600), 1202, 601));
    }

    [Fact]
    public void Non_proportional_stretch_takes_smaller_ratio()
    {
        // 宽 ×0.5、高 ×0.75:窗口被拉伸或游戏加了黑边。
        // 取较小比例——UI 内容一般按较短边等比适配,按大的放必然错过。
        Assert.Equal(0.5, TemplateScale.Resolve(new Size(1200, 600), 600, 450), 6);
    }

    [Theory]
    [InlineData(100, 50)]     // ×0.083,低于 MinScale
    [InlineData(12000, 6000)] // ×10,高于 MaxScale
    public void Absurd_ratio_falls_back_to_no_scaling(int frameWidth, int frameHeight)
    {
        // 基准尺寸明显不可信(填错/换了游戏)时,宁可不缩放也不要拿一张糊到没法看的模板去匹配。
        Assert.Equal(1.0, TemplateScale.Resolve(new Size(1200, 600), frameWidth, frameHeight));
    }

    [Fact]
    public void ScaledSize_rounds_to_nearest_pixel()
    {
        var size = TemplateScale.ScaledSize(61, 31, 0.5);
        Assert.Equal(new Size(31, 16), size);   // 30.5→31, 15.5→16
    }

    [Fact]
    public void ScaledSize_supports_enlargement()
    {
        Assert.Equal(new Size(120, 60), TemplateScale.ScaledSize(60, 30, 2.0));
    }

    [Theory]
    [InlineData(3, 3, 0.1)]    // 缩到 0×0
    [InlineData(0, 10, 1.0)]   // 空模板
    [InlineData(10, 10, 0)]    // 非法缩放比
    [InlineData(10, 10, -1)]
    public void ScaledSize_returns_null_when_unusable(int width, int height, double scale)
    {
        Assert.Null(TemplateScale.ScaledSize(width, height, scale));
    }
}
