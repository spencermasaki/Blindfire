using AimDecider.Trials;

namespace AimDecider.Tests;

public class TrialPlacementStrategyTests
{
    private const double Width = 1920;
    private const double Height = 1080;

    [Theory]
    [InlineData(Direction.LeftToRight)]
    [InlineData(Direction.RightToLeft)]
    [InlineData(Direction.UpToDown)]
    [InlineData(Direction.DownToUp)]
    public void GeneratePositions_StaysWithinScreenBounds(Direction direction)
    {
        var strategy = new TrialPlacementStrategy(new Random(99));

        for (var i = 0; i < 50; i++)
        {
            var (a, b) = strategy.GeneratePositions(direction, Width, Height);

            Assert.InRange(a.X, 0, Width);
            Assert.InRange(a.Y, 0, Height);
            Assert.InRange(b.X, 0, Width);
            Assert.InRange(b.Y, 0, Height);
        }
    }

    [Fact]
    public void GeneratePositions_LeftToRight_StartsLeftOfEnd()
    {
        var strategy = new TrialPlacementStrategy(new Random(1));
        var (a, b) = strategy.GeneratePositions(Direction.LeftToRight, Width, Height);
        Assert.True(a.X < b.X);
    }

    [Fact]
    public void GeneratePositions_RightToLeft_StartsRightOfEnd()
    {
        var strategy = new TrialPlacementStrategy(new Random(1));
        var (a, b) = strategy.GeneratePositions(Direction.RightToLeft, Width, Height);
        Assert.True(a.X > b.X);
    }

    [Fact]
    public void GeneratePositions_UpToDown_StartsAboveEnd()
    {
        var strategy = new TrialPlacementStrategy(new Random(1));
        var (a, b) = strategy.GeneratePositions(Direction.UpToDown, Width, Height);
        Assert.True(a.Y < b.Y);
    }

    [Fact]
    public void GeneratePositions_DownToUp_StartsBelowEnd()
    {
        var strategy = new TrialPlacementStrategy(new Random(1));
        var (a, b) = strategy.GeneratePositions(Direction.DownToUp, Width, Height);
        Assert.True(a.Y > b.Y);
    }
}
