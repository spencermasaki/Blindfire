namespace AimDecider.Trials;

// TargetBPosition is rendered - the user sees both the dimmed Target A and
// the new Target B at once, and judges the gap between them by eye while the
// cursor itself is hidden.
public sealed record TrialDefinition(int Index, Direction Direction, ScreenPoint TargetAPosition, ScreenPoint TargetBPosition);
