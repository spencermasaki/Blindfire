using Blindfire.Trials;

namespace Blindfire.Tracking;

// Bounces left/right at a constant speed along a fixed height. Reversals are
// instant (no easing on direction the way TrackingMotionController eases
// heading) but gated by a minimum dwell time per leg: when a leg begins, its
// total length is immediately randomized between MinLegSeconds and
// MaxLegSeconds, so the target can't flip back and forth faster than a human
// can react, while reversal timing still stays unpredictable. Hitting a
// screen edge forces an immediate reversal regardless of the leg timer,
// since the target physically can't keep going - this is the one case the
// minimum dwell time isn't guaranteed to hold.
public sealed class StrafeMotionController : ITargetMotionController
{
    private const double Speed = 350.0;
    private const double EdgeMargin = 100.0;
    private const double MinLegSeconds = 0.5;
    private const double MaxLegSeconds = 2.0;

    private readonly Random _random;
    private readonly double _screenWidth;
    private readonly double _fixedY;

    private double _timeUntilReversal;

    public StrafeMotionController(Random random, double screenWidth, double screenHeight, ScreenPoint startPosition)
    {
        _random = random;
        _screenWidth = screenWidth;
        _fixedY = startPosition.Y;
        Position = startPosition;

        CurrentDirection = _random.NextDouble() < 0.5 ? -1 : 1;
        _timeUntilReversal = NextLegDuration();
    }

    public ScreenPoint Position { get; private set; }

    public int CurrentDirection { get; private set; }

    public void Advance(double deltaTimeSeconds)
    {
        _timeUntilReversal -= deltaTimeSeconds;
        if (_timeUntilReversal <= 0)
        {
            Reverse();
        }

        var newX = Position.X + (CurrentDirection * Speed * deltaTimeSeconds);

        if (newX <= EdgeMargin || newX >= _screenWidth - EdgeMargin)
        {
            newX = Math.Max(EdgeMargin, Math.Min(_screenWidth - EdgeMargin, newX));
            Reverse();
        }

        Position = new ScreenPoint(newX, _fixedY);
    }

    private void Reverse()
    {
        CurrentDirection = -CurrentDirection;
        _timeUntilReversal = NextLegDuration();
    }

    private double NextLegDuration() => MinLegSeconds + (_random.NextDouble() * (MaxLegSeconds - MinLegSeconds));
}
