using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Blindfire.Simulation;

// Builds the static range (floor, walls, lighting) and the target dummies.
// Everything is plain axis-aligned boxes from Mesh3DFactory - no textures or
// model files, matching how Audio/ClickSoundPlayer synthesizes sound in code
// instead of shipping assets. World units are meters; the camera starts at
// the origin facing -Z (see CameraLookState), so the range extends mostly
// into negative Z with a short stretch of floor behind the spawn point too,
// so spinning around by feel doesn't show an abrupt edge.
public static class SceneBuilder
{
    private const double GroundHalfWidth = 20.0;
    private const double GroundNearZ = 10.0;
    private const double GroundFarZ = -50.0;
    private const double WallHeight = 8.0;
    private const double TileSize = 5.0;

    private static readonly Color TileColorA = Color.FromRgb(0x3A, 0x4A, 0x3A);
    private static readonly Color TileColorB = Color.FromRgb(0x2E, 0x3A, 0x2E);
    private static readonly Color WallColor = Color.FromRgb(0x22, 0x26, 0x30);

    public static ModelVisual3D BuildStaticRange()
    {
        var group = new Model3DGroup();

        AddCheckeredFloor(group);
        AddWalls(group);

        group.Children.Add(new AmbientLight(Color.FromRgb(0x55, 0x55, 0x5C)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(0xC0, 0xC0, 0xBE), new Vector3D(-0.4, -0.7, -0.5)));

        return new ModelVisual3D { Content = group };
    }

    public static IReadOnlyList<SimTarget> CreateStationaryTargets() => new[]
    {
        new SimTarget(-6.0, -10.0),
        new SimTarget(4.0, -15.0),
        new SimTarget(-2.0, -22.0),
        new SimTarget(8.0, -28.0),
        new SimTarget(-9.0, -35.0),
    };

    public static IReadOnlyList<SimTarget> CreateMovingTargets()
    {
        var random = new Random();
        return new[]
        {
            new SimTarget(0.0, -12.0, new MovingTargetLane(random, 0.0, -8.0, 8.0)),
            new SimTarget(-5.0, -20.0, new MovingTargetLane(random, -5.0, -10.0, 10.0)),
            new SimTarget(3.0, -30.0, new MovingTargetLane(random, 3.0, -6.0, 6.0)),
        };
    }

    private static void AddCheckeredFloor(Model3DGroup group)
    {
        var mesh = Mesh3DFactory.CreateBox(TileSize, 0.1, TileSize);
        var materialA = new DiffuseMaterial(new SolidColorBrush(TileColorA));
        var materialB = new DiffuseMaterial(new SolidColorBrush(TileColorB));

        var columns = (int)((GroundHalfWidth * 2) / TileSize);
        var rows = (int)((GroundNearZ - GroundFarZ) / TileSize);

        for (var col = 0; col < columns; col++)
        {
            for (var row = 0; row < rows; row++)
            {
                var x = -GroundHalfWidth + (TileSize / 2.0) + (col * TileSize);
                var z = GroundNearZ - (TileSize / 2.0) - (row * TileSize);
                var material = (col + row) % 2 == 0 ? materialA : materialB;

                var model = new GeometryModel3D(mesh, material)
                {
                    Transform = new TranslateTransform3D(x, -0.05, z),
                };
                group.Children.Add(model);
            }
        }
    }

    private static void AddWalls(Model3DGroup group)
    {
        var material = new DiffuseMaterial(new SolidColorBrush(WallColor));
        var halfWidth = GroundHalfWidth;
        var depth = GroundNearZ - GroundFarZ;
        var centerZ = (GroundNearZ + GroundFarZ) / 2.0;

        AddBox(group, material, halfWidth * 2, WallHeight, 0.3, 0.0, WallHeight / 2.0, GroundFarZ); // back wall
        AddBox(group, material, halfWidth * 2, WallHeight, 0.3, 0.0, WallHeight / 2.0, GroundNearZ); // front wall (behind spawn)
        AddBox(group, material, 0.3, WallHeight, depth, -halfWidth, WallHeight / 2.0, centerZ); // left wall
        AddBox(group, material, 0.3, WallHeight, depth, halfWidth, WallHeight / 2.0, centerZ); // right wall
    }

    private static void AddBox(Model3DGroup group, Material material, double width, double height, double depth, double x, double y, double z)
    {
        var mesh = Mesh3DFactory.CreateBox(width, height, depth);
        var model = new GeometryModel3D(mesh, material)
        {
            Transform = new TranslateTransform3D(x, y, z),
        };
        group.Children.Add(model);
    }
}
