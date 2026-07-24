using SmartOnmyoji.Core.Matching;

namespace SmartOnmyoji.Core.Engine;

public enum RunMode { ByMinutes, ByRounds }

public sealed record IntervalRange(double MinSeconds, double MaxSeconds);

/// <summary>
/// 层1:概率随机等待——模拟走神、打破节奏规律。每"开局"(RoundStart)按 <see cref="Probability"/>
/// 掷骰子,命中则等 [<see cref="MinSeconds"/>,<see cref="MaxSeconds"/>] 内随机秒数;
/// <see cref="MinGapSeconds"/> 保证两次随机等待不至于挨太近(对齐旧版 150s 保护)。
/// </summary>
public sealed record RandomWaitOptions
{
    public bool Enabled { get; init; } = true;
    public double Probability { get; init; } = 0.05;
    public int MinSeconds { get; init; } = 10;
    public int MaxSeconds { get; init; } = 40;
    public int MinGapSeconds { get; init; } = 150;
}

/// <summary>
/// 层2:时间窗口频次上限——总量安全阀。滑动 <see cref="WindowMinutes"/> 分钟窗口内"开局"数
/// 超过 <see cref="MaxRounds"/> 后,之后每次开局强制额外等 <see cref="ExtraWaitSeconds"/> 秒。
/// 注:计数单位是"局"(RoundStart),不是旧版的"任意匹配成功次数",故默认阈值相应下调。
/// </summary>
public sealed record FrequencyCapOptions
{
    public bool Enabled { get; init; } = true;
    public int WindowMinutes { get; init; } = 10;
    public int MaxRounds { get; init; } = 25;
    public int ExtraWaitSeconds { get; init; } = 5;
}

public sealed record AntiDetectionOptions
{
    /// <summary>防检测总开关。关闭时 <see cref="RandomWait"/>/<see cref="FrequencyCap"/> 一律不触发。</summary>
    public bool Enabled { get; init; } = true;

    public int ClickDeviation { get; init; } = 25;

    /// <summary>诱饵点击(在别处多点一下混淆热区)的概率。默认 0 = 关闭;为可选功能,当前未实现。</summary>
    public double DecoyClickProbability { get; init; }
    public RandomWaitOptions RandomWait { get; init; } = new();
    public FrequencyCapOptions FrequencyCap { get; init; } = new();
    public bool PlaytimeWarning { get; init; } = true;
    public bool EnableRepeatStop { get; init; } = true;
    public int RepeatSameTargetStop { get; init; } = 5;
}

public sealed record EngineOptions
{
    public RunMode Mode { get; init; } = RunMode.ByRounds;
    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes(100);
    public int Rounds { get; init; } = 100;
    public IntervalRange Interval { get; init; } = new(2.0, 4.0);
    public MatchOptions Match { get; init; } = new(MatchMethod.Template, 0.80, 1.0);
    public ProcessPriority? SetPriority { get; init; } = ProcessPriority.High;
    public AntiDetectionOptions AntiDetection { get; init; } = new();
}
