namespace Blindfire.Simulation;

// Bounces a target back and forth along a fixed world-space X span at a
// constant height/depth - same shape as Tracking/StrafeMotionController
// (instant reversal, gated by a randomized minimum dwell per leg so it can't
// flip faster than a human can react) ported from screen pixels to world
// meters for the 3D range's moving dummies.
public sealed class MovingTargetLane
{
    private const double SpeedMetersPerSecond = 2.2;
    private const double MinLegSeconds = 0.6;
    private const double MaxLegSeconds = 2.2;

    private readonly Random _random;
    private readonly double _minX;
    private readonly double _maxX;

    private double _timeUntilReversal;
    private int _direction;

    public MovingTargetLane(Random random, double startX, double minX, double maxX)
    {
        _random = random;
        _minX = minX;
        _maxX = maxX;
        CurrentX = startX;

        _direction = _random.NextDouble() < 0.5 ? -1 : 1;
        _timeUntilReversal = NextLegDuration();
    }

    public double CurrentX { get; private set; }

    public void Advance(double deltaSeconds)
    {
        _timeUntilReversal -= deltaSeconds;
        if (_timeUntilReversal <= 0)
        {
            Reverse();
        }

        var newX = CurrentX + (_direction * SpeedMetersPerSecond * deltaSeconds);
        if (newX <= _minX || newX >= _maxX)
        {
            newX = Math.Max(_minX, Math.Min(_maxX, newX));
            Reverse();
        }

        CurrentX = newX;
    }

    private void Reverse()
    {
        _direction = -_direction;
        _timeUntilReversal = NextLegDuration();
    }

    private double NextLegDuration() => MinLegSeconds + (_random.NextDouble() * (MaxLegSeconds - MinLegSeconds));
}
