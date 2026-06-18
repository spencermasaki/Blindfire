namespace Blindfire.Trials;

public sealed class TrialSequenceGenerator
{
    private readonly Random _random;

    public TrialSequenceGenerator(Random random)
    {
        _random = random;
    }

    public IReadOnlyList<Direction> GenerateShuffledDirections(int minPerDirection)
    {
        if (minPerDirection < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minPerDirection), "Must request at least one trial per direction.");
        }

        var directions = Enum.GetValues<Direction>();
        var list = new List<Direction>(directions.Length * minPerDirection);

        foreach (var direction in directions)
        {
            for (var i = 0; i < minPerDirection; i++)
            {
                list.Add(direction);
            }
        }

        ShuffleInPlace(list);
        return list;
    }

    // Spreads a fixed total across the 4 directions as evenly as the count
    // allows (e.g. 10 -> 3/3/2/2) rather than requiring it to be a multiple
    // of 4 like GenerateShuffledDirections does.
    public IReadOnlyList<Direction> GenerateShuffledDirectionsWithTotal(int totalCount)
    {
        if (totalCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount), "Must request at least one trial.");
        }

        var directions = Enum.GetValues<Direction>();
        var list = new List<Direction>(totalCount);

        for (var i = 0; i < totalCount; i++)
        {
            list.Add(directions[i % directions.Length]);
        }

        ShuffleInPlace(list);
        return list;
    }

    private void ShuffleInPlace<T>(List<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
