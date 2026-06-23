using Blindfire.Input;

namespace Blindfire.Trials;

// Drives a single trial: Target A click resets the raw-input accumulator and
// hides the cursor; the next click (anywhere - there's nothing to hit-test
// against during the blind phase) captures whatever raw distance accumulated.
public sealed class TrialRunner
{
    private readonly MouseDeltaAccumulator _accumulator;

    public TrialRunner(MouseDeltaAccumulator accumulator, TrialDefinition definition)
    {
        _accumulator = accumulator;
        Definition = definition;
        State = TrialState.AwaitingTargetAClick;
    }

    public TrialDefinition Definition { get; }
    public TrialState State { get; private set; }
    public TrialResult? Result { get; private set; }

    public void OnTargetAClicked()
    {
        if (State != TrialState.AwaitingTargetAClick)
        {
            return;
        }

        _accumulator.Reset();
        State = TrialState.AwaitingFeelClick;
    }

    public void OnFeelClicked(double impliedDegrees, double perpendicularImpliedDegrees = 0)
    {
        if (State != TrialState.AwaitingFeelClick)
        {
            return;
        }

        Result = new TrialResult(
            Definition.Direction, _accumulator.AccumulatedDx, _accumulator.AccumulatedDy, _accumulator.SampleCount, impliedDegrees,
            _accumulator.AccumulatedPathLength, _accumulator.TracePoints.ToArray(), perpendicularImpliedDegrees);
        State = TrialState.ShowingResult;
    }

    public void Complete()
    {
        State = TrialState.Complete;
    }
}
