using SmartOnmyoji.Core.Targets;

namespace SmartOnmyoji.Core.Matching;

public enum MatchMethod { Template, Feature, Auto }

/// <summary>一次匹配的选项。<see cref="CompressRatio"/> 为 1.0 表示不压缩。</summary>
public sealed record MatchOptions(MatchMethod Method, double Threshold, double CompressRatio);

/// <summary>命中结果:命中的目标图、客户区坐标系下的中心点、匹配分数。</summary>
public sealed record MatchResult(TargetImage Target, Point Center, double Score);
