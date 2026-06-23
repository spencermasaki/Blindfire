using Blindfire.Tracking;
using Blindfire.Trials;

namespace Blindfire.Results;

public interface ITrialTile
{
    TrialKind Kind { get; }
}

public sealed record FlickTile(TrialKind Kind, TrialDefinition Definition, TrialResult Result) : ITrialTile;

// Quick flick's chain spans 3 points (P1->P2->P3) across two TrialResults -
// kept as one tile so the results screen shows all 3 targets and both trace
// segments together, rather than two tiles that would each only show 2 of
// the 3 points in isolation.
public sealed record QuickFlickTile(TrialKind Kind, TrialDefinition DefinitionA, TrialResult ResultA, TrialDefinition DefinitionB, TrialResult ResultB) : ITrialTile;

public sealed record TrackingTile(TrackingResult Result) : ITrialTile
{
    public TrialKind Kind => TrialKind.Tracking;
}

public sealed record StrafeTile(TrackingResult Result) : ITrialTile
{
    public TrialKind Kind => TrialKind.Strafe;
}
