namespace AimDecider.Calibration;

// Converts on-screen pixel positions into degrees of rotation, given a field
// of view, using standard rectilinear (pinhole-camera) projection geometry.
// This assumes the configured FOV applies uniformly across the screen's
// aspect ratio. Source-engine games (Apex included) are widely believed to
// apply "Hor+" scaling - the configured FOV is calibrated at a 4:3 baseline
// and stretched for wider aspect ratios - so the true relationship may differ
// slightly from this model. Treated as a reasonable approximation for a feel
// calibration tool, not asserted as exact.
public static class FieldOfViewProjection
{
    public static double DeriveVerticalFov(double horizontalFovDegrees, double screenWidth, double screenHeight)
    {
        var halfHorizontalFovRad = DegreesToRadians(horizontalFovDegrees) / 2.0;
        var halfVerticalFovRad = Math.Atan(Math.Tan(halfHorizontalFovRad) * (screenHeight / screenWidth));
        return RadiansToDegrees(halfVerticalFovRad) * 2.0;
    }

    // pixelA/pixelB are absolute screen-space coordinates along the relevant
    // axis; screenExtentPixels is the full width (for horizontal trials) or
    // full height (for vertical trials) of that axis.
    public static double DegreesBetween(double pixelA, double pixelB, double screenExtentPixels, double fovDegrees)
    {
        var halfExtent = screenExtentPixels / 2.0;
        var halfFovRad = DegreesToRadians(fovDegrees) / 2.0;
        var tanHalfFov = Math.Tan(halfFovRad);

        var angleA = AngleFromCenter(pixelA, halfExtent, tanHalfFov);
        var angleB = AngleFromCenter(pixelB, halfExtent, tanHalfFov);

        return Math.Abs(RadiansToDegrees(angleB - angleA));
    }

    private static double AngleFromCenter(double pixel, double halfExtent, double tanHalfFov)
    {
        var offsetFromCenter = pixel - halfExtent;
        return Math.Atan((offsetFromCenter / halfExtent) * tanHalfFov);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
}
