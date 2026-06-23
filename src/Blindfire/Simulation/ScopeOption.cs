namespace Blindfire.Simulation;

// The 5 fixed optics the user can equip. ADS sensitivity stays flat across
// all of them (see SimulationWindow) - only the magnification changes, which
// narrows the camera's horizontal field of view (ScopedFov below).
public sealed record ScopeOption(string Label, double Magnification)
{
    public static readonly IReadOnlyList<ScopeOption> All = new[]
    {
        new ScopeOption("2x", 2.0),
        new ScopeOption("3x", 3.0),
        new ScopeOption("4x", 4.0),
        new ScopeOption("6x", 6.0),
        new ScopeOption("10x", 10.0),
    };

    public double ScopedFov(double hipfireHorizontalFovDegrees) => hipfireHorizontalFovDegrees / Magnification;
}
