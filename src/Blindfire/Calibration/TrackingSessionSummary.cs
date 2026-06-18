using Blindfire.GameProfiles;
using Blindfire.Tracking;

namespace Blindfire.Calibration;

public sealed record TrackingSessionSummary(
    AxisAggregateStats HorizontalStats,
    AxisAggregateStats VerticalStats,
    double? TargetDegreesPerCount,
    double? RecommendedSensitivity,
    string? ErrorMessage);

public static class TrackingSessionSummaryBuilder
{
    // Mirrors SessionSummaryBuilder.Build, but aggregating across several
    // short tracking trials (each with its own CountsPerDegree) instead of
    // one long continuous hold.
    public static TrackingSessionSummary Build(IReadOnlyList<TrackingResult> results, IGameProfile gameProfile)
    {
        var horizontalStats = SensitivityCalculator.Aggregate(results.Select(r => r.CountsPerDegreeHorizontal));
        var verticalStats = SensitivityCalculator.Aggregate(results.Select(r => r.CountsPerDegreeVertical));

        double? targetDegreesPerCount = null;
        double? recommendedSensitivity = null;
        string? errorMessage = null;

        try
        {
            targetDegreesPerCount = SensitivityCalculator.ComputeTargetDegreesPerCount(1.0, horizontalStats.Median);
            recommendedSensitivity = gameProfile.RecommendSensitivity(targetDegreesPerCount.Value);
        }
        catch (InvalidOperationException ex)
        {
            errorMessage = ex.Message;
        }

        return new TrackingSessionSummary(horizontalStats, verticalStats, targetDegreesPerCount, recommendedSensitivity, errorMessage);
    }
}
