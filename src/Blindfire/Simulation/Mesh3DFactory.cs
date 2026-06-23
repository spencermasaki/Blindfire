using System.Windows;
using System.Windows.Media.Media3D;

namespace Blindfire.Simulation;

// Builds a few primitive meshes by hand instead of pulling in a 3D toolkit -
// the whole range only ever needs axis-aligned boxes (ground tiles, walls,
// target dummies), so a single box builder covers everything. Every box is
// centered at the local origin; callers position it in the world via a
// TranslateTransform3D on the containing ModelVisual3D rather than baking a
// world offset into the mesh, so moving targets can reposition cheaply by
// updating just that transform each frame.
public static class Mesh3DFactory
{
    public static MeshGeometry3D CreateBox(double width, double height, double depth)
    {
        var hw = width / 2.0;
        var hh = height / 2.0;
        var hd = depth / 2.0;

        var mesh = new MeshGeometry3D();

        AddQuad(mesh, new Point3D(-hw, hh, -hd), new Point3D(-hw, hh, hd), new Point3D(hw, hh, hd), new Point3D(hw, hh, -hd)); // top
        AddQuad(mesh, new Point3D(-hw, -hh, -hd), new Point3D(hw, -hh, -hd), new Point3D(hw, -hh, hd), new Point3D(-hw, -hh, hd)); // bottom
        AddQuad(mesh, new Point3D(-hw, -hh, hd), new Point3D(hw, -hh, hd), new Point3D(hw, hh, hd), new Point3D(-hw, hh, hd)); // front (+Z)
        AddQuad(mesh, new Point3D(hw, -hh, -hd), new Point3D(-hw, -hh, -hd), new Point3D(-hw, hh, -hd), new Point3D(hw, hh, -hd)); // back (-Z)
        AddQuad(mesh, new Point3D(hw, -hh, hd), new Point3D(hw, -hh, -hd), new Point3D(hw, hh, -hd), new Point3D(hw, hh, hd)); // right (+X)
        AddQuad(mesh, new Point3D(-hw, -hh, -hd), new Point3D(-hw, -hh, hd), new Point3D(-hw, hh, hd), new Point3D(-hw, hh, -hd)); // left (-X)

        return mesh;
    }

    // p0..p3 must already be wound counter-clockwise as seen from outside
    // the box (each call site below was verified by hand via the cross-
    // product of its edges) so every face's normal/lighting comes out right
    // without needing a runtime normal computation.
    private static void AddQuad(MeshGeometry3D mesh, Point3D p0, Point3D p1, Point3D p2, Point3D p3)
    {
        var baseIndex = mesh.Positions.Count;

        mesh.Positions.Add(p0);
        mesh.Positions.Add(p1);
        mesh.Positions.Add(p2);
        mesh.Positions.Add(p3);

        mesh.TextureCoordinates.Add(new Point(0, 1));
        mesh.TextureCoordinates.Add(new Point(1, 1));
        mesh.TextureCoordinates.Add(new Point(1, 0));
        mesh.TextureCoordinates.Add(new Point(0, 0));

        mesh.TriangleIndices.Add(baseIndex);
        mesh.TriangleIndices.Add(baseIndex + 1);
        mesh.TriangleIndices.Add(baseIndex + 2);
        mesh.TriangleIndices.Add(baseIndex);
        mesh.TriangleIndices.Add(baseIndex + 2);
        mesh.TriangleIndices.Add(baseIndex + 3);
    }
}
