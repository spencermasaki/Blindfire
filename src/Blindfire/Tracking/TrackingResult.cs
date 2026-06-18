using Blindfire.Trials;

namespace Blindfire.Tracking;

// Mirrors TrialResult's CountsPerDegree idea, but for a continuous tracking
// session: ImpliedDegreesX/Y are the sum of per-frame angular deltas the
// target traveled (via FieldOfViewProjection), accumulated over the whole
// held duration, rather than a single A-to-B gap.
public sealed record TrackingResult(
    long RawDx, long RawDy, double ImpliedDegreesX, double ImpliedDegreesY, double DurationSeconds, int SampleCount,
    IReadOnlyList<TracePoint>? RawMouseTrace = null, IReadOnlyList<ScreenPoint>? TargetTrace = null)
{
    public double CountsPerDegreeHorizontal => ImpliedDegreesX > 0 ? Math.Abs(RawDx) / ImpliedDegreesX : 0;

    public double CountsPerDegreeVertical => ImpliedDegreesY > 0 ? Math.Abs(RawDy) / ImpliedDegreesY : 0;

    public IReadOnlyList<TracePoint> MouseTrace => RawMouseTrace ?? Array.Empty<TracePoint>();

    public IReadOnlyList<ScreenPoint> Target => TargetTrace ?? Array.Empty<ScreenPoint>();
}
