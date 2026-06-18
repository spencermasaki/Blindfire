using AimDecider.Tracking;
using AimDecider.Trials;

namespace AimDecider.Results;

public interface ITrialTile
{
    TrialKind Kind { get; }
}

public sealed record FlickTile(TrialKind Kind, TrialDefinition Definition, TrialResult Result) : ITrialTile;

public sealed record TrackingTile(TrackingResult Result) : ITrialTile
{
    public TrialKind Kind => TrialKind.Tracking;
}
