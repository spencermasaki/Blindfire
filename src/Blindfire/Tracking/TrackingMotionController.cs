using Blindfire.Trials;

namespace Blindfire.Tracking;

// Steering-style motion: heading and speed each ease toward a periodically
// re-picked random target (rate-limited turn rate / acceleration), so the
// target wanders smoothly across most of the screen without ever snapping to
// a new direction. Near an edge, the re-picked heading is biased back toward
// the screen center instead of bouncing, keeping the motion organic.
public sealed class TrackingMotionController
{
    private const double MinSpeed = 150.0;
    private const double MaxSpeed = 400.0;
    private const double MaxTurnRateDegreesPerSecond = 90.0;
    private const double MaxAccelerationPxPerSecondSquared = 300.0;
    private const double EdgeMargin = 100.0;
    private const double EdgeAvoidanceZone = 250.0;
    private const double MinRetargetIntervalSeconds = 1.5;
    private const double MaxRetargetIntervalSeconds = 2.5;

    private readonly Random _random;
    private readonly double _screenWidth;
    private readonly double _screenHeight;

    private double _headingRadians;
    private double _targetHeadingRadians;
    private double _speed;
    private double _targetSpeed;
    private double _timeUntilRetarget;

    public TrackingMotionController(Random random, double screenWidth, double screenHeight, ScreenPoint startPosition)
    {
        _random = random;
        _screenWidth = screenWidth;
        _screenHeight = screenHeight;
        Position = startPosition;

        _headingRadians = _random.NextDouble() * Math.PI * 2;
        _targetHeadingRadians = _headingRadians;
        _speed = (MinSpeed + MaxSpeed) / 2;
        _targetSpeed = _speed;
        _timeUntilRetarget = NextRetargetInterval();
    }

    public ScreenPoint Position { get; private set; }

    public void Advance(double deltaTimeSeconds)
    {
        _timeUntilRetarget -= deltaTimeSeconds;
        if (_timeUntilRetarget <= 0)
        {
            PickNewTarget();
            _timeUntilRetarget = NextRetargetInterval();
        }

        _headingRadians = StepTowardAngle(
            _headingRadians, _targetHeadingRadians, DegreesToRadians(MaxTurnRateDegreesPerSecond) * deltaTimeSeconds);
        _speed = StepTowardValue(_speed, _targetSpeed, MaxAccelerationPxPerSecondSquared * deltaTimeSeconds);

        var dx = Math.Cos(_headingRadians) * _speed * deltaTimeSeconds;
        var dy = Math.Sin(_headingRadians) * _speed * deltaTimeSeconds;

        var newX = Clamp(Position.X + dx, EdgeMargin, _screenWidth - EdgeMargin);
        var newY = Clamp(Position.Y + dy, EdgeMargin, _screenHeight - EdgeMargin);

        Position = new ScreenPoint(newX, newY);
    }

    private void PickNewTarget()
    {
        var randomHeading = _random.NextDouble() * Math.PI * 2;

        var centerX = _screenWidth / 2;
        var centerY = _screenHeight / 2;
        var towardCenterHeading = Math.Atan2(centerY - Position.Y, centerX - Position.X);

        var distanceToNearestEdge = Math.Min(
            Math.Min(Position.X - EdgeMargin, _screenWidth - EdgeMargin - Position.X),
            Math.Min(Position.Y - EdgeMargin, _screenHeight - EdgeMargin - Position.Y));

        var centerBiasWeight = Clamp(1.0 - distanceToNearestEdge / EdgeAvoidanceZone, 0.0, 1.0);

        _targetHeadingRadians = BlendAngles(randomHeading, towardCenterHeading, centerBiasWeight);
        _targetSpeed = MinSpeed + _random.NextDouble() * (MaxSpeed - MinSpeed);
    }

    private double NextRetargetInterval() =>
        MinRetargetIntervalSeconds + _random.NextDouble() * (MaxRetargetIntervalSeconds - MinRetargetIntervalSeconds);

    private static double StepTowardAngle(double current, double target, double maxDelta)
    {
        var diff = NormalizeAngle(target - current);
        var clamped = Clamp(diff, -maxDelta, maxDelta);
        return NormalizeAngle(current + clamped);
    }

    private static double StepTowardValue(double current, double target, double maxDelta)
    {
        var diff = target - current;
        var clamped = Clamp(diff, -maxDelta, maxDelta);
        return current + clamped;
    }

    private static double BlendAngles(double a, double b, double weightB)
    {
        var x = (Math.Cos(a) * (1 - weightB)) + (Math.Cos(b) * weightB);
        var y = (Math.Sin(a) * (1 - weightB)) + (Math.Sin(b) * weightB);
        return Math.Atan2(y, x);
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle > Math.PI)
        {
            angle -= 2 * Math.PI;
        }

        while (angle < -Math.PI)
        {
            angle += 2 * Math.PI;
        }

        return angle;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
}
