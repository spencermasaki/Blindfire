namespace Blindfire.Calibration;

public static class SensitivityCalculator
{
    public static AxisAggregateStats Aggregate(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count == 0)
        {
            return new AxisAggregateStats(0, 0, 0, 0, list);
        }

        var mean = list.Average();
        var median = ComputeMedian(list);
        var stdDev = ComputeSampleStdDev(list, mean);

        return new AxisAggregateStats(mean, median, stdDev, list.Count, list);
    }

    // Caller should use the median (not mean) as the working value fed into
    // ComputeTargetDegreesPerCount - it's robust against an occasional
    // misclick/outlier trial.
    public static double ComputeTargetDegreesPerCount(double calibrationAnchorDegrees, double medianRawDistance)
    {
        if (medianRawDistance <= 0)
        {
            throw new InvalidOperationException("Median raw distance must be positive to compute a sensitivity recommendation.");
        }

        return calibrationAnchorDegrees / medianRawDistance;
    }

    private static double ComputeMedian(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }

    private static double ComputeSampleStdDev(List<double> values, double mean)
    {
        if (values.Count <= 1)
        {
            return 0;
        }

        var sumSquares = values.Sum(v => (v - mean) * (v - mean));
        return Math.Sqrt(sumSquares / (values.Count - 1));
    }
}
