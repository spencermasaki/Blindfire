namespace AimDecider.Trials;

// A cumulative raw-mouse-count offset from the start of a capture window
// (Target A click for flick trials, hold-start for tracking) - not pixels.
// Rendering code decides how to project this into screen space per trial.
public readonly record struct TracePoint(double X, double Y);
