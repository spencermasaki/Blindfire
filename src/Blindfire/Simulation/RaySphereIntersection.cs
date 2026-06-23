using System.Windows.Media.Media3D;

namespace Blindfire.Simulation;

// Standard analytic ray-sphere hit test, used for hitscan shots against each
// SimTarget's collision sphere - simple geometry is enough fidelity for an
// aim-feel range and avoids pulling in a physics library for one formula.
public static class RaySphereIntersection
{
    // direction must be normalized. Returns the nearest positive hit
    // distance along the ray, or null if the ray misses or the sphere is
    // entirely behind the origin.
    public static double? Distance(Point3D origin, Vector3D direction, Point3D sphereCenter, double sphereRadius)
    {
        var originToCenter = sphereCenter - origin;
        var projection = Vector3D.DotProduct(originToCenter, direction);

        var perpendicularDistanceSquared = Vector3D.DotProduct(originToCenter, originToCenter) - (projection * projection);
        var radiusSquared = sphereRadius * sphereRadius;
        if (perpendicularDistanceSquared > radiusSquared)
        {
            return null;
        }

        var halfChord = Math.Sqrt(radiusSquared - perpendicularDistanceSquared);
        var nearDistance = projection - halfChord;
        var farDistance = projection + halfChord;

        if (farDistance < 0)
        {
            return null;
        }

        return nearDistance >= 0 ? nearDistance : farDistance;
    }
}
