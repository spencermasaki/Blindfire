using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Blindfire.Audio;
using Blindfire.Calibration;
using Blindfire.GameProfiles;
using Blindfire.Input;
using Blindfire.Native;
using Blindfire.Results;
using Blindfire.Settings;
using Blindfire.Tracking;
using Blindfire.Trials;

namespace Blindfire;

public partial class MainWindow : Window
{
    private const double TargetHitRadius = 30.0;
    private const double TrackingTrialDurationSeconds = 4.0;

    // WPF's coordinate space is device-independent: 96 units = 1 physical
    // inch on screen, regardless of the monitor's actual pixel density.
    private const double ScreenPixelsPerInch = 96.0;
    private const int TotalTrialCount = 30;
    private const double TargetFps = 60.0;

    // Fallback footprint if the results card hasn't been laid out yet;
    // normally the exclusion zone is measured from its actual rendered size
    // (which shrinks/grows as the details section is toggled), plus this
    // margin, so background trace tiles never end up rendered underneath it.
    private const double ResultsCardFallbackWidth = 540.0;
    private const double ResultsCardFallbackHeight = 260.0;
    private const double ResultsCardMargin = 60.0;

    private static readonly Brush TargetAActiveBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x66));
    private static readonly Brush TargetADimmedBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0x5C, 0x66));
    private static readonly Brush AdsTargetAActiveBrush = new SolidColorBrush(Color.FromRgb(0xB1, 0x4E, 0xFF));
    private static readonly Brush AdsTargetADimmedBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xB1, 0x4E, 0xFF));
    private static readonly Brush TargetBStandardBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0xA9, 0xFC));
    private static readonly Brush AdsTargetBBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x4F, 0xD8));
    private static readonly Color TargetAGlowColor = Color.FromRgb(0xFF, 0x5C, 0x66);
    private static readonly Color TargetBGlowColor = Color.FromRgb(0x3D, 0xA9, 0xFC);
    private static readonly Color AdsTargetAGlowColor = Color.FromRgb(0xB1, 0x4E, 0xFF);
    private static readonly Color AdsTargetBGlowColor = Color.FromRgb(0xFF, 0x4F, 0xD8);

    private readonly MouseDeltaAccumulator _accumulator = new();
    private readonly RawMouseInputService _rawInputService;

    private TrialSessionController? _hipfireController;
    private TrialSessionController? _adsController;
    private TrialSessionController? _controller;
    private TrialRunner? _runner;
    private IReadOnlyList<TrialKind> _kindPattern = Array.Empty<TrialKind>();
    private int _slotIndex;
    private List<TrackingResult> _trackingResults = new();
    private readonly List<ITrialTile> _tileData = new();
    private readonly List<Border> _tileElements = new();
    private double _horizontalFovDegrees = 70.0;
    private double? _mouseDpi;
    private bool _isAdsPhase;
    private bool _adsRightMouseDown;

    private bool _awaitingTrackingPress;
    private bool _trackingActive;
    private TrackingMotionController? _trackingMotion;
    private Stopwatch? _trackingStopwatch;
    private double _trackingElapsedSeconds;
    private double _trackingLastRenderElapsed;
    private double _trackingTimeSinceLastUpdate;
    private double _trackingAccumulatedDegreesX;
    private double _trackingAccumulatedDegreesY;
    private double _trackingVerticalFovDegrees;
    private ScreenPoint _trackingPreviousPosition;
    private readonly List<ScreenPoint> _trackingTargetTrace = new();

    public MainWindow()
    {
        InitializeComponent();

        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        _rawInputService = new RawMouseInputService(_accumulator);

        PreviewMouseDown += OnRootPreviewMouseDown;
        PreviewMouseUp += OnRootPreviewMouseUp;
        KeyDown += OnKeyDown;
        Focusable = true;
        Loaded += (_, _) =>
        {
            Focus();
            _rawInputService.Attach(this);
        };
        Closed += (_, _) => _rawInputService.Dispose();

        var savedSettings = UserSettings.Load();
        if (savedSettings != null)
        {
            FovDegreesBox.Text = savedSettings.FovDegrees.ToString("G");
            DpiBox.Text = savedSettings.MouseDpi.ToString("G");
        }
    }

    private bool TryValidateFovAndDpi(out double fov, out double dpi)
    {
        dpi = 0;

        if (!double.TryParse(FovDegreesBox.Text, out fov) || fov <= 0 || fov >= 180)
        {
            StartValidationText.Text = "Horizontal FOV must be a number between 0 and 180.";
            return false;
        }

        if (!double.TryParse(DpiBox.Text, out dpi) || dpi <= 0)
        {
            StartValidationText.Text = "Mouse DPI must be a positive number.";
            return false;
        }

        return true;
    }

    private void OnBeginClicked(object sender, RoutedEventArgs e)
    {
        if (!TryValidateFovAndDpi(out var fov, out var dpi))
        {
            return;
        }

        StartValidationText.Text = string.Empty;
        _horizontalFovDegrees = fov;
        _mouseDpi = dpi;
        new UserSettings(fov, dpi).Save();
        _trackingVerticalFovDegrees = FieldOfViewProjection.DeriveVerticalFov(_horizontalFovDegrees, Width, Height);
        _isAdsPhase = false;
        _adsRightMouseDown = false;

        _kindPattern = TrialKindPattern.Generate(TotalTrialCount);
        var (hipfireCount, adsCount, _) = TrialKindPattern.CountKinds(TotalTrialCount);

        _hipfireController = TrialSessionController.CreateWithTotalCount(
            hipfireCount, Width, Height, new Random(), new TrialPlacementStrategy(new Random()));
        var adsPlacement = new TrialPlacementStrategy(
            new Random(), gapMinPixels: 4.0 * ScreenPixelsPerInch, gapMaxPixels: 6.0 * ScreenPixelsPerInch);
        _adsController = TrialSessionController.CreateWithTotalCount(adsCount, Width, Height, new Random(), adsPlacement);
        _trackingResults = new List<TrackingResult>();
        _tileData.Clear();
        _tileElements.Clear();
        _slotIndex = 0;

        StartPanel.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
        TraceTileCanvas.Children.Clear();
        TraceTileCanvas.Visibility = Visibility.Collapsed;
        TileLegend.Visibility = Visibility.Collapsed;
        DetailsPanel.Visibility = Visibility.Collapsed;
        DetailsToggleButton.Content = "▾ More details";
        HideResultFeedback();
        ProgressText.Visibility = Visibility.Visible;

        PresentSlot(0);
        Focus();
    }

    private void OnRunAgainClicked(object sender, RoutedEventArgs e)
    {
        ResultsPanel.Visibility = Visibility.Collapsed;
        FadeIn(StartPanel);
        ResetSessionState();
        Cursor = Cursors.Arrow;
        Focus();
    }

    private void ResetSessionState()
    {
        _hipfireController = null;
        _adsController = null;
        _controller = null;
        _runner = null;
        _kindPattern = Array.Empty<TrialKind>();
        _slotIndex = 0;
        _trackingResults = new List<TrackingResult>();
        _tileData.Clear();
        _tileElements.Clear();
        _isAdsPhase = false;
        _adsRightMouseDown = false;
        ProgressText.Visibility = Visibility.Collapsed;
        TraceTileCanvas.Children.Clear();
        TraceTileCanvas.Visibility = Visibility.Collapsed;
        TileLegend.Visibility = Visibility.Collapsed;
        DetailsPanel.Visibility = Visibility.Collapsed;
        DetailsToggleButton.Content = "▾ More details";
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private static void FadeIn(UIElement element)
    {
        element.Visibility = Visibility.Visible;
        element.Opacity = 0;
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        Cursor = Cursors.Arrow;

        ResetSessionState();
        HidePrompt();
        HideResultFeedback();
        TargetEllipse.Visibility = Visibility.Collapsed;
        TargetBEllipse.Visibility = Visibility.Collapsed;

        StopTrackingRenderLoop();
        _trackingActive = false;
        _awaitingTrackingPress = false;

        TrialPanel.Visibility = Visibility.Collapsed;
        TrackingPanel.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
        FadeIn(StartPanel);

        Focus();
    }

    private void OnRootPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            _adsRightMouseDown = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (_awaitingTrackingPress)
        {
            var clickPosition = e.GetPosition(RootGrid);
            if (IsWithinTarget(clickPosition, _trackingMotion!.Position))
            {
                ClickSoundPlayer.PlayClick();
                StartTrackingHold();
            }

            return;
        }

        if (_runner == null || _controller == null)
        {
            return;
        }

        switch (_runner.State)
        {
            case TrialState.AwaitingTargetAClick:
                if (_isAdsPhase && !_adsRightMouseDown)
                {
                    break;
                }

                var targetAClick = e.GetPosition(RootGrid);
                if (IsWithinTarget(targetAClick, _runner.Definition.TargetAPosition))
                {
                    ClickSoundPlayer.PlayClick();
                    _runner.OnTargetAClicked();
                    Cursor = Cursors.None;
                    DimTargetA();
                    ShowTargetB(_runner.Definition.TargetBPosition);
                    ShowDirectionPrompt(_runner.Definition.Direction);
                }

                break;

            case TrialState.AwaitingFeelClick:
                ClickSoundPlayer.PlayClick();
                var impliedDegrees = ComputeImpliedDegrees(_runner.Definition);
                _runner.OnFeelClicked(impliedDegrees);
                CenterCursor();
                Cursor = Cursors.Arrow;
                HidePrompt();
                TargetEllipse.Visibility = Visibility.Collapsed;
                TargetBEllipse.Visibility = Visibility.Collapsed;

                UpdateResultFeedback(_runner.Result!);
                _controller.RecordResult(_runner.Result!);
                _tileData.Add(new FlickTile(_isAdsPhase ? TrialKind.Ads : TrialKind.Hipfire, _runner.Definition, _runner.Result!));
                _runner.Complete();

                AdvanceToNextSlot();

                break;
        }
    }

    private void OnRootPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            _adsRightMouseDown = false;

            if (_isAdsPhase && _runner?.State == TrialState.AwaitingFeelClick)
            {
                RestartCurrentAdsTrial();
            }

            return;
        }

        if (e.ChangedButton != MouseButton.Left || !_trackingActive)
        {
            return;
        }

        ClickSoundPlayer.PlayClick();
        RestartCurrentTrackingTrial();
    }

    private void StartTrackingHold()
    {
        _awaitingTrackingPress = false;
        _trackingActive = true;
        TrackingInstructionPanel.Visibility = Visibility.Collapsed;
        Cursor = Cursors.None;

        _accumulator.Reset();
        _trackingAccumulatedDegreesX = 0;
        _trackingAccumulatedDegreesY = 0;
        _trackingElapsedSeconds = 0;
        _trackingLastRenderElapsed = 0;
        _trackingTimeSinceLastUpdate = 0;
        _trackingPreviousPosition = _trackingMotion!.Position;
        _trackingTargetTrace.Clear();
        _trackingTargetTrace.Add(_trackingPreviousPosition);

        _trackingStopwatch = Stopwatch.StartNew();
        CompositionTarget.Rendering += OnTrackingRenderTick;
    }

    // Driven by CompositionTarget.Rendering rather than a DispatcherTimer:
    // it fires once per actual render frame (tied to the composition thread,
    // typically vsync-locked), which avoids the ~15ms jitter a DispatcherTimer
    // is subject to and gives much smoother target motion. The FPS slider
    // value still gates how often we actually advance the simulation, so a
    // lower setting simulates a lower update rate without reintroducing timer
    // jitter.
    private void OnTrackingRenderTick(object? sender, EventArgs e)
    {
        var elapsedNow = _trackingStopwatch!.Elapsed.TotalSeconds;
        var frameDelta = elapsedNow - _trackingLastRenderElapsed;
        _trackingLastRenderElapsed = elapsedNow;

        _trackingTimeSinceLastUpdate += frameDelta;
        var targetInterval = 1.0 / TargetFps;

        if (_trackingTimeSinceLastUpdate < targetInterval)
        {
            return;
        }

        var deltaTime = _trackingTimeSinceLastUpdate;
        _trackingTimeSinceLastUpdate = 0;
        _trackingElapsedSeconds = elapsedNow;

        _trackingMotion!.Advance(deltaTime);
        var newPosition = _trackingMotion.Position;

        _trackingAccumulatedDegreesX += FieldOfViewProjection.DegreesBetween(
            _trackingPreviousPosition.X, newPosition.X, Width, _horizontalFovDegrees);
        _trackingAccumulatedDegreesY += FieldOfViewProjection.DegreesBetween(
            _trackingPreviousPosition.Y, newPosition.Y, Height, _trackingVerticalFovDegrees);
        _trackingPreviousPosition = newPosition;
        _trackingTargetTrace.Add(newPosition);

        PositionTrackingTarget(newPosition);

        var remaining = Math.Max(0, TrackingTrialDurationSeconds - _trackingElapsedSeconds);
        TrackingCountdownText.Text = Math.Ceiling(remaining).ToString("F0");

        if (remaining <= 0)
        {
            EndTrackingHold();
        }
    }

    private void EndTrackingHold()
    {
        StopTrackingRenderLoop();
        _trackingActive = false;
        Cursor = Cursors.Arrow;

        var result = new TrackingResult(
            _accumulator.AccumulatedDx,
            _accumulator.AccumulatedDy,
            _trackingAccumulatedDegreesX,
            _trackingAccumulatedDegreesY,
            _trackingElapsedSeconds,
            _accumulator.SampleCount,
            _accumulator.TracePoints.ToArray(),
            _trackingTargetTrace.ToArray());

        _trackingResults.Add(result);
        _tileData.Add(new TrackingTile(result));
        AdvanceToNextSlot();
    }

    private void StopTrackingRenderLoop()
    {
        CompositionTarget.Rendering -= OnTrackingRenderTick;
    }

    private void PositionTrackingTarget(ScreenPoint position)
    {
        Canvas.SetLeft(TrackingTargetEllipse, position.X - (TrackingTargetEllipse.Width / 2));
        Canvas.SetTop(TrackingTargetEllipse, position.Y - (TrackingTargetEllipse.Height / 2));
        Canvas.SetLeft(TrackingCountdownText, position.X - (TrackingCountdownText.Width / 2));
        Canvas.SetTop(TrackingCountdownText, position.Y - (TrackingCountdownText.Height / 2));
    }

    private void ShowFinalResults()
    {
        TrialPanel.Visibility = Visibility.Collapsed;
        TrackingPanel.Visibility = Visibility.Collapsed;
        ProgressText.Visibility = Visibility.Collapsed;
        FadeIn(ResultsPanel);
        ResultsHeadingText.Text = "Calibration Results";

        var profile = new ApexLegendsProfile();
        var hipfireFlick = SessionSummaryBuilder.Build(_hipfireController!.Results, profile);
        var adsFlick = SessionSummaryBuilder.Build(_adsController!.Results, profile);
        var trackingSummary = TrackingSessionSummaryBuilder.Build(_trackingResults, profile);

        double? flickSens = hipfireFlick.ErrorMessage == null ? hipfireFlick.RecommendedSensitivity : null;
        double? flickMultiplier = hipfireFlick.ErrorMessage == null && adsFlick.ErrorMessage == null
            ? adsFlick.RecommendedSensitivity!.Value / hipfireFlick.RecommendedSensitivity!.Value
            : null;

        double? trackingSens = trackingSummary.ErrorMessage == null ? trackingSummary.RecommendedSensitivity : null;

        // The flick test and the tracking test independently estimate the
        // same underlying feel - average whichever ones succeeded into one
        // headline number instead of showing two recommendations to reconcile.
        var sensEstimates = new[] { flickSens, trackingSens }.Where(v => v.HasValue).Select(v => v!.Value).ToList();

        var lines = new List<string>();

        if (sensEstimates.Count == 0)
        {
            lines.Add("Could not compute a recommended sensitivity from either test.");
        }
        else
        {
            var combinedSens = sensEstimates.Average();
            var cm360 = 360.0 * 2.54 / (_mouseDpi!.Value * combinedSens * ApexLegendsProfile.MYaw);
            lines.Add($"Recommended Apex Legends sensitivity: {combinedSens:F3}");
            lines.Add($"(~{cm360:F1} cm/360 at {_mouseDpi:F0} DPI)");
        }

        if (flickMultiplier == null)
        {
            lines.Add("Could not compute a recommended ADS sensitivity multiplier.");
        }
        else
        {
            lines.Add($"Recommended ADS sensitivity multiplier: {flickMultiplier:F2}");
        }

        var straightnessValues = hipfireFlick.StraightnessStats.RawValues.Concat(adsFlick.StraightnessStats.RawValues).ToList();
        if (straightnessValues.Count > 0)
        {
            var avgStraightness = straightnessValues.Average();
            lines.Add($"Average mouse path straightness: {avgStraightness:P0} - {DescribeStraightness(avgStraightness)}");
        }

        RecommendationText.Text = string.Join("\n", lines);

        var sb = new StringBuilder();
        sb.AppendLine($"Horizontal FOV: {_horizontalFovDegrees:F0} degrees");
        sb.AppendLine();
        sb.AppendLine("Combined above from whichever of these succeeded:");
        sb.AppendLine($"  Flick-based:    sensitivity={flickSens?.ToString("F3") ?? "n/a"}  ADS multiplier={flickMultiplier?.ToString("F2") ?? "n/a"}");
        sb.AppendLine($"  Tracking-based: sensitivity={trackingSens?.ToString("F3") ?? "n/a"}");
        sb.AppendLine();
        sb.AppendLine("=== Flick test: Hipfire ===");
        AppendSessionStats(sb, hipfireFlick);
        sb.AppendLine();
        sb.AppendLine("=== Flick test: ADS (right mouse held) ===");
        AppendSessionStats(sb, adsFlick);
        sb.AppendLine();
        sb.AppendLine("=== Tracking test ===");
        AppendTrackingSessionStats(sb, trackingSummary, _trackingResults);

        StatsText.Text = sb.ToString();

        ResultsPanel.UpdateLayout();
        BuildTraceTileCanvas();
        Focus();
    }

    private void OnDetailsToggleClicked(object sender, RoutedEventArgs e)
    {
        var expanding = DetailsPanel.Visibility != Visibility.Visible;
        DetailsPanel.Visibility = expanding ? Visibility.Visible : Visibility.Collapsed;
        DetailsToggleButton.Content = expanding ? "▴ Hide details" : "▾ More details";

        ResultsPanel.UpdateLayout();
        RelayoutTiles();
    }

    // Lays out a dimmed background tile per trial (targets + recorded mouse
    // trace) around the results card rather than under it, so the card stays
    // legible while the tiles provide ambient detail on hover. Called once
    // when results first appear; RelayoutTiles handles re-positioning these
    // same elements afterward as the card resizes.
    private void BuildTraceTileCanvas()
    {
        TraceTileCanvas.Children.Clear();
        _tileElements.Clear();
        TraceTileCanvas.Width = Width;
        TraceTileCanvas.Height = Height;

        var placements = TileLayoutPlanner.PlanTileLayout(Width, Height, ComputeResultsCardExclusionZone(), _tileData.Count);
        var tileElements = TraceTileBuilder.BuildAll(_tileData, placements, Width, Height, _horizontalFovDegrees, _trackingVerticalFovDegrees);

        for (var i = 0; i < tileElements.Count && i < placements.Count; i++)
        {
            Canvas.SetLeft(tileElements[i], placements[i].X);
            Canvas.SetTop(tileElements[i], placements[i].Y);
            TraceTileCanvas.Children.Add(tileElements[i]);
            _tileElements.Add(tileElements[i]);
        }

        TraceTileCanvas.Visibility = Visibility.Visible;
        TileLegend.Visibility = Visibility.Visible;
    }

    // Re-positions the existing tile elements (without rebuilding their
    // trace geometry) to flow around the card's current footprint - used
    // when the details section expands/collapses and the card's height
    // changes, so tiles visibly move out of the way instead of just popping.
    private void RelayoutTiles()
    {
        if (_tileElements.Count == 0)
        {
            return;
        }

        var placements = TileLayoutPlanner.PlanTileLayout(Width, Height, ComputeResultsCardExclusionZone(), _tileElements.Count);
        for (var i = 0; i < _tileElements.Count && i < placements.Count; i++)
        {
            AnimateTileMove(_tileElements[i], placements[i].X, placements[i].Y);
        }
    }

    private Rect ComputeResultsCardExclusionZone()
    {
        var cardWidth = (ResultsPanel.ActualWidth > 0 ? ResultsPanel.ActualWidth : ResultsCardFallbackWidth) + ResultsCardMargin;
        var cardHeight = (ResultsPanel.ActualHeight > 0 ? ResultsPanel.ActualHeight : ResultsCardFallbackHeight) + ResultsCardMargin;
        return new Rect((Width - cardWidth) / 2, (Height - cardHeight) / 2, cardWidth, cardHeight);
    }

    private static void AnimateTileMove(UIElement element, double toLeft, double toTop)
    {
        var fromLeft = Canvas.GetLeft(element);
        var fromTop = Canvas.GetTop(element);
        if (double.IsNaN(fromLeft))
        {
            fromLeft = toLeft;
        }

        if (double.IsNaN(fromTop))
        {
            fromTop = toTop;
        }

        var duration = TimeSpan.FromMilliseconds(300);
        element.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(fromLeft, toLeft, duration));
        element.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(fromTop, toTop, duration));
    }

    private static string DescribeStraightness(double straightness)
    {
        return straightness switch
        {
            >= 0.92 => "you move your mouse in a very direct, straight line.",
            >= 0.80 => "you move in a fairly straight line, with some natural drift or correction.",
            _ => "your mouse path deviates noticeably from a straight line - you may be curving, overshooting, or correcting mid-flick.",
        };
    }

    private static void AppendTrackingSessionStats(StringBuilder sb, TrackingSessionSummary summary, IReadOnlyList<TrackingResult> results)
    {
        var totalDuration = results.Sum(r => r.DurationSeconds);
        var totalSamples = results.Sum(r => r.SampleCount);
        sb.AppendLine($"{results.Count} tracking trials, {totalDuration:F1}s total held, {totalSamples} raw input samples");
        sb.AppendLine($"Horizontal (used for recommendation): median={summary.HorizontalStats.Median:F0} counts/deg");
        sb.AppendLine($"Vertical (informational only):        median={summary.VerticalStats.Median:F0} counts/deg");
    }

    private double ComputeImpliedDegrees(TrialDefinition definition)
    {
        if (definition.Direction is Direction.LeftToRight or Direction.RightToLeft)
        {
            return FieldOfViewProjection.DegreesBetween(
                definition.TargetAPosition.X, definition.TargetBPosition.X, Width, _horizontalFovDegrees);
        }

        var verticalFov = FieldOfViewProjection.DeriveVerticalFov(_horizontalFovDegrees, Width, Height);
        return FieldOfViewProjection.DegreesBetween(
            definition.TargetAPosition.Y, definition.TargetBPosition.Y, Height, verticalFov);
    }

    private void StartNextTrial()
    {
        var definition = _controller!.StartNext();
        _runner = new TrialRunner(_accumulator, definition);
        PresentTrial(definition);
    }

    private void RestartCurrentAdsTrial()
    {
        CenterCursor();
        Cursor = Cursors.Arrow;
        HidePrompt();
        HideResultFeedback();
        TargetBEllipse.Visibility = Visibility.Collapsed;

        var definition = _runner!.Definition;
        _runner = new TrialRunner(_accumulator, definition);
        PresentTrial(definition);
    }

    private void PresentTrial(TrialDefinition definition)
    {
        TargetEllipse.Fill = _isAdsPhase ? AdsTargetAActiveBrush : TargetAActiveBrush;
        ((DropShadowEffect)TargetEllipse.Effect).Color = _isAdsPhase ? AdsTargetAGlowColor : TargetAGlowColor;
        Canvas.SetLeft(TargetEllipse, definition.TargetAPosition.X - (TargetEllipse.Width / 2));
        Canvas.SetTop(TargetEllipse, definition.TargetAPosition.Y - (TargetEllipse.Height / 2));
        TargetEllipse.Visibility = Visibility.Visible;

        TargetBEllipse.Fill = _isAdsPhase ? AdsTargetBBrush : TargetBStandardBrush;
        ((DropShadowEffect)TargetBEllipse.Effect).Color = _isAdsPhase ? AdsTargetBGlowColor : TargetBGlowColor;
        TargetBEllipse.Visibility = Visibility.Collapsed;

        PromptText.Text = _isAdsPhase ? "Hold right mouse, then click the target" : "Click the target";
        PromptSubText.Text = string.Empty;
        PromptPanel.Visibility = Visibility.Visible;

        var prefix = _isAdsPhase ? "ADS Trial" : "Trial";
        ProgressText.Text = $"{prefix} {_slotIndex + 1} / {TotalTrialCount}";
    }

    private void DimTargetA()
    {
        TargetEllipse.Fill = _isAdsPhase ? AdsTargetADimmedBrush : TargetADimmedBrush;
    }

    private void ShowTargetB(ScreenPoint position)
    {
        Canvas.SetLeft(TargetBEllipse, position.X - (TargetBEllipse.Width / 2));
        Canvas.SetTop(TargetBEllipse, position.Y - (TargetBEllipse.Height / 2));
        TargetBEllipse.Visibility = Visibility.Visible;
    }

    private void ShowDirectionPrompt(Direction direction)
    {
        var colorWord = _isAdsPhase ? "magenta" : "blue";
        var baseText = direction switch
        {
            Direction.LeftToRight => $"Drag to the {colorWord} target →",
            Direction.RightToLeft => $"← Drag to the {colorWord} target",
            Direction.UpToDown => $"Drag to the {colorWord} target ↓",
            Direction.DownToUp => $"↑ Drag to the {colorWord} target",
            _ => $"Drag to the {colorWord} target",
        };
        PromptText.Text = _isAdsPhase ? $"Hold RMB - {baseText}" : baseText;
        PromptSubText.Text = "This is a feel test, not a sight test - don't try to peek. Your cursor re-centers once you click, so you won't see where it actually landed.";
        PromptPanel.Visibility = Visibility.Visible;
    }

    // Dispatches to the kind of trial the mixed-pattern sequence calls for
    // next - the three trial types are interleaved within one session, not
    // run as separate phases, so each slot just swaps which UI is active.
    private void PresentSlot(int index)
    {
        var kind = _kindPattern[index];
        if (kind == TrialKind.Tracking)
        {
            EnterTrackingSlot();
        }
        else
        {
            EnterFlickSlot(isAds: kind == TrialKind.Ads);
        }
    }

    private void AdvanceToNextSlot()
    {
        _slotIndex++;
        if (_slotIndex >= _kindPattern.Count)
        {
            ShowFinalResults();
        }
        else
        {
            PresentSlot(_slotIndex);
        }
    }

    private void EnterFlickSlot(bool isAds)
    {
        _isAdsPhase = isAds;
        _adsRightMouseDown = false;
        _controller = isAds ? _adsController : _hipfireController;

        TrackingPanel.Visibility = Visibility.Collapsed;
        if (TrialPanel.Visibility != Visibility.Visible)
        {
            FadeIn(TrialPanel);
        }

        StartNextTrial();
    }

    private void EnterTrackingSlot()
    {
        TrialPanel.Visibility = Visibility.Collapsed;
        if (TrackingPanel.Visibility != Visibility.Visible)
        {
            FadeIn(TrackingPanel);
        }

        ProgressText.Text = $"Tracking Trial {_slotIndex + 1} / {TotalTrialCount}";
        BeginTrackingAttempt();
    }

    // Releasing early (e.g. an accidental double-click) routes here instead
    // of EndTrackingHold - the slot index doesn't advance and no result is
    // recorded, so the player just retries the same segment.
    private void RestartCurrentTrackingTrial()
    {
        StopTrackingRenderLoop();
        _trackingActive = false;
        Cursor = Cursors.Arrow;
        BeginTrackingAttempt();
    }

    private void BeginTrackingAttempt()
    {
        TrackingInstructionPanel.Visibility = Visibility.Visible;
        TrackingCountdownText.Text = string.Empty;

        var startPosition = new ScreenPoint(Width / 2, Height / 2);
        _trackingMotion = new TrackingMotionController(new Random(), Width, Height, startPosition);
        PositionTrackingTarget(startPosition);
        _awaitingTrackingPress = true;
    }

    private void HidePrompt()
    {
        PromptPanel.Visibility = Visibility.Collapsed;
    }

    private void UpdateResultFeedback(TrialResult result)
    {
        ResultFeedbackText.Text =
            $"Last trial - {result.Direction}: raw distance {result.DominantAxisDistance:F0}  (drift: {result.PerpendicularDrift:F0})  " +
            $"implied: {result.ImpliedDegrees:F2}°  →  {result.CountsPerDegree:F0} counts/°  ({result.SampleCount} samples)  " +
            $"|  path straightness: {result.StraightnessRatio:P0}";
        ResultFeedbackPanel.Visibility = Visibility.Visible;
    }

    private void HideResultFeedback()
    {
        ResultFeedbackPanel.Visibility = Visibility.Collapsed;
    }

    private static void AppendSessionStats(StringBuilder sb, SessionSummary summary)
    {
        foreach (var direction in Enum.GetValues<Direction>())
        {
            var stats = summary.PerDirectionStats[direction];
            sb.AppendLine($"{direction,-12} mean={stats.Mean,8:F0}  median={stats.Median,8:F0}  stddev={stats.StdDev,7:F0}  n={stats.SampleCount}  (counts/deg)");
        }

        sb.AppendLine($"Horizontal (used for recommendation): median={summary.HorizontalStats.Median:F0} counts/deg");
        sb.AppendLine($"Vertical (informational only):        median={summary.VerticalStats.Median:F0} counts/deg");
        sb.AppendLine($"Path straightness:                    median={summary.StraightnessStats.Median:P0}  mean={summary.StraightnessStats.Mean:P0}");
    }

    private static bool IsWithinTarget(Point click, ScreenPoint target)
    {
        var dx = click.X - target.X;
        var dy = click.Y - target.Y;
        return dx * dx + dy * dy <= TargetHitRadius * TargetHitRadius;
    }

    // Warps the real OS cursor back to center before it's revealed again, so
    // the user never sees how far their physical mouse actually traveled -
    // the whole point of the trial is judging that distance by feel alone.
    private void CenterCursor()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var physicalX = (int)Math.Round(Width / 2.0 * dpi.DpiScaleX);
        var physicalY = (int)Math.Round(Height / 2.0 * dpi.DpiScaleY);
        CursorNativeMethods.SetCursorPos(physicalX, physicalY);
    }
}
