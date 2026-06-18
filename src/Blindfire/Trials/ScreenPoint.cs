namespace Blindfire.Trials;

// Deliberately not System.Windows.Point - keeps the trial domain logic free of
// a WPF dependency so it stays plain-C# and unit-testable.
public readonly record struct ScreenPoint(double X, double Y);
