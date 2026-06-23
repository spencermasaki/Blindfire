namespace Blindfire.Trials;

// ImpliedDegrees is the angular gap the user visually judged (Target A to
// Target B, given the session's FOV) - computed via FieldOfViewProjection at
// construction time. CountsPerDegree is the actual measurement that matters:
// how many raw mouse counts the user produced for that visually-judged angle.
//
// PerpendicularImpliedDegrees is the angular gap along the *other* axis -
// e.g. for a left-right trial, the small incidental up-down offset between
// Target A and B. It's real, visible, intentional movement (the user still
// has to land on Target B's actual position, not just match the dominant
// axis), just usually much smaller than the dominant gap. Defaults to 0
// (treated as "not available") for any TrialResult built before this field
// existed.
public sealed record TrialResult(
    Direction Direction, long RawDx, long RawDy, int SampleCount, double ImpliedDegrees, double PathLength = 0,
    IReadOnlyList<TracePoint>? RawTrace = null, double PerpendicularImpliedDegrees = 0)
{
    public IReadOnlyList<TracePoint> Trace => RawTrace ?? Array.Empty<TracePoint>();

    public double DominantAxisDistance => IsHorizontal ? Math.Abs(RawDx) : Math.Abs(RawDy);

    public double PerpendicularDrift => IsHorizontal ? Math.Abs(RawDy) : Math.Abs(RawDx);

    public double VectorMagnitude => Math.Sqrt((double)RawDx * RawDx + (double)RawDy * RawDy);

    public double CountsPerDegree => DominantAxisDistance / ImpliedDegrees;

    public double PerpendicularCountsPerDegree => PerpendicularImpliedDegrees > 0 ? PerpendicularDrift / PerpendicularImpliedDegrees : 0;

    // Ratio of the straight-line distance actually covered (VectorMagnitude)
    // to the total distance the cursor traveled getting there (PathLength).
    // 1.0 means a perfectly direct path; lower values mean wandering,
    // overshoot/correction, or curvature between the two clicks.
    public double StraightnessRatio => PathLength > 0 ? VectorMagnitude / PathLength : 0;

    private bool IsHorizontal => Direction is Direction.LeftToRight or Direction.RightToLeft;
}
