using AimDecider.GameProfiles;
using AimDecider.Trials;

namespace AimDecider.Calibration;

public sealed record SessionSummary(
    IReadOnlyDictionary<Direction, AxisAggregateStats> PerDirectionStats,
    AxisAggregateStats HorizontalStats,
    AxisAggregateStats VerticalStats,
    AxisAggregateStats StraightnessStats,
    double? TargetDegreesPerCount,
    double? RecommendedSensitivity,
    string? ErrorMessage);

public static class SessionSummaryBuilder
{
    // Aggregates each trial's CountsPerDegree (raw mouse counts produced per
    // degree of visually-judged A-to-B gap) directly, rather than a flat
    // distance divided by an abstract anchor - every trial is tied to a real,
    // visible, known on-screen distance.
    public static SessionSummary Build(IReadOnlyList<TrialResult> results, IGameProfile gameProfile)
    {
        var perDirection = new Dictionary<Direction, AxisAggregateStats>();
        foreach (var direction in Enum.GetValues<Direction>())
        {
            var values = results.Where(r => r.Direction == direction).Select(r => r.CountsPerDegree);
            perDirection[direction] = SensitivityCalculator.Aggregate(values);
        }

        var horizontalValues = results
            .Where(r => r.Direction is Direction.LeftToRight or Direction.RightToLeft)
            .Select(r => r.CountsPerDegree);
        var verticalValues = results
            .Where(r => r.Direction is Direction.UpToDown or Direction.DownToUp)
            .Select(r => r.CountsPerDegree);

        var horizontalStats = SensitivityCalculator.Aggregate(horizontalValues);
        var verticalStats = SensitivityCalculator.Aggregate(verticalValues);
        var straightnessStats = SensitivityCalculator.Aggregate(results.Select(r => r.StraightnessRatio));

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

        return new SessionSummary(perDirection, horizontalStats, verticalStats, straightnessStats, targetDegreesPerCount, recommendedSensitivity, errorMessage);
    }
}
