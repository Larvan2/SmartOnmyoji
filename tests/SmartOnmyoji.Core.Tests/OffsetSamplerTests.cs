using SmartOnmyoji.Core;
using SmartOnmyoji.Core.Humanize;
using Xunit;

namespace SmartOnmyoji.Core.Tests;

public class OffsetSamplerTests
{
    [Fact]
    public void Deviation_zero_returns_target()
    {
        var sampler = new OffsetSampler(new Random(1));
        Assert.Equal(new Point(100, 100), sampler.Sample(new Point(100, 100), 1200, 600, 0));
    }

    [Fact]
    public void Samples_stay_within_client_bounds()
    {
        var sampler = new OffsetSampler(new Random(2));
        for (var i = 0; i < 3000; i++)
        {
            var p = sampler.Sample(new Point(0, 0), 1200, 600, 60);
            Assert.InRange(p.X, 0, 1199);
            Assert.InRange(p.Y, 0, 599);
        }
    }

    [Fact]
    public void Center_target_offset_mean_is_near_zero()
    {
        var sampler = new OffsetSampler(new Random(4));
        const int n = 5000;
        long sumX = 0;
        for (var i = 0; i < n; i++)
            sumX += sampler.Sample(new Point(600, 300), 1200, 600, 40).X - 600;
        Assert.True(Math.Abs(sumX / (double)n) < 5, "中心目标的偏移均值应≈0");
    }

    [Fact]
    public void Edge_target_is_biased_toward_interior()
    {
        var sampler = new OffsetSampler(new Random(3));
        const int n = 5000;
        long sumX = 0;
        for (var i = 0; i < n; i++)
            sumX += sampler.Sample(new Point(10, 300), 1200, 600, 40).X;
        var meanX = sumX / (double)n;
        Assert.True(meanX > 10, $"靠左边缘目标的平均落点 {meanX:0.0} 应 > 10(偏向内侧)");
    }
}
