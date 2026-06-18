using AimDecider.Trials;

namespace AimDecider.Tests;

public class TrialSequenceGeneratorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void GenerateShuffledDirections_ReturnsExactCountsPerDirection(int minPerDirection)
    {
        var generator = new TrialSequenceGenerator(new Random(42));
        var result = generator.GenerateShuffledDirections(minPerDirection);

        Assert.Equal(minPerDirection * 4, result.Count);
        foreach (var direction in Enum.GetValues<Direction>())
        {
            Assert.Equal(minPerDirection, result.Count(d => d == direction));
        }
    }

    [Fact]
    public void GenerateShuffledDirections_ActuallyShufflesOrder()
    {
        var generator = new TrialSequenceGenerator(new Random(7));
        var result = generator.GenerateShuffledDirections(5);

        var sequentialOrder = Enum.GetValues<Direction>()
            .SelectMany(d => Enumerable.Repeat(d, 5))
            .ToList();

        Assert.NotEqual(sequentialOrder, result);
    }

    [Fact]
    public void GenerateShuffledDirections_SameSeedProducesSameSequence()
    {
        var a = new TrialSequenceGenerator(new Random(123)).GenerateShuffledDirections(5);
        var b = new TrialSequenceGenerator(new Random(123)).GenerateShuffledDirections(5);

        Assert.Equal(a, b);
    }

    [Fact]
    public void GenerateShuffledDirections_ThrowsForInvalidMinPerDirection()
    {
        var generator = new TrialSequenceGenerator(new Random());
        Assert.Throws<ArgumentOutOfRangeException>(() => generator.GenerateShuffledDirections(0));
    }
}
