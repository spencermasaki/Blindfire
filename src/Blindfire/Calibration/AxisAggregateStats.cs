namespace Blindfire.Calibration;

public sealed record AxisAggregateStats(double Mean, double Median, double StdDev, int SampleCount, IReadOnlyList<double> RawValues);
