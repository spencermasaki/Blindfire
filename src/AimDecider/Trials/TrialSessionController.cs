namespace AimDecider.Trials;

public sealed class TrialSessionController
{
    private readonly List<TrialResult> _results = new();
    private int _currentIndex = -1;

    public IReadOnlyList<TrialDefinition> Definitions { get; }

    public TrialSessionController(IReadOnlyList<TrialDefinition> definitions)
    {
        Definitions = definitions;
    }

    public static TrialSessionController Create(int minPerDirection, double screenWidth, double screenHeight, Random random)
    {
        return Create(minPerDirection, screenWidth, screenHeight, random, new TrialPlacementStrategy(random));
    }

    public static TrialSessionController Create(int minPerDirection, double screenWidth, double screenHeight, Random random, TrialPlacementStrategy placementStrategy)
    {
        var directions = new TrialSequenceGenerator(random).GenerateShuffledDirections(minPerDirection);
        return CreateFromDirections(directions, screenWidth, screenHeight, placementStrategy);
    }

    public static TrialSessionController CreateWithTotalCount(int totalCount, double screenWidth, double screenHeight, Random random, TrialPlacementStrategy placementStrategy)
    {
        var directions = new TrialSequenceGenerator(random).GenerateShuffledDirectionsWithTotal(totalCount);
        return CreateFromDirections(directions, screenWidth, screenHeight, placementStrategy);
    }

    private static TrialSessionController CreateFromDirections(IReadOnlyList<Direction> directions, double screenWidth, double screenHeight, TrialPlacementStrategy placementStrategy)
    {
        var definitions = new List<TrialDefinition>(directions.Count);
        for (var i = 0; i < directions.Count; i++)
        {
            var (a, b) = placementStrategy.GeneratePositions(directions[i], screenWidth, screenHeight);
            definitions.Add(new TrialDefinition(i, directions[i], a, b));
        }

        return new TrialSessionController(definitions);
    }

    public int TotalCount => Definitions.Count;
    public int CompletedCount => _results.Count;
    public IReadOnlyList<TrialResult> Results => _results;
    public bool IsSessionComplete => _results.Count >= Definitions.Count;

    public TrialDefinition StartNext()
    {
        _currentIndex++;
        if (_currentIndex >= Definitions.Count)
        {
            throw new InvalidOperationException("No more trials remaining in this session.");
        }

        return Definitions[_currentIndex];
    }

    public void RecordResult(TrialResult result)
    {
        _results.Add(result);
    }
}
