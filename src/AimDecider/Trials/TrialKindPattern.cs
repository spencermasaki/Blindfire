namespace AimDecider.Trials;

// A fixed repeating cycle (not shuffled) so the three trial types are mixed
// into one continuous session rather than run as separate phases - tracking
// appears half as often as either flick type per the trial-design intent.
public static class TrialKindPattern
{
    private static readonly TrialKind[] Cycle =
    {
        TrialKind.Hipfire, TrialKind.Ads, TrialKind.Hipfire, TrialKind.Ads, TrialKind.Tracking,
    };

    public static IReadOnlyList<TrialKind> Generate(int totalCount)
    {
        if (totalCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount), "Must request at least one trial.");
        }

        var result = new List<TrialKind>(totalCount);
        for (var i = 0; i < totalCount; i++)
        {
            result.Add(Cycle[i % Cycle.Length]);
        }

        return result;
    }

    public static (int Hipfire, int Ads, int Tracking) CountKinds(int totalCount)
    {
        var pattern = Generate(totalCount);
        return (
            pattern.Count(k => k == TrialKind.Hipfire),
            pattern.Count(k => k == TrialKind.Ads),
            pattern.Count(k => k == TrialKind.Tracking));
    }
}
