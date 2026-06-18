using AimDecider.Calibration;
using AimDecider.GameProfiles;
using AimDecider.Trials;

namespace AimDecider.Tests;

public class SessionSummaryBuilderTests
{
    [Fact]
    public void Build_ComputesRecommendationFromMedianCountsPerDegree()
    {
        // Three horizontal trials with CountsPerDegree of 100, 200, 300 (median 200).
        var results = new[]
        {
            new TrialResult(Direction.LeftToRight, RawDx: 1000, RawDy: 0, SampleCount: 10, ImpliedDegrees: 10.0),
            new TrialResult(Direction.RightToLeft, RawDx: -4000, RawDy: 0, SampleCount: 10, ImpliedDegrees: 20.0),
            new TrialResult(Direction.LeftToRight, RawDx: 9000, RawDy: 0, SampleCount: 10, ImpliedDegrees: 30.0),
        };

        var summary = SessionSummaryBuilder.Build(results, new ApexLegendsProfile());

        Assert.Equal(200.0, summary.HorizontalStats.Median, precision: 6);

        var expectedTargetDegreesPerCount = 1.0 / 200.0;
        Assert.Equal(expectedTargetDegreesPerCount, summary.TargetDegreesPerCount!.Value, precision: 9);

        var expectedSensitivity = expectedTargetDegreesPerCount / ApexLegendsProfile.MYaw;
        Assert.Equal(expectedSensitivity, summary.RecommendedSensitivity!.Value, precision: 6);
        Assert.Null(summary.ErrorMessage);
    }

    [Fact]
    public void Build_NoHorizontalTrials_ReportsError()
    {
        var results = new[]
        {
            new TrialResult(Direction.UpToDown, RawDx: 0, RawDy: 1000, SampleCount: 10, ImpliedDegrees: 10.0),
        };

        var summary = SessionSummaryBuilder.Build(results, new ApexLegendsProfile());

        Assert.NotNull(summary.ErrorMessage);
        Assert.Null(summary.RecommendedSensitivity);
    }

    [Fact]
    public void Build_PerDirectionStats_OnlyIncludeMatchingDirection()
    {
        var results = new[]
        {
            new TrialResult(Direction.LeftToRight, RawDx: 1000, RawDy: 0, SampleCount: 10, ImpliedDegrees: 10.0),
            new TrialResult(Direction.UpToDown, RawDx: 0, RawDy: 2000, SampleCount: 10, ImpliedDegrees: 10.0),
        };

        var summary = SessionSummaryBuilder.Build(results, new ApexLegendsProfile());

        Assert.Equal(1, summary.PerDirectionStats[Direction.LeftToRight].SampleCount);
        Assert.Equal(1, summary.PerDirectionStats[Direction.UpToDown].SampleCount);
        Assert.Equal(0, summary.PerDirectionStats[Direction.RightToLeft].SampleCount);
        Assert.Equal(0, summary.PerDirectionStats[Direction.DownToUp].SampleCount);
    }
}
