using System.Windows.Media.Media3D;

namespace Blindfire.Simulation;

// Free-look camera orientation with no roll - the only thing that ever moves
// is yaw/pitch, driven directly by raw mouse counts (see SimulationWindow's
// render loop). Kept dependency-free from WPF's Window/Visual types so the
// yaw/pitch math itself stays as easy to reason about as the rest of this
// app's pure-logic calibration code.
public sealed class CameraLookState
{
    private const double MaxPitchDegrees = 89.0;

    public double YawDegrees { get; private set; }
    public double PitchDegrees { get; private set; }

    // dx/dy are raw mouse counts (positive dx = mouse moved right, positive
    // dy = mouse moved down, matching Win32 raw input convention); degreesPerCount
    // is sensitivity * (ApexLegendsProfile.MYaw or sensitivity * adsMultiplier *
    // MYaw while ADS'd) - see SimulationWindow.
    public void Advance(double dx, double dy, double degreesPerCount)
    {
        YawDegrees = NormalizeAngle(YawDegrees + (dx * degreesPerCount));
        PitchDegrees = Clamp(PitchDegrees - (dy * degreesPerCount), -MaxPitchDegrees, MaxPitchDegrees);
    }

    public Vector3D Forward
    {
        get
        {
            var yawRad = DegreesToRadians(YawDegrees);
            var pitchRad = DegreesToRadians(PitchDegrees);
            var cosPitch = Math.Cos(pitchRad);
            return new Vector3D(cosPitch * Math.Sin(yawRad), Math.Sin(pitchRad), -cosPitch * Math.Cos(yawRad));
        }
    }

    public Vector3D Right
    {
        get
        {
            var right = Vector3D.CrossProduct(Forward, new Vector3D(0, 1, 0));
            right.Normalize();
            return right;
        }
    }

    public Vector3D Up => Vector3D.CrossProduct(Right, Forward);

    private static double NormalizeAngle(double degrees)
    {
        var wrapped = degrees % 360.0;
        return wrapped < 0 ? wrapped + 360.0 : wrapped;
    }

    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}
