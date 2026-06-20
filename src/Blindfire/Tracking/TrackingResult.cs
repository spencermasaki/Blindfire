using Blindfire.Trials;

namespace Blindfire.Tracking;

// Mirrors TrialResult's CountsPerDegree idea, but for a continuous tracking
// session: ImpliedDegreesX/Y are the sum of per-frame angular deltas the
// target traveled (via FieldOfViewProjection), accumulated over the whole
// held duration, rather than a single A-to-B gap. AbsDx/AbsDy must be the
// matching path-length quantity for the user's own raw counts (sum of
// |per-sample delta|, not net displacement) - a net value would cancel out
// on every direction reversal while ImpliedDegrees keeps growing, deflating
// CountsPerDegree toward zero on any trial where the target doesn't move in
// a straight line for its whole duration (i.e. nearly every one).
public sealed record TrackingResult(
    long AbsDx, long AbsDy, double ImpliedDegreesX, double ImpliedDegreesY, double DurationSeconds, int SampleCount,
    IReadOnlyList<TracePoint>? RawMouseTrace = null, IReadOnlyList<ScreenPoint>? TargetTrace = null,
    IReadOnlyList<double>? ReversalLagsSeconds = null, IReadOnlyList<double>? ReversalOvershootCounts = null)
{
    public double CountsPerDegreeHorizontal => ImpliedDegreesX > 0 ? (double)AbsDx / ImpliedDegreesX : 0;

    public double CountsPerDegreeVertical => ImpliedDegreesY > 0 ? (double)AbsDy / ImpliedDegreesY : 0;

    public IReadOnlyList<TracePoint> MouseTrace => RawMouseTrace ?? Array.Empty<TracePoint>();

    public IReadOnlyList<ScreenPoint> Target => TargetTrace ?? Array.Empty<ScreenPoint>();

    // Only populated for strafe trials - one entry per target direction
    // reversal the user lived through during the hold.
    public IReadOnlyList<double> ReversalLags => ReversalLagsSeconds ?? Array.Empty<double>();

    public IReadOnlyList<double> ReversalOvershoots => ReversalOvershootCounts ?? Array.Empty<double>();
}
