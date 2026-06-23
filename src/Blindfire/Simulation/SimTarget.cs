using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace Blindfire.Simulation;

// A single shootable dummy: a flattened box silhouette standing on the
// ground, optionally wandering along a MovingTargetLane. Position is driven
// entirely through the ModelVisual3D's own TranslateTransform3D so moving
// targets can reposition every frame without rebuilding any geometry.
public sealed class SimTarget
{
    private const double Width = 0.5;
    private const double Height = 1.8;
    private const double Depth = 0.25;
    private const double CollisionRadiusMeters = 0.45;

    private static readonly MeshGeometry3D SharedMesh = Mesh3DFactory.CreateBox(Width, Height, Depth);
    private static readonly Color IdleColor = Color.FromRgb(0xE0, 0x40, 0x40);
    private static readonly Color HitColor = Colors.White;

    private readonly SolidColorBrush _brush = new(IdleColor);
    private readonly TranslateTransform3D _transform;
    private readonly MovingTargetLane? _lane;

    // World-standing height: the box is centered on its own origin, so it
    // needs lifting by half its height to stand on the y=0 ground plane.
    public SimTarget(double x, double groundZ, MovingTargetLane? lane = null)
    {
        _lane = lane;
        _transform = new TranslateTransform3D(x, Height / 2.0, groundZ);

        var material = new DiffuseMaterial(_brush);
        var model = new GeometryModel3D(SharedMesh, material);
        Visual = new ModelVisual3D { Content = model, Transform = _transform };
    }

    public ModelVisual3D Visual { get; }

    public Point3D Position => new(_transform.OffsetX, _transform.OffsetY, _transform.OffsetZ);

    public double CollisionRadius => CollisionRadiusMeters;

    public void Advance(double deltaSeconds)
    {
        if (_lane == null)
        {
            return;
        }

        _lane.Advance(deltaSeconds);
        _transform.OffsetX = _lane.CurrentX;
    }

    public void RegisterHit()
    {
        var animation = new ColorAnimation(HitColor, IdleColor, TimeSpan.FromMilliseconds(220));
        _brush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
    }
}
