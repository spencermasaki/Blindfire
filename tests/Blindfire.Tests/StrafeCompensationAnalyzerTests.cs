using Blindfire.Calibration;

namespace Blindfire.Tests;

public class StrafeCompensationAnalyzerTests
{
    [Fact]
    public void Analyze_NoDirectionChanges_ReturnsEmpty()
    {
        var times = new[] { 0.0, 0.1, 0.2, 0.3 };
        var userDx = new[] { 0.0, 10.0, 20.0, 30.0 };
        var direction = new[] { 1, 1, 1, 1 };

        var reversals = StrafeCompensationAnalyzer.Analyze(times, userDx, direction);

        Assert.Empty(reversals);
    }

    [Fact]
    public void Analyze_UserOvershootsThenCorrects_RecordsLagAndOvershoot()
    {
        var times = new[] { 0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7 };
        var direction = new[] { 1, 1, 1, -1, -1, -1, -1, -1 };
        // Target reverses at index 3. The user keeps drifting in the old
        // (+1) direction for one more tick (25 -> 35) before correcting.
        var userDx = new[] { 0.0, 10.0, 20.0, 25.0, 35.0, 30.0, 15.0, -5.0 };

        var reversals = StrafeCompensationAnalyzer.Analyze(times, userDx, direction);

        var reversal = Assert.Single(reversals);
        Assert.Equal(0.3, reversal.LagSeconds, precision: 9);
        Assert.Equal(10.0, reversal.OvershootCounts, precision: 9);
    }

    [Fact]
    public void Analyze_UserNeverCatchesUp_LagsUntilTrialEnd()
    {
        var times = new[] { 0.0, 0.1, 0.2, 0.3 };
        var direction = new[] { 1, 1, -1, -1 };
        // Target reverses at index 2 (new direction -1) but the user keeps
        // moving in the original +1 direction for the rest of the trial.
        var userDx = new[] { 0.0, 10.0, 15.0, 20.0 };

        var reversals = StrafeCompensationAnalyzer.Analyze(times, userDx, direction);

        var reversal = Assert.Single(reversals);
        Assert.Equal(0.1, reversal.LagSeconds, precision: 9);
        Assert.Equal(5.0, reversal.OvershootCounts, precision: 9);
    }

    [Fact]
    public void Analyze_MultipleReversals_ProducesOneEntryPerReversal()
    {
        var times = new[] { 0.0, 0.1, 0.2, 0.3, 0.4, 0.5 };
        var direction = new[] { 1, -1, -1, 1, 1, 1 };
        var userDx = new[] { 0.0, -10.0, -20.0, -15.0, 0.0, 10.0 };

        var reversals = StrafeCompensationAnalyzer.Analyze(times, userDx, direction);

        Assert.Equal(2, reversals.Count);
    }
}
