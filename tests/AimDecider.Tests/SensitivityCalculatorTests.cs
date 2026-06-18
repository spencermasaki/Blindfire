using AimDecider.Calibration;

namespace AimDecider.Tests;

public class SensitivityCalculatorTests
{
    [Fact]
    public void Aggregate_ComputesMeanMedianStdDev()
    {
        var stats = SensitivityCalculator.Aggregate(new[] { 10.0, 20.0, 30.0, 40.0 });

        Assert.Equal(25.0, stats.Mean);
        Assert.Equal(25.0, stats.Median);
        Assert.Equal(4, stats.SampleCount);
        Assert.True(stats.StdDev > 0);
    }

    [Fact]
    public void Aggregate_OddCountMedianIsMiddleValue()
    {
        var stats = SensitivityCalculator.Aggregate(new[] { 5.0, 1.0, 3.0 });
        Assert.Equal(3.0, stats.Median);
    }

    [Fact]
    public void Aggregate_EmptyInputReturnsZeroedStats()
    {
        var stats = SensitivityCalculator.Aggregate(Array.Empty<double>());
        Assert.Equal(0, stats.SampleCount);
        Assert.Equal(0, stats.Mean);
    }

    [Fact]
    public void ComputeTargetDegreesPerCount_MatchesHandCalculation()
    {
        var result = SensitivityCalculator.ComputeTargetDegreesPerCount(360.0, 16363.6);
        Assert.Equal(360.0 / 16363.6, result, precision: 6);
    }

    [Fact]
    public void ComputeTargetDegreesPerCount_ThrowsForZeroOrNegativeDistance()
    {
        Assert.Throws<InvalidOperationException>(() => SensitivityCalculator.ComputeTargetDegreesPerCount(360, 0));
        Assert.Throws<InvalidOperationException>(() => SensitivityCalculator.ComputeTargetDegreesPerCount(360, -5));
    }
}
