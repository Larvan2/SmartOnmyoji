using SmartOnmyoji.Core;
using SmartOnmyoji.Core.Engine;
using SmartOnmyoji.Core.Matching;

namespace SmartOnmyoji.Host.Ui;

// 前后端契约 DTO。句柄一律用十六进制字符串("0x1A2B")往返,避免 nint 在 JSON 里的平台差异。
// Minimal API 默认 camelCase 序列化 + 大小写不敏感反序列化,故 C# 用 PascalCase 即可。

public sealed record WindowDto(string Handle, string Title, int ProcessId, int Width, int Height);

public sealed record TargetSetSummaryDto(string Name, int ImageCount, string Source);

public sealed record TargetImageDto(string Name, string File, int Priority, string Flag, bool HasClick);

public sealed record TargetSetDetailDto(
    string Name,
    string Source,
    IReadOnlyList<TargetImageDto> Images,
    IReadOnlyList<string> Warnings);

public sealed record StartRequest
{
    public IReadOnlyList<string> WindowHandles { get; init; } = Array.Empty<string>();
    public string TargetSet { get; init; } = "";
    public OptionsDto Options { get; init; } = new();
}

public sealed record RandomWaitDto
{
    public bool Enabled { get; init; } = true;
    public double Probability { get; init; } = 0.05;
    public int MinSeconds { get; init; } = 10;
    public int MaxSeconds { get; init; } = 40;
    public int MinGapSeconds { get; init; } = 150;
}

public sealed record FrequencyCapDto
{
    public bool Enabled { get; init; } = true;
    public int WindowMinutes { get; init; } = 10;
    public int MaxRounds { get; init; } = 25;
    public int ExtraWaitSeconds { get; init; } = 5;
}

public sealed record AntiDetectionDto
{
    public bool Enabled { get; init; } = true;
    public int ClickDeviation { get; init; } = 25;
    public RandomWaitDto RandomWait { get; init; } = new();
    public FrequencyCapDto FrequencyCap { get; init; } = new();
    public bool EnableRepeatStop { get; init; } = true;
    public int RepeatSameTargetStop { get; init; } = 5;
}

public sealed record OptionsDto
{
    public string Mode { get; init; } = "ByRounds";        // ByRounds | ByMinutes
    public int Rounds { get; init; } = 100;
    public double DurationMinutes { get; init; } = 100;
    public double IntervalMin { get; init; } = 2.0;
    public double IntervalMax { get; init; } = 4.0;
    public double MatchThreshold { get; init; } = 0.80;
    public string? Priority { get; init; }                 // null = 不改进程优先级
    public AntiDetectionDto AntiDetection { get; init; } = new();
}

public sealed record StatusDto(
    bool Running,
    string? TargetSet,
    int WindowCount,
    int Round,
    int Progress,
    DateTimeOffset? StartedAt);

/// <summary>DTO ↔ 领域类型映射。UI 只见 DTO,引擎只见 <see cref="EngineOptions"/>。</summary>
public static class OptionsMapping
{
    public static OptionsDto ToDto(EngineOptions o) => new()
    {
        Mode = o.Mode.ToString(),
        Rounds = o.Rounds,
        DurationMinutes = o.Duration.TotalMinutes,
        IntervalMin = o.Interval.MinSeconds,
        IntervalMax = o.Interval.MaxSeconds,
        MatchThreshold = o.Match.Threshold,
        Priority = o.SetPriority?.ToString(),
        AntiDetection = new AntiDetectionDto
        {
            Enabled = o.AntiDetection.Enabled,
            ClickDeviation = o.AntiDetection.ClickDeviation,
            RandomWait = new RandomWaitDto
            {
                Enabled = o.AntiDetection.RandomWait.Enabled,
                Probability = o.AntiDetection.RandomWait.Probability,
                MinSeconds = o.AntiDetection.RandomWait.MinSeconds,
                MaxSeconds = o.AntiDetection.RandomWait.MaxSeconds,
                MinGapSeconds = o.AntiDetection.RandomWait.MinGapSeconds,
            },
            FrequencyCap = new FrequencyCapDto
            {
                Enabled = o.AntiDetection.FrequencyCap.Enabled,
                WindowMinutes = o.AntiDetection.FrequencyCap.WindowMinutes,
                MaxRounds = o.AntiDetection.FrequencyCap.MaxRounds,
                ExtraWaitSeconds = o.AntiDetection.FrequencyCap.ExtraWaitSeconds,
            },
            EnableRepeatStop = o.AntiDetection.EnableRepeatStop,
            RepeatSameTargetStop = o.AntiDetection.RepeatSameTargetStop,
        },
    };

    public static EngineOptions ToOptions(OptionsDto d)
    {
        var mode = string.Equals(d.Mode, "ByMinutes", StringComparison.OrdinalIgnoreCase)
            ? RunMode.ByMinutes
            : RunMode.ByRounds;

        ProcessPriority? priority = Enum.TryParse<ProcessPriority>(d.Priority, ignoreCase: true, out var p)
            ? p
            : null;

        return new EngineOptions
        {
            Mode = mode,
            Rounds = d.Rounds,
            Duration = TimeSpan.FromMinutes(d.DurationMinutes),
            Interval = new IntervalRange(d.IntervalMin, d.IntervalMax),
            Match = new MatchOptions(MatchMethod.Template, d.MatchThreshold, 1.0),
            SetPriority = priority,
            AntiDetection = new AntiDetectionOptions
            {
                Enabled = d.AntiDetection.Enabled,
                ClickDeviation = d.AntiDetection.ClickDeviation,
                RandomWait = new RandomWaitOptions
                {
                    Enabled = d.AntiDetection.RandomWait.Enabled,
                    Probability = d.AntiDetection.RandomWait.Probability,
                    MinSeconds = d.AntiDetection.RandomWait.MinSeconds,
                    MaxSeconds = d.AntiDetection.RandomWait.MaxSeconds,
                    MinGapSeconds = d.AntiDetection.RandomWait.MinGapSeconds,
                },
                FrequencyCap = new FrequencyCapOptions
                {
                    Enabled = d.AntiDetection.FrequencyCap.Enabled,
                    WindowMinutes = d.AntiDetection.FrequencyCap.WindowMinutes,
                    MaxRounds = d.AntiDetection.FrequencyCap.MaxRounds,
                    ExtraWaitSeconds = d.AntiDetection.FrequencyCap.ExtraWaitSeconds,
                },
                EnableRepeatStop = d.AntiDetection.EnableRepeatStop,
                RepeatSameTargetStop = d.AntiDetection.RepeatSameTargetStop,
            },
        };
    }
}
