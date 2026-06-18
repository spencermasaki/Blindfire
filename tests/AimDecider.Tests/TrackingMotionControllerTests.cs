using AimDecider.Tracking;
using AimDecider.Trials;

namespace AimDecider.Tests;

public class TrackingMotionControllerTests
{
    private const double Width = 1920;
    private const double Height = 1080;

    [Fact]
    public void Advance_StaysWithinScreenBoundsOverManySteps()
    {
        var controller = new TrackingMotionController(new Random(1), Width, Height, new ScreenPoint(Width / 2, Height / 2));

        for (var i = 0; i < 10000; i++)
        {
            controller.Advance(0.016);

            Assert.InRange(controller.Position.X, 0, Width);
            Assert.InRange(controller.Position.Y, 0, Height);
        }
    }

    [Fact]
    public void Advance_NeverMovesFartherThanMaxSpeedAllows()
    {
        var controller = new TrackingMotionController(new Random(2), Width, Height, new ScreenPoint(Width / 2, Height / 2));
        const double deltaTime = 0.016;
        const double maxPossibleStepDistance = 400.0 * deltaTime * 1.01; // small tolerance for floating point

        var previous = controller.Position;
        for (var i = 0; i < 2000; i++)
        {
            controller.Advance(deltaTime);
            var current = controller.Position;

            var dx = current.X - previous.X;
            var dy = current.Y - previous.Y;
            var stepDistance = Math.Sqrt(dx * dx + dy * dy);

            Assert.True(stepDistance <= maxPossibleStepDistance, $"step distance {stepDistance} exceeded max {maxPossibleStepDistance}");
            previous = current;
        }
    }

    [Fact]
    public void Advance_DoesNotReverseHeadingInstantly()
    {
        // With a 90 deg/sec max turn rate, a single ~16ms frame can change
        // heading by at most ~1.44 degrees - nowhere near a sudden reversal.
        var controller = new TrackingMotionController(new Random(3), Width, Height, new ScreenPoint(Width / 2, Height / 2));

        var previous = controller.Position;
        controller.Advance(0.016);
        var first = controller.Position;
        var firstHeading = Math.Atan2(first.Y - previous.Y, first.X - previous.X);

        controller.Advance(0.016);
        var second = controller.Position;
        var secondHeading = Math.Atan2(second.Y - first.Y, second.X - first.X);

        var headingDeltaDegrees = Math.Abs(NormalizeDegrees(secondHeading - firstHeading) * 180.0 / Math.PI);
        Assert.True(headingDeltaDegrees < 10.0, $"heading changed by {headingDeltaDegrees} degrees in one frame");
    }

    private static double NormalizeDegrees(double angle)
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
}
