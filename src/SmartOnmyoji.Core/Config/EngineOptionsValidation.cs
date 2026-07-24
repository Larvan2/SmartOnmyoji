using SmartOnmyoji.Core.Engine;

namespace SmartOnmyoji.Core.Config;

/// <summary>
/// <see cref="EngineOptions"/> 的类型化校验(对齐设计文档 §2.2「Options 校验」、§9.1)。
/// 纯逻辑、无平台依赖:返回人类可读的错误列表,空列表即合法。
/// Host 的组合根可在启动时调 <see cref="ValidateAndThrow"/>;P7 接 <c>IValidateOptions</c> 时也复用 <see cref="Validate"/>。
/// </summary>
public static class EngineOptionsValidation
{
    public static IReadOnlyList<string> Validate(EngineOptions options)
    {
        var errors = new List<string>();

        // 运行模式
        if (options.Mode == RunMode.ByRounds && options.Rounds <= 0)
            errors.Add($"Rounds 必须 > 0(当前 {options.Rounds})。");
        if (options.Mode == RunMode.ByMinutes && options.Duration <= TimeSpan.Zero)
            errors.Add($"Duration 必须 > 0(当前 {options.Duration})。");

        // 匹配间隔
        var interval = options.Interval;
        if (interval.MinSeconds < 0)
            errors.Add($"Interval.MinSeconds 不能为负(当前 {interval.MinSeconds})。");
        if (interval.MaxSeconds < interval.MinSeconds)
            errors.Add($"Interval.MaxSeconds({interval.MaxSeconds})不能小于 MinSeconds({interval.MinSeconds})。");

        // 匹配选项
        var match = options.Match;
        if (match.Threshold is <= 0 or > 1)
            errors.Add($"Match.Threshold 须在 (0,1](当前 {match.Threshold})。");
        if (match.CompressRatio is <= 0 or > 1)
            errors.Add($"Match.CompressRatio 须在 (0,1](当前 {match.CompressRatio})。");

        errors.AddRange(ValidateAntiDetection(options.AntiDetection));
        return errors;
    }

    private static IEnumerable<string> ValidateAntiDetection(AntiDetectionOptions anti)
    {
        if (anti.ClickDeviation < 0)
            yield return $"AntiDetection.ClickDeviation 不能为负(当前 {anti.ClickDeviation})。";
        if (anti.DecoyClickProbability is < 0 or > 1)
            yield return $"AntiDetection.DecoyClickProbability 须在 [0,1](当前 {anti.DecoyClickProbability})。";
        if (anti.RepeatSameTargetStop < 1)
            yield return $"AntiDetection.RepeatSameTargetStop 须 ≥ 1(当前 {anti.RepeatSameTargetStop})。";

        var rw = anti.RandomWait;
        if (rw.Probability is < 0 or > 1)
            yield return $"RandomWait.Probability 须在 [0,1](当前 {rw.Probability})。";
        if (rw.MinSeconds < 0)
            yield return $"RandomWait.MinSeconds 不能为负(当前 {rw.MinSeconds})。";
        if (rw.MaxSeconds < rw.MinSeconds)
            yield return $"RandomWait.MaxSeconds({rw.MaxSeconds})不能小于 MinSeconds({rw.MinSeconds})。";
        if (rw.MinGapSeconds < 0)
            yield return $"RandomWait.MinGapSeconds 不能为负(当前 {rw.MinGapSeconds})。";

        var fc = anti.FrequencyCap;
        if (fc.WindowMinutes <= 0)
            yield return $"FrequencyCap.WindowMinutes 须 > 0(当前 {fc.WindowMinutes})。";
        if (fc.MaxRounds <= 0)
            yield return $"FrequencyCap.MaxRounds 须 > 0(当前 {fc.MaxRounds})。";
        if (fc.ExtraWaitSeconds < 0)
            yield return $"FrequencyCap.ExtraWaitSeconds 不能为负(当前 {fc.ExtraWaitSeconds})。";
    }

    /// <summary>校验失败则抛 <see cref="OptionsValidationException"/>(错误汇总)。</summary>
    public static void ValidateAndThrow(EngineOptions options)
    {
        var errors = Validate(options);
        if (errors.Count > 0)
            throw new OptionsValidationException(errors);
    }
}

/// <summary>EngineOptions 校验失败时抛出,携带全部错误项。</summary>
public sealed class OptionsValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public OptionsValidationException(IReadOnlyList<string> errors)
        : base("EngineOptions 校验失败:\n  - " + string.Join("\n  - ", errors))
    {
        Errors = errors;
    }
}
