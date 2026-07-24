using SmartOnmyoji.Core;
using SmartOnmyoji.Core.Abstractions;
using SmartOnmyoji.Core.Matching;
using SmartOnmyoji.Core.Targets;
using Xunit;

namespace SmartOnmyoji.Core.Tests;

/// <summary>
/// <see cref="MatcherExtensions.MatchFirst"/>:匹配阈值统一用运行级(UI/<c>EngineOptions.Match</c>),
/// 对所有图一致,不存在每图/默认阈值覆盖。锁死「运行页匹配阈值被 target.json defaults 架空」的回归
/// (根因:曾经每图恒有阈值、覆盖了运行级 base——已彻底删除每图阈值这条路径,阈值只有此唯一来源)。
/// </summary>
public class MatcherExtensionsTests
{
    /// <summary>记录每张图匹配时实际收到的阈值/方法;从不命中 → 遍历全部图。</summary>
    private sealed class RecordingMatcher : IMatcher
    {
        public List<(string Name, double Threshold, MatchMethod Method)> Seen { get; } = new();
        public MatchResult? Match(CaptureFrame frame, TargetImage target, MatchOptions options)
        {
            Seen.Add((target.Name, options.Threshold, options.Method));
            return null;
        }
    }

    private static TargetImage Img(string name, MatchHint hint = MatchHint.Auto) => new()
    {
        Name = name,
        FilePath = $"(fake)/{name}.png",
        Flag = TargetFlag.Normal,
        Priority = 0,
        Hint = hint,
    };

    private static readonly CaptureFrame Frame = new(10, 10, new byte[100]);

    [Fact]
    public void All_images_use_the_single_runtime_threshold()
    {
        var set = new TargetSet("t", new[] { Img("a"), Img("b") });
        var matcher = new RecordingMatcher();

        matcher.MatchFirst(Frame, set, new MatchOptions(MatchMethod.Template, 0.9, 1.0));

        // 运行级 0.9 对每张图一致生效(修复前会被每图/defaults 恒有的阈值 0.80 架空)
        Assert.Equal(2, matcher.Seen.Count);
        Assert.All(matcher.Seen, s => Assert.Equal(0.9, s.Threshold));
    }

    [Fact]
    public void Image_hint_overrides_method_but_threshold_stays_runtime()
    {
        var set = new TargetSet("t", new[] { Img("feat", MatchHint.Feature) });
        var matcher = new RecordingMatcher();

        matcher.MatchFirst(Frame, set, new MatchOptions(MatchMethod.Template, 0.85, 1.0));

        var seen = matcher.Seen.Single();
        Assert.Equal(MatchMethod.Feature, seen.Method); // 每图仍可覆盖匹配器
        Assert.Equal(0.85, seen.Threshold);              // 阈值不受影响,仍是运行级
    }
}
