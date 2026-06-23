using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using Blindfire.Audio;
using Blindfire.GameProfiles;
using Blindfire.Input;

namespace Blindfire.Simulation;

// A free-standing fullscreen range for feeling out a sensitivity/ADS
// multiplier/FOV combination without opening Apex itself. The camera turns
// using exactly the same degrees-per-count math this app's calibration is
// built on (see the Advance() math in OnRenderTick) - the rest (targets,
// weapon, scopes) exists only to give that turn rate something to be tested
// against. No player movement, recoil, or damage model by design.
public partial class SimulationWindow : Window
{
    private static readonly Duration AdsZoomDuration = new(TimeSpan.FromMilliseconds(170));
    private const double GunForwardOffset = 0.35;
    private const double GunRightOffset = 0.18;
    private const double GunDownOffset = 0.18;

    private readonly RawMouseInputService _rawInputService;
    private readonly MouseDeltaAccumulator _accumulator;
    private readonly CameraLookState _look = new();

    private double _sensitivity;
    private double _adsMultiplier;
    private double _hipfireFovDegrees;
    private ScopeOption _equippedScope = ScopeOption.All[0];
    private bool _isAds;
    private bool _movingTargetsEnabled = true;
    private bool _settingsOpen;
    private int _shotsFired;
    private int _hitsLanded;

    private IReadOnlyList<SimTarget> _stationaryTargets = Array.Empty<SimTarget>();
    private IReadOnlyList<SimTarget> _movingTargets = Array.Empty<SimTarget>();
    private Button[] _scopeButtons = Array.Empty<Button>();

    private ModelVisual3D? _gunVisual;
    private GeometryModel3D? _gunModel;

    private Stopwatch? _stopwatch;
    private double _lastFrameElapsedSeconds;

    public SimulationWindow(RawMouseInputService rawInputService, MouseDeltaAccumulator accumulator, double sensitivity, double adsMultiplier, double hipfireFovDegrees)
    {
        InitializeComponent();

        _rawInputService = rawInputService;
        _accumulator = accumulator;
        _sensitivity = sensitivity;
        _adsMultiplier = adsMultiplier;
        _hipfireFovDegrees = hipfireFovDegrees;

        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        PreviewMouseDown += OnPreviewMouseDown;
        PreviewMouseUp += OnPreviewMouseUp;
        PreviewKeyDown += OnPreviewKeyDown;
        Focusable = true;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        Focus();
        Cursor = Cursors.None;
        _rawInputService.Attach(this);
        _accumulator.Reset();

        MainCamera.FieldOfView = _hipfireFovDegrees;

        MainViewport.Children.Add(SceneBuilder.BuildStaticRange());

        _stationaryTargets = SceneBuilder.CreateStationaryTargets();
        foreach (var target in _stationaryTargets)
        {
            MainViewport.Children.Add(target.Visual);
        }

        _movingTargets = SceneBuilder.CreateMovingTargets();
        foreach (var target in _movingTargets)
        {
            MainViewport.Children.Add(target.Visual);
        }

        BuildGunViewmodel();
        LayoutReticle();
        PopulateSettingsFields();
        HighlightEquippedScopeButton();
        UpdateStatsText();

        _stopwatch = Stopwatch.StartNew();
        CompositionTarget.Rendering += OnRenderTick;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRenderTick;
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        var elapsedNow = _stopwatch!.Elapsed.TotalSeconds;
        var deltaSeconds = elapsedNow - _lastFrameElapsedSeconds;
        _lastFrameElapsedSeconds = elapsedNow;

        if (!_settingsOpen)
        {
            var dx = _accumulator.AccumulatedDx;
            var dy = _accumulator.AccumulatedDy;
            _accumulator.Reset();

            var degreesPerCount = _isAds
                ? _sensitivity * _adsMultiplier * ApexLegendsProfile.MYaw
                : _sensitivity * ApexLegendsProfile.MYaw;

            _look.Advance(dx, dy, degreesPerCount);

            MainCamera.LookDirection = _look.Forward;
            MainCamera.UpDirection = _look.Up;

            UpdateGunViewmodel();
        }
        else
        {
            // Drop any input that arrived while paused so resuming doesn't jump.
            _accumulator.Reset();
        }

        foreach (var target in _stationaryTargets)
        {
            target.Advance(deltaSeconds);
        }

        if (_movingTargetsEnabled)
        {
            foreach (var target in _movingTargets)
            {
                target.Advance(deltaSeconds);
            }
        }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_settingsOpen)
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            BeginAds();
        }
        else if (e.ChangedButton == MouseButton.Left)
        {
            Fire();
        }
    }

    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            EndAds();
        }
    }

    // Handled at the tunneling (Preview) stage, on the Window itself, so Tab
    // is caught before WPF's built-in focus-navigation can consume it - that
    // navigation only ever reacts to the bubbling KeyDown event, which would
    // never fire for this key press once something earlier marks it handled.
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Tab)
        {
            e.Handled = true;
            ToggleSettingsOverlay();
        }
    }

    private void BeginAds()
    {
        _isAds = true;
        AnimateFieldOfView(_equippedScope.ScopedFov(_hipfireFovDegrees));
        SetReticleAds(true);
        if (_gunVisual != null)
        {
            _gunVisual.Content = null;
        }
    }

    private void EndAds()
    {
        _isAds = false;
        AnimateFieldOfView(_hipfireFovDegrees);
        SetReticleAds(false);
        if (_gunVisual != null)
        {
            _gunVisual.Content = _gunModel;
        }
    }

    private void AnimateFieldOfView(double targetFov)
    {
        var animation = new DoubleAnimation(MainCamera.FieldOfView, targetFov, AdsZoomDuration);
        MainCamera.BeginAnimation(PerspectiveCamera.FieldOfViewProperty, animation);
    }

    // Semi-auto: this handler only fires once per physical click (WPF does
    // not repeat MouseDown while the button is held), so there is nothing
    // extra to gate for "one shot per press".
    private void Fire()
    {
        _shotsFired++;

        var origin = MainCamera.Position;
        var direction = _look.Forward;

        SimTarget? nearestTarget = null;
        var nearestDistance = double.MaxValue;

        foreach (var target in ActiveTargets())
        {
            var distance = RaySphereIntersection.Distance(origin, direction, target.Position, target.CollisionRadius);
            if (distance.HasValue && distance.Value < nearestDistance)
            {
                nearestDistance = distance.Value;
                nearestTarget = target;
            }
        }

        if (nearestTarget != null)
        {
            _hitsLanded++;
            nearestTarget.RegisterHit();
        }

        ClickSoundPlayer.PlayClick();
        UpdateStatsText();
    }

    private IEnumerable<SimTarget> ActiveTargets()
    {
        foreach (var target in _stationaryTargets)
        {
            yield return target;
        }

        if (_movingTargetsEnabled)
        {
            foreach (var target in _movingTargets)
            {
                yield return target;
            }
        }
    }

    private void ToggleSettingsOverlay()
    {
        _settingsOpen = !_settingsOpen;
        SettingsOverlay.Visibility = _settingsOpen ? Visibility.Visible : Visibility.Collapsed;
        Cursor = _settingsOpen ? Cursors.Arrow : Cursors.None;

        if (!_settingsOpen)
        {
            Focus();
            _accumulator.Reset();
        }
    }

    private void OnScopeButtonClicked(object sender, RoutedEventArgs e)
    {
        var magnification = double.Parse((string)((Button)sender).Tag);
        _equippedScope = ScopeOption.All.First(s => s.Magnification == magnification);
        HighlightEquippedScopeButton();

        if (_isAds)
        {
            AnimateFieldOfView(_equippedScope.ScopedFov(_hipfireFovDegrees));
        }
    }

    private void OnMovingTargetsToggled(object sender, RoutedEventArgs e)
    {
        _movingTargetsEnabled = MovingTargetsCheckBox.IsChecked == true;

        foreach (var target in _movingTargets)
        {
            if (_movingTargetsEnabled)
            {
                if (!MainViewport.Children.Contains(target.Visual))
                {
                    MainViewport.Children.Add(target.Visual);
                }
            }
            else
            {
                MainViewport.Children.Remove(target.Visual);
            }
        }
    }

    private void OnApplySettingsClicked(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(SensitivityBox.Text, out var sensitivity) || sensitivity <= 0)
        {
            SettingsValidationText.Text = "Sensitivity must be a positive number.";
            return;
        }

        if (!double.TryParse(AdsMultiplierBox.Text, out var adsMultiplier) || adsMultiplier <= 0)
        {
            SettingsValidationText.Text = "ADS multiplier must be a positive number.";
            return;
        }

        if (!double.TryParse(FovBox.Text, out var fov) || fov <= 0 || fov >= 180)
        {
            SettingsValidationText.Text = "Horizontal FOV must be a number between 0 and 180.";
            return;
        }

        SettingsValidationText.Text = string.Empty;
        _sensitivity = sensitivity;
        _adsMultiplier = adsMultiplier;
        _hipfireFovDegrees = fov;

        MainCamera.FieldOfView = _isAds ? _equippedScope.ScopedFov(_hipfireFovDegrees) : _hipfireFovDegrees;
    }

    private void PopulateSettingsFields()
    {
        SensitivityBox.Text = _sensitivity.ToString("G4");
        AdsMultiplierBox.Text = _adsMultiplier.ToString("G4");
        FovBox.Text = _hipfireFovDegrees.ToString("G4");
    }

    private void HighlightEquippedScopeButton()
    {
        _scopeButtons = new[] { Scope2xButton, Scope3xButton, Scope4xButton, Scope6xButton, Scope10xButton };

        foreach (var button in _scopeButtons)
        {
            var magnification = double.Parse((string)button.Tag);
            var isEquipped = Math.Abs(magnification - _equippedScope.Magnification) < 0.001;
            button.Background = isEquipped
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("SurfaceBrush");
        }
    }

    private void UpdateStatsText()
    {
        var accuracy = _shotsFired == 0 ? 0.0 : (double)_hitsLanded / _shotsFired;
        StatsText.Text = $"Shots {_shotsFired}  ·  Hits {_hitsLanded}  ·  Accuracy {accuracy:P0}  ·  Scope {_equippedScope.Label}";
    }

    private void SetReticleAds(bool isAds)
    {
        CrosshairTop.Visibility = isAds ? Visibility.Collapsed : Visibility.Visible;
        CrosshairBottom.Visibility = isAds ? Visibility.Collapsed : Visibility.Visible;
        CrosshairLeft.Visibility = isAds ? Visibility.Collapsed : Visibility.Visible;
        CrosshairRight.Visibility = isAds ? Visibility.Collapsed : Visibility.Visible;
        AdsDot.Visibility = isAds ? Visibility.Visible : Visibility.Collapsed;
        ScopeVignette.Visibility = isAds ? Visibility.Visible : Visibility.Collapsed;

        UpdateStatsText();
    }

    private void LayoutReticle()
    {
        var cx = Width / 2.0;
        var cy = Height / 2.0;
        const double gap = 6.0;
        const double length = 10.0;

        CrosshairTop.X1 = cx;
        CrosshairTop.X2 = cx;
        CrosshairTop.Y1 = cy - gap - length;
        CrosshairTop.Y2 = cy - gap;

        CrosshairBottom.X1 = cx;
        CrosshairBottom.X2 = cx;
        CrosshairBottom.Y1 = cy + gap;
        CrosshairBottom.Y2 = cy + gap + length;

        CrosshairLeft.X1 = cx - gap - length;
        CrosshairLeft.X2 = cx - gap;
        CrosshairLeft.Y1 = cy;
        CrosshairLeft.Y2 = cy;

        CrosshairRight.X1 = cx + gap;
        CrosshairRight.X2 = cx + gap + length;
        CrosshairRight.Y1 = cy;
        CrosshairRight.Y2 = cy;

        Canvas.SetLeft(AdsDot, cx - (AdsDot.Width / 2.0));
        Canvas.SetTop(AdsDot, cy - (AdsDot.Height / 2.0));

        var vignetteRadius = Height * 0.46;
        var fullRect = new RectangleGeometry(new Rect(0, 0, Width, Height));
        var hole = new EllipseGeometry(new Point(cx, cy), vignetteRadius, vignetteRadius);
        ScopeVignette.Data = new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, hole);
    }

    private void BuildGunViewmodel()
    {
        var mesh = Mesh3DFactory.CreateBox(0.08, 0.08, 0.32);
        var material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x46)));
        _gunModel = new GeometryModel3D(mesh, material);
        _gunVisual = new ModelVisual3D { Content = _gunModel };
        MainViewport.Children.Add(_gunVisual);
    }

    // Builds the gun's world transform directly from the camera's already-
    // orthonormal Right/Up/Forward basis (verified via cross products in
    // CameraLookState) rather than composing yaw/pitch RotateTransform3Ds in
    // some assumed order - this sidesteps any Euler-order ambiguity entirely.
    // Purely cosmetic: it cannot affect aim, since Fire() always raycasts
    // along the camera's own Forward vector regardless of this transform.
    private void UpdateGunViewmodel()
    {
        if (_gunVisual == null || _isAds)
        {
            return;
        }

        var right = _look.Right;
        var up = _look.Up;
        var forward = _look.Forward;

        var rotation = new Matrix3D(
            right.X, right.Y, right.Z, 0,
            up.X, up.Y, up.Z, 0,
            forward.X, forward.Y, forward.Z, 0,
            0, 0, 0, 1);

        var origin = MainCamera.Position;
        var worldPosition = origin + (forward * GunForwardOffset) + (right * GunRightOffset) + (up * -GunDownOffset);

        var transform = new Transform3DGroup();
        transform.Children.Add(new MatrixTransform3D(rotation));
        transform.Children.Add(new TranslateTransform3D(worldPosition.X, worldPosition.Y, worldPosition.Z));

        _gunVisual.Transform = transform;
    }
}
