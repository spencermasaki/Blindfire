namespace Blindfire.Calibration;

// Detects each time the strafe target reverses direction and measures how
// long the user's own horizontal raw movement took to catch up (lag) and how
// far they kept drifting in the stale direction before catching up
// (overshoot). Together these describe over/under-compensation specifically
// at direction changes - steady-state tracking quality is already covered
// by the plain tracking trial's counts-per-degree.
//
// Operates on three tick-aligned series sampled once per render tick during
// the hold (same tick, same index): elapsed time, the user's cumulative raw
// horizontal delta, and the target's current direction sign. A small dead
// zone (counts) avoids treating input jitter as "caught up".
public static class StrafeCompensationAnalyzer
{
    private const double DeadZoneCounts = 8.0;

    public sealed record Reversal(double LagSeconds, double OvershootCounts);

    public static IReadOnlyList<Reversal> Analyze(
        IReadOnlyList<double> tickTimesSeconds, IReadOnlyList<double> userCumulativeDx, IReadOnlyList<int> targetDirection)
    {
        var count = Math.Min(tickTimesSeconds.Count, Math.Min(userCumulativeDx.Count, targetDirection.Count));
        var reversals = new List<Reversal>();

        for (var i = 1; i < count; i++)
        {
            if (targetDirection[i] == targetDirection[i - 1])
            {
                continue;
            }

            var newDirection = targetDirection[i];
            var baselineDx = userCumulativeDx[i];
            var baselineTime = tickTimesSeconds[i];
            var worstOvershoot = 0.0;
            var caughtUp = false;

            for (var j = i; j < count; j++)
            {
                if (targetDirection[j] != newDirection)
                {
                    // Another reversal landed before the user caught up to
                    // this one - stop crediting it to this earlier reversal.
                    break;
                }

                var delta = userCumulativeDx[j] - baselineDx;
                var wrongWayDistance = newDirection > 0 ? Math.Max(0, -delta) : Math.Max(0, delta);
                worstOvershoot = Math.Max(worstOvershoot, wrongWayDistance);

                var caughtUpThisTick = newDirection > 0 ? delta > DeadZoneCounts : delta < -DeadZoneCounts;
                if (caughtUpThisTick)
                {
                    reversals.Add(new Reversal(tickTimesSeconds[j] - baselineTime, worstOvershoot));
                    caughtUp = true;
                    break;
                }
            }

            if (!caughtUp)
            {
                reversals.Add(new Reversal(tickTimesSeconds[count - 1] - baselineTime, worstOvershoot));
            }
        }

        return reversals;
    }
}
