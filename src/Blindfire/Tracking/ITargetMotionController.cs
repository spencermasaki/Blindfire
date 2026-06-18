using Blindfire.Trials;

namespace Blindfire.Tracking;

// Common shape for anything that drives a held-tracking target each frame,
// so MainWindow's hold/render-tick code doesn't need to know which motion
// model (free-roam wander vs. side-to-side strafe) is currently active.
public interface ITargetMotionController
{
    ScreenPoint Position { get; }

    void Advance(double deltaTimeSeconds);
}
