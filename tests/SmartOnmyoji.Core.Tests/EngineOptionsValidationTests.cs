using SmartOnmyoji.Core.Config;
using SmartOnmyoji.Core.Engine;
using SmartOnmyoji.Core.Matching;
using Xunit;

namespace SmartOnmyoji.Core.Tests;

public class EngineOptionsValidationTests
{
    [Fact]
    public void Default_options_are_valid()
    {
        Assert.Empty(EngineOptionsValidation.Validate(new EngineOptions()));
    }

    [Fact]
    public void ByRounds_requires_positive_rounds()
    {
        var opts = new EngineOptions { Mode = RunMode.ByRounds, Rounds = 0 };
        Assert.Contains(EngineOptionsValidation.Validate(opts), e => e.Contains("Rounds"));
    }

    [Fact]
    public void ByMinutes_requires_positive_duration()
    {
        var opts = new EngineOptions { Mode = RunMode.ByMinutes, Duration = TimeSpan.Zero };
        Assert.Contains(EngineOptionsValidation.Validate(opts), e => e.Contains("Duration"));
    }

    [Fact]
    public void Interval_max_below_min_is_invalid()
    {
        var opts = new EngineOptions { Interval = new IntervalRange(5, 2) };
        Assert.Contains(EngineOptionsValidation.Validate(opts), e => e.Contains("Interval.MaxSeconds"));
    }

    [Fact]
    public void Negative_interval_min_is_invalid()
    {
        var opts = new EngineOptions { Interval = new IntervalRange(-1, 2) };
        Assert.Contains(EngineOptionsValidation.Validate(opts), e => e.Contains("Interval.MinSeconds"));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.5)]
    public void Threshold_out_of_range_is_invalid(double threshold)
    {
        var opts = new EngineOptions { Match = new MatchOptions(MatchMethod.Template, threshold, 1.0) };
        Assert.Contains(EngineOptionsValidation.Validate(opts), e => e.Contains("Threshold"));
    }

    [Fact]
    public void CompressRatio_out_of_range_is_invalid()
    {
        var opts = new EngineOptions { Match = new MatchOptions(MatchMethod.Template, 0.8, 0) };
        Assert.Contains(EngineOptionsValidation.Validate(opts), e => e.Contains("CompressRatio"));
    }

    [Fact]
    public void Decoy_probability_out_of_range_is_invalid()
    {
        var opts = new EngineOptions { AntiDetection = new AntiDetectionOptions { DecoyClickProbability = 2 } };
        Assert.Contains(EngineOptionsValidation.Validate(opts), e => e.Contains("DecoyClickProbability"));
    }

    [Fact]
    public void RandomWait_max_below_min_is_invalid()
    {
        var opts = new EngineOptions
        {
            AntiDetection = new AntiDetectionOptions { RandomWait = new RandomWaitOptions { MinSeconds = 40, MaxSeconds = 10 } },
        };
        Assert.Contains(EngineOptionsValidation.Validate(opts), e => e.Contains("RandomWait.MaxSeconds"));
    }

    [Fact]
    public void FrequencyCap_nonpositive_window_is_invalid()
    {
        var opts = new EngineOptions
        {
            AntiDetection = new AntiDetectionOptions { FrequencyCap = new FrequencyCapOptions { WindowMinutes = 0 } },
        };
        Assert.Contains(EngineOptionsValidation.Validate(opts), e => e.Contains("FrequencyCap.WindowMinutes"));
    }

    [Fact]
    public void RepeatSameTargetStop_below_one_is_invalid()
    {
        var opts = new EngineOptions { AntiDetection = new AntiDetectionOptions { RepeatSameTargetStop = 0 } };
        Assert.Contains(EngineOptionsValidation.Validate(opts), e => e.Contains("RepeatSameTargetStop"));
    }

    [Fact]
    public void ValidateAndThrow_throws_with_all_errors()
    {
        var opts = new EngineOptions
        {
            Rounds = -1,
            Interval = new IntervalRange(5, 1),
        };
        var ex = Assert.Throws<OptionsValidationException>(() => EngineOptionsValidation.ValidateAndThrow(opts));
        Assert.True(ex.Errors.Count >= 2);
    }

    [Fact]
    public void ValidateAndThrow_passes_for_valid_options()
    {
        EngineOptionsValidation.ValidateAndThrow(new EngineOptions());  // 不抛
    }
}
