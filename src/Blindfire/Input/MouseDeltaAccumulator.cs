using Blindfire.Trials;

namespace Blindfire.Input;

// Accumulates raw, unclamped, unaccelerated mouse counts (not OS cursor position).
// Only ever touched from the UI thread, since WM_INPUT arrives via the window's
// own message pump - no locking needed.
public sealed class MouseDeltaAccumulator
{
    // Seeded with a leading (0,0) so Add() is always safe to call - real
    // mouse movement reaches Add() as soon as raw input is attached, before
    // any trial's first Reset().
    private readonly List<TracePoint> _tracePoints = new() { new TracePoint(0, 0) };

    public long AccumulatedDx { get; private set; }
    public long AccumulatedDy { get; private set; }
    public double AccumulatedPathLength { get; private set; }
    public int SampleCount { get; private set; }
    public int AbsoluteModePacketsSkipped { get; private set; }

    // Cumulative offset from the start of the current capture window, one
    // entry per raw input sample (plus a leading (0,0)). Callers that want to
    // keep this beyond the next Reset() must snapshot it (e.g. ToArray()) -
    // this list instance is cleared and reused in place.
    public IReadOnlyList<TracePoint> TracePoints => _tracePoints;

    public void Reset()
    {
        AccumulatedDx = 0;
        AccumulatedDy = 0;
        AccumulatedPathLength = 0;
        SampleCount = 0;
        AbsoluteModePacketsSkipped = 0;
        _tracePoints.Clear();
        _tracePoints.Add(new TracePoint(0, 0));
    }

    public void Add(int dx, int dy)
    {
        AccumulatedDx += dx;
        AccumulatedDy += dy;
        AccumulatedPathLength += Math.Sqrt((double)dx * dx + (double)dy * dy);
        SampleCount++;

        var last = _tracePoints[^1];
        _tracePoints.Add(new TracePoint(last.X + dx, last.Y + dy));
    }

    public void NoteAbsoluteModePacketSkipped()
    {
        AbsoluteModePacketsSkipped++;
    }
}
