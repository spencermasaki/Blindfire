using Blindfire.Trials;

namespace Blindfire.Tracking;

// Common shape for anything that drives a held-tracking target each frame,
// so MainWindow's hold/render-tick code doesn't need to know which motion
// model (free-roam wander vs. side-to-side strafe) is currently active.
public interface ITargetMotionController
{
    ScreenPoint Position { get; }

    void Advance(double deltaTimeSeconds);

    // Pure extrapolation from the current position/heading, used only to draw
    // a short "here's where it's about to go" preview before the hold
    // starts - doesn't mutate any state, so it's safe to call repeatedly.
    ScreenPoint PeekAhead(double secondsAhead);
}
