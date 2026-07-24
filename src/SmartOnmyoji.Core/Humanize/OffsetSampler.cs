namespace SmartOnmyoji.Core.Humanize;

/// <summary>
/// 拟人化点击偏移采样器。取代旧 ClickModSet 那套九宫格旋转 + 一堆魔法系数:
/// 在目标点附近按<b>截断正态分布</b>偏移,并整体<b>偏向窗口内侧</b>(目标越靠边、越往中心推),
/// 结果<b>截断在客户区内</b>。参数化、可用统计断言单测。
/// </summary>
public sealed class OffsetSampler
{
    /// <summary>朝内侧偏置强度(相对 deviation 的比例)。</summary>
    private const double BiasStrength = 0.3;

    private readonly Random _random;

    public OffsetSampler(Random? random = null) => _random = random ?? Random.Shared;

    /// <param name="target">期望点击点(客户区坐标)。</param>
    /// <param name="clientWidth">客户区宽。</param>
    /// <param name="clientHeight">客户区高。</param>
    /// <param name="deviation">偏移强度(约等于 2σ 对应的像素幅度)。&lt;=0 时不偏移。</param>
    public Point Sample(Point target, int clientWidth, int clientHeight, int deviation)
    {
        if (deviation <= 0)
            return Clamp(target, clientWidth, clientHeight);

        // 截断正态偏移(σ = deviation/2,截断到 ±2σ)
        var dx = NextTruncatedNormal() * (deviation / 2.0);
        var dy = NextTruncatedNormal() * (deviation / 2.0);

        // 偏向窗口内侧:目标越靠边,均值越往中心偏
        dx += BiasStrength * deviation * ((clientWidth / 2.0 - target.X) / (clientWidth / 2.0));
        dy += BiasStrength * deviation * ((clientHeight / 2.0 - target.Y) / (clientHeight / 2.0));

        return Clamp(
            new Point(target.X + (int)Math.Round(dx), target.Y + (int)Math.Round(dy)),
            clientWidth, clientHeight);
    }

    // Box-Muller 生成标准正态,截断到 ±2σ
    private double NextTruncatedNormal()
    {
        double z;
        do
        {
            var u1 = 1.0 - _random.NextDouble();
            var u2 = 1.0 - _random.NextDouble();
            z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
        while (Math.Abs(z) > 2.0);
        return z;
    }

    private static Point Clamp(Point p, int width, int height) =>
        new(Math.Clamp(p.X, 0, Math.Max(0, width - 1)), Math.Clamp(p.Y, 0, Math.Max(0, height - 1)));
}
