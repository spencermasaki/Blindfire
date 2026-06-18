using Blindfire.Tracking;
using Blindfire.Trials;

namespace Blindfire.Results;

public interface ITrialTile
{
    TrialKind Kind { get; }
}

public sealed record FlickTile(TrialKind Kind, TrialDefinition Definition, TrialResult Result) : ITrialTile;

public sealed record TrackingTile(TrackingResult Result) : ITrialTile
{
    public TrialKind Kind => TrialKind.Tracking;
}

public sealed record StrafeTile(TrackingResult Result) : ITrialTile
{
    public TrialKind Kind => TrialKind.Strafe;
}
