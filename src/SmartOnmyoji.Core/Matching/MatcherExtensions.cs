using SmartOnmyoji.Core.Abstractions;
using SmartOnmyoji.Core.Targets;

namespace SmartOnmyoji.Core.Matching;

public static class MatcherExtensions
{
    /// <summary>
    /// 按优先级顺序匹配,返回第一个命中(短路)。匹配阈值统一用 <paramref name="baseOptions"/>
    /// (运行页「匹配阈值」/ <c>EngineOptions.Match</c>),对所有图一致——不再有每图/默认阈值覆盖
    /// (曾因每图恒有阈值架空运行级设置,已删除该覆盖路径)。每图仍可覆盖匹配器(Hint != Auto)。
    /// 取代旧代码里靠文件名排序 + 早退的隐式优先级。
    /// </summary>
    public static MatchResult? MatchFirst(this IMatcher matcher, CaptureFrame frame, TargetSet set, MatchOptions baseOptions)
    {
        foreach (var image in set.Images)
        {
            var options = image.Hint != MatchHint.Auto
                ? baseOptions with { Method = image.Hint == MatchHint.Template ? MatchMethod.Template : MatchMethod.Feature }
                : baseOptions;

            var result = matcher.Match(frame, image, options);
            if (result is not null) return result;
        }
        return null;
    }
}
