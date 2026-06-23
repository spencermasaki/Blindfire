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
using System.Windows.Threading;
using Blindfire.Audio;
using Blindfire.Calibration;
using Blindfire.GameProfiles;
using Blindfire.Input;
using Blindfire.Native;
using Blindfire.Results;
using Blindfire.Settings;
using Blindfire.Simulation;
using Blindfire.Tracking;
using Blindfire.Trials;

namespace Blindfire;

public partial class MainWindow : Window
{
    private const double TargetHitRadius = 30.0;
    private const double TrackingTrialDurationSeconds = 4.0;

    // Hipfire/Tracking target diameter. ADS targets are rendered at this
    // size divided by AdsZoomScale, so once the view is actually zoomed in,
    // they appear at exactly this same on-screen size - ADS looks "smaller"
    // only because you haven't raised your sights yet.
    private const double DefaultTargetDiameter = 60.0;

    // Fixed ADS zoom-in amount, always anchored on screen center. ADS
    // trials are confined to a centered Width/AdsZoomScale x
    // Height/AdsZoomScale frame (see the ADS TrialPlacementStrategy setup
    // in OnBeginClicked) so this scale always safely brings both targets
    // into full view - no per-trial scale calculation needed.
    private const double AdsZoomScale = 2.0;

    // ADS gap range is intentionally tighter than Hipfire's: it has to fit
    // within the centered ADS frame (screen size / AdsZoomScale) with room
    // for edge margins, and a tighter flick also fits the "small, precise
    // adjustment while looking down sights" feel.
    private const double AdsGapMinInches = 1.5;
    private const double AdsGapMaxInches = 2.25;
    private const double AdsFrameEdgeMargin = 24.0;

    private static readonly TimeSpan AdsZoomDuration = TimeSpan.FromMilliseconds(180);

    // Quick flick's deadline: each target after the first must be clicked
    // within this window or the whole trial restarts with fresh positions.
    // Also doubles as the duration of that target's shrink-to-nothing cue.
    private static readonly TimeSpan QuickFlickTimerDuration = TimeSpan.FromMilliseconds(1000);

    // Bigger than ADS's tight 1.5-2.25in gap since this variant isn't
    // confined to a zoomed-in sub-frame; smaller than Hipfire's full-screen
    // sweep since the deadline is a tight window to judge and execute a big flick blind.
    private const double QuickFlickGapMinInches = 4.0;
    private const double QuickFlickGapMaxInches = 8.0;

    // WPF's coordinate space is device-independent: 96 units = 1 physical
    // inch on screen, regardless of the monitor's actual pixel density.
    private const double ScreenPixelsPerInch = 96.0;
    private const double TargetFps = 60.0;
    private const double TrackingPreviewAheadSeconds = 0.6;
    private const double TrackingPreviewStartOpacity = 0.45;

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
    private static readonly Brush TrackingTargetBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x20));
    private static readonly Color TrackingGlowColor = Color.FromRgb(0xFF, 0xB0, 0x20);
    private static readonly Brush StrafeTargetBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
    private static readonly Color StrafeGlowColor = Color.FromRgb(0x2E, 0xCC, 0x71);
    private static readonly Brush QuickFlickActiveBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x3F));
    private static readonly Brush QuickFlickDimmedBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xD2, 0x3F));
    private static readonly Color QuickFlickGlowColor = Color.FromRgb(0xFF, 0xD2, 0x3F);
    private static readonly Brush AdsQuickFlickActiveBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF));
    private static readonly Brush AdsQuickFlickDimmedBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0xE5, 0xFF));
    private static readonly Color AdsQuickFlickGlowColor = Color.FromRgb(0x00, 0xE5, 0xFF);
    private static readonly Brush QuickFlickGhostBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xD2, 0x3F));
    private static readonly Brush AdsQuickFlickGhostBrush = new SolidColorBrush(Color.FromArgb(0x50, 0x00, 0xE5, 0xFF));

    private readonly MouseDeltaAccumulator _accumulator = new();
    private readonly RawMouseInputService _rawInputService;

    private TrialSessionController? _hipfireController;
    private TrialSessionController? _adsController;
    private TrialSessionController? _controller;
    private TrialRunner? _runner;
    private IReadOnlyList<TrialKind> _kindPattern = Array.Empty<TrialKind>();
    private int _slotIndex;
    private List<TrackingResult> _trackingResults = new();
    private List<TrackingResult> _strafeResults = new();
    private readonly List<ITrialTile> _tileData = new();
    private readonly List<Border> _tileElements = new();
    private double _horizontalFovDegrees = 70.0;
    private double? _mouseDpi;
    private double? _lastCombinedSensitivity;
    private double? _lastAdsMultiplier;
    private int _totalTrialCount = 30;
    private bool _includePerpendicularAxisData;
    private bool _isAdsPhase;
    private bool _adsRightMouseDown;
    private bool _isInitializing = true;
    private IReadOnlyList<ResultHistoryEntry> _resultHistory = Array.Empty<ResultHistoryEntry>();
    private ResultHistoryEntry? _viewedHistoryEntry;

    private TrialPlacementStrategy? _quickFlickPlacement;
    private TrialPlacementStrategy? _adsQuickFlickPlacement;
    private double _adsFrameWidth;
    private double _adsFrameHeight;
    private double _adsFrameOriginX;
    private double _adsFrameOriginY;
    private List<TrialResult> _quickFlickResults = new();
    private List<TrialResult> _adsQuickFlickResults = new();
    private bool _quickFlickActive;
    private bool _quickFlickIsAds;
    private int _quickFlickSegment;
    private TrialDefinition? _quickFlickDefinitionA;
    private TrialDefinition? _quickFlickDefinitionB;
    private TrialResult? _quickFlickPendingResultA;
    private DispatcherTimer? _quickFlickTimer;

    private bool _awaitingTrackingPress;
    private bool _trackingActive;
    private TrialKind _activeHoldKind;
    private ITargetMotionController? _trackingMotion;
    private StrafeMotionController? _activeStrafeController;
    private Stopwatch? _trackingStopwatch;
    private double _trackingElapsedSeconds;
    private double _trackingLastRenderElapsed;
    private double _trackingTimeSinceLastUpdate;
    private double _trackingAccumulatedDegreesX;
    private double _trackingAccumulatedDegreesY;
    private double _trackingVerticalFovDegrees;
    private ScreenPoint _trackingPreviousPosition;
    private readonly List<ScreenPoint> _trackingTargetTrace = new();
    private readonly List<double> _strafeTickTimes = new();
    private readonly List<double> _strafeUserCumulativeDx = new();
    private readonly List<int> _strafeDirectionSamples = new();

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
            _horizontalFovDegrees = savedSettings.FovDegrees;
            _mouseDpi = savedSettings.MouseDpi;
            ClickVolumeSlider.Value = Math.Clamp(savedSettings.ClickVolume, 0.0, 1.0) * 100;
            TrialCountSlider.Value = Math.Clamp(savedSettings.TrialCount, 10, 100);
        }

        RefreshTrialCountDisplay();
        RefreshResultHistoryDisplay();

        // Random click sounds always start off, regardless of what was last selected.
        _isInitializing = false;
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
        new UserSettings(fov, dpi, ClickVolumeSlider.Value / 100.0, _totalTrialCount).Save();
        _trackingVerticalFovDegrees = FieldOfViewProjection.DeriveVerticalFov(_horizontalFovDegrees, Width, Height);
        _isAdsPhase = false;
        _adsRightMouseDown = false;

        _kindPattern = TrialKindPattern.Generate(_totalTrialCount);
        var (hipfireCount, adsCount, _, _, _, _) = TrialKindPattern.CountKinds(_totalTrialCount);

        _hipfireController = TrialSessionController.CreateWithTotalCount(
            hipfireCount, Width, Height, new Random(), new TrialPlacementStrategy(new Random()));

        // ADS targets are confined to a centered frame the same size as
        // what AdsZoomScale will magnify to fill the screen - generate
        // positions as if that frame's own dimensions were the screen, then
        // shift them into the frame's actual on-screen position.
        var adsFrameWidth = Width / AdsZoomScale;
        var adsFrameHeight = Height / AdsZoomScale;
        var adsFrameOriginX = (Width - adsFrameWidth) / 2.0;
        var adsFrameOriginY = (Height - adsFrameHeight) / 2.0;
        var adsPlacement = new TrialPlacementStrategy(
            new Random(), edgeMargin: AdsFrameEdgeMargin,
            gapMinPixels: AdsGapMinInches * ScreenPixelsPerInch, gapMaxPixels: AdsGapMaxInches * ScreenPixelsPerInch,
            originX: adsFrameOriginX, originY: adsFrameOriginY);
        _adsController = TrialSessionController.CreateWithTotalCount(adsCount, adsFrameWidth, adsFrameHeight, new Random(), adsPlacement);
        _adsFrameWidth = adsFrameWidth;
        _adsFrameHeight = adsFrameHeight;
        _adsFrameOriginX = adsFrameOriginX;
        _adsFrameOriginY = adsFrameOriginY;
        _quickFlickPlacement = new TrialPlacementStrategy(
            new Random(), gapMinPixels: QuickFlickGapMinInches * ScreenPixelsPerInch, gapMaxPixels: QuickFlickGapMaxInches * ScreenPixelsPerInch);
        _adsQuickFlickPlacement = adsPlacement;
        _quickFlickResults = new List<TrialResult>();
        _adsQuickFlickResults = new List<TrialResult>();
        _trackingResults = new List<TrackingResult>();
        _strafeResults = new List<TrackingResult>();
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

    private void OnOpenSimulatorClicked(object sender, RoutedEventArgs e)
    {
        var savedSettings = UserSettings.Load();
        var sensitivity = savedSettings?.LastRecommendedSensitivity ?? 1.0;
        var adsMultiplier = savedSettings?.LastAdsMultiplier ?? 1.0;
        var fov = double.TryParse(FovDegreesBox.Text, out var parsedFov) && parsedFov > 0 && parsedFov < 180
            ? parsedFov
            : _horizontalFovDegrees;

        OpenSimulator(sensitivity, adsMultiplier, fov);
    }

    private void OnTrySimulatorClicked(object sender, RoutedEventArgs e)
    {
        if (!_lastCombinedSensitivity.HasValue)
        {
            return;
        }

        OpenSimulator(_lastCombinedSensitivity.Value, _lastAdsMultiplier ?? 1.0, _horizontalFovDegrees);
    }

    // Hands raw mouse input over to the simulator for the duration of its
    // session and reclaims it once the simulator closes -
    // RawMouseInputService.Attach re-targets Win32's raw input registration,
    // which is process-wide, so only one of MainWindow/SimulationWindow can
    // be the active target at a time.
    private void OpenSimulator(double sensitivity, double adsMultiplier, double fovDegrees)
    {
        var simulationWindow = new SimulationWindow(_rawInputService, _accumulator, sensitivity, adsMultiplier, fovDegrees);
        simulationWindow.Closed += (_, _) =>
        {
            _rawInputService.Attach(this);
            Show();
            Focus();
        };

        Hide();
        simulationWindow.Show();
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
        _strafeResults = new List<TrackingResult>();
        _quickFlickResults = new List<TrialResult>();
        _adsQuickFlickResults = new List<TrialResult>();
        _tileData.Clear();
        _tileElements.Clear();
        _isAdsPhase = false;
        _adsRightMouseDown = false;
        _quickFlickActive = false;
        _quickFlickIsAds = false;
        _quickFlickDefinitionA = null;
        _quickFlickDefinitionB = null;
        _quickFlickPendingResultA = null;
        DisarmQuickFlickTimer();
        HideQuickFlickCountdown();
        HideAdsZoomPreviewBorder();
        TrialCanvasZoomTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        TrialCanvasZoomTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        TrialCanvasZoomTransform.ScaleX = 1.0;
        TrialCanvasZoomTransform.ScaleY = 1.0;
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

    private void OnMoreOptionsToggleClicked(object sender, RoutedEventArgs e)
    {
        var expanding = MoreOptionsPanel.Visibility != Visibility.Visible;
        MoreOptionsPanel.Visibility = expanding ? Visibility.Visible : Visibility.Collapsed;
        MoreOptionsToggleButton.Content = expanding ? "▴ Hide options" : "▾ More options";
    }

    private void OnRandomClickSoundsToggled(object sender, RoutedEventArgs e)
    {
        ClickSoundPlayer.RandomSoundsEnabled = RandomClickSoundsCheckBox.IsChecked == true;
    }

    private void OnIncludePerpendicularAxisToggled(object sender, RoutedEventArgs e)
    {
        _includePerpendicularAxisData = IncludePerpendicularAxisCheckBox.IsChecked == true;
    }

    private void OnClickVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var volume = ClickVolumeSlider.Value / 100.0;
        ClickSoundPlayer.Volume = volume;
        ClickVolumeValueText.Text = $"{(int)ClickVolumeSlider.Value}%";

        if (_isInitializing)
        {
            return;
        }

        new UserSettings(_horizontalFovDegrees, _mouseDpi ?? 800.0, volume, _totalTrialCount).Save();
    }

    private void OnSoundPreviewTargetClicked(object sender, MouseButtonEventArgs e)
    {
        ClickSoundPlayer.PlayClick();
    }

    private void OnTrialCountChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // TrialCountSlider's Minimum (10) differs from the Slider default (0),
        // so WPF coerces Value and fires this event while InitializeComponent
        // is still building the tree - before TrialCountBreakdownText (declared
        // after the slider in XAML) has been constructed. Bail out on that
        // spurious early call; RefreshTrialCountDisplay (called once after
        // construction finishes) handles the real initial population.
        if (TrialCountBreakdownText == null)
        {
            return;
        }

        RefreshTrialCountDisplay();

        if (_isInitializing)
        {
            return;
        }

        new UserSettings(_horizontalFovDegrees, _mouseDpi ?? 800.0, ClickVolumeSlider.Value / 100.0, _totalTrialCount).Save();
    }

    private void RefreshTrialCountDisplay()
    {
        _totalTrialCount = (int)Math.Round(TrialCountSlider.Value);
        TrialCountValueText.Text = _totalTrialCount.ToString();

        var (hipfireCount, adsCount, trackingCount, _, quickFlickCount, adsQuickFlickCount) = TrialKindPattern.CountKinds(_totalTrialCount);
        TrialCountBreakdownText.Text = $"{hipfireCount} hipfire, {adsCount} ADS, {quickFlickCount} quick flick, {adsQuickFlickCount} ADS quick flick, {trackingCount} tracking";
    }

    // Re-reads history.json and refreshes the up-to-3 "Recent Results" rows
    // on the start screen - called at startup and again right after a fresh
    // run is appended, so a just-finished session shows up immediately
    // without needing an app restart.
    private void RefreshResultHistoryDisplay()
    {
        _resultHistory = ResultHistoryStore.Load();

        var rows = new[]
        {
            (Row: HistoryRow0, Summary: HistorySummary0, Settings: HistorySettings0),
            (Row: HistoryRow1, Summary: HistorySummary1, Settings: HistorySettings1),
            (Row: HistoryRow2, Summary: HistorySummary2, Settings: HistorySettings2),
        };

        for (var i = 0; i < rows.Length; i++)
        {
            var (row, summary, settings) = rows[i];
            if (i < _resultHistory.Count)
            {
                var entry = _resultHistory[i];
                summary.Text = $"{entry.Timestamp.LocalDateTime:MMM d, h:mm tt} - {entry.RecommendationSummary.Split('\n')[0]}";
                settings.Text = $"FOV {entry.FovDegrees:F0}, DPI {entry.MouseDpi:F0}, {entry.TrialCount} trials";
                row.Visibility = Visibility.Visible;
            }
            else
            {
                row.Visibility = Visibility.Collapsed;
            }
        }

        ResultHistoryPanel.Visibility = _resultHistory.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnViewHistoryEntryClicked(object sender, RoutedEventArgs e)
    {
        var index = int.Parse((string)((Button)sender).Tag);
        if (index >= _resultHistory.Count)
        {
            return;
        }

        var entry = _resultHistory[index];
        _viewedHistoryEntry = entry;

        StartPanel.Visibility = Visibility.Collapsed;
        HistoryHeadingText.Text = $"Result from {entry.Timestamp.LocalDateTime:MMM d, yyyy h:mm tt}";
        HistorySettingsUsedText.Text = $"FOV {entry.FovDegrees:F0}°, DPI {entry.MouseDpi:F0}, {entry.TrialCount} trials" +
            (entry.IncludePerpendicularAxisData ? ", perpendicular-axis data included" : "");
        HistoryRecommendationText.Text = entry.RecommendationSummary;
        HistoryStatsText.Text = entry.DetailsText;
        HistoryDetailsPanel.Visibility = Visibility.Collapsed;
        HistoryDetailsToggleButton.Content = "▾ More details";
        HistoryTrySimulatorButton.IsEnabled = entry.CombinedSensitivity.HasValue;

        FadeIn(HistoryDetailPanel);
    }

    private void OnHistoryDetailsToggleClicked(object sender, RoutedEventArgs e)
    {
        var expanding = HistoryDetailsPanel.Visibility != Visibility.Visible;
        HistoryDetailsPanel.Visibility = expanding ? Visibility.Visible : Visibility.Collapsed;
        HistoryDetailsToggleButton.Content = expanding ? "▴ Hide details" : "▾ More details";
    }

    private void OnHistoryTrySimulatorClicked(object sender, RoutedEventArgs e)
    {
        if (_viewedHistoryEntry?.CombinedSensitivity is not double sensitivity)
        {
            return;
        }

        OpenSimulator(sensitivity, _viewedHistoryEntry.AdsMultiplier ?? 1.0, _viewedHistoryEntry.FovDegrees);
    }

    private void OnHistoryBackClicked(object sender, RoutedEventArgs e)
    {
        HistoryDetailPanel.Visibility = Visibility.Collapsed;
        _viewedHistoryEntry = null;
        FadeIn(StartPanel);
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
        StopTrackingPreview();
        _trackingActive = false;
        _awaitingTrackingPress = false;
        _activeStrafeController = null;

        TrialPanel.Visibility = Visibility.Collapsed;
        TrackingPanel.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Collapsed;
        HistoryDetailPanel.Visibility = Visibility.Collapsed;
        _viewedHistoryEntry = null;
        FadeIn(StartPanel);

        Focus();
    }

    private void OnRootPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            _adsRightMouseDown = true;

            if (_isAdsPhase && _runner?.State == TrialState.AwaitingTargetAClick)
            {
                BeginAdsZoomIn();
            }

            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (_awaitingTrackingPress)
        {
            var clickPosition = e.GetPosition(RootGrid);
            if (IsWithinTarget(clickPosition, _trackingMotion!.Position, TargetHitRadius))
            {
                ClickSoundPlayer.PlayClick();
                StartTrackingHold();
            }

            return;
        }

        if (_quickFlickActive)
        {
            HandleQuickFlickClick(e);
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

                var targetAClick = e.GetPosition(TrialCanvas);
                var targetAHitRadius = _isAdsPhase ? (TargetHitRadius / AdsZoomScale) : TargetHitRadius;
                if (IsWithinTarget(targetAClick, _runner.Definition.TargetAPosition, targetAHitRadius))
                {
                    ClickSoundPlayer.PlayClick();
                    _runner.OnTargetAClicked();
                    Cursor = Cursors.None;
                    DimTargetA();
                    ShowTargetB(_runner.Definition.TargetBPosition);
                    ShowSecondClickPrompt();
                }

                break;

            case TrialState.AwaitingFeelClick:
                ClickSoundPlayer.PlayClick();
                var (impliedDegrees, perpendicularImpliedDegrees) = ComputeImpliedDegrees(_runner.Definition, _isAdsPhase);
                _runner.OnFeelClicked(impliedDegrees, perpendicularImpliedDegrees);
                CenterCursor();
                Cursor = Cursors.Arrow;
                HidePrompt();
                TargetEllipse.Visibility = Visibility.Collapsed;
                TargetBEllipse.Visibility = Visibility.Collapsed;

                UpdateResultFeedback(_runner.Result!);
                _controller.RecordResult(_runner.Result!);
                _tileData.Add(new FlickTile(_isAdsPhase ? TrialKind.Ads : TrialKind.Hipfire, _runner.Definition, _runner.Result!));
                _runner.Complete();

                if (_isAdsPhase)
                {
                    BeginAdsZoomOut(AdvanceToNextSlot);
                }
                else
                {
                    AdvanceToNextSlot();
                }

                break;
        }
    }

    private void OnRootPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            _adsRightMouseDown = false;

            if (_quickFlickActive && _quickFlickIsAds)
            {
                if (_runner?.State == TrialState.AwaitingFeelClick)
                {
                    BeginAdsZoomOut(RestartQuickFlickTrial);
                }
                else if (_runner?.State == TrialState.AwaitingTargetAClick)
                {
                    BeginAdsZoomOut(() => { });
                }

                return;
            }

            if (_isAdsPhase && _runner?.State == TrialState.AwaitingFeelClick)
            {
                BeginAdsZoomOut(RestartCurrentAdsTrial);
                return;
            }

            if (_isAdsPhase && _runner?.State == TrialState.AwaitingTargetAClick)
            {
                BeginAdsZoomOut(() => { });
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
        StopTrackingPreview();
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
        _strafeTickTimes.Clear();
        _strafeUserCumulativeDx.Clear();
        _strafeDirectionSamples.Clear();

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

        if (_activeStrafeController != null)
        {
            _strafeTickTimes.Add(_trackingElapsedSeconds);
            _strafeUserCumulativeDx.Add(_accumulator.AccumulatedDx);
            _strafeDirectionSamples.Add(_activeStrafeController.CurrentDirection);
        }

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

        if (_activeHoldKind == TrialKind.Strafe)
        {
            var reversals = StrafeCompensationAnalyzer.Analyze(_strafeTickTimes, _strafeUserCumulativeDx, _strafeDirectionSamples);
            var strafeResult = new TrackingResult(
                _accumulator.AccumulatedAbsDx,
                _accumulator.AccumulatedAbsDy,
                _trackingAccumulatedDegreesX,
                _trackingAccumulatedDegreesY,
                _trackingElapsedSeconds,
                _accumulator.SampleCount,
                _accumulator.TracePoints.ToArray(),
                _trackingTargetTrace.ToArray(),
                reversals.Select(r => r.LagSeconds).ToArray(),
                reversals.Select(r => r.OvershootCounts).ToArray());

            _strafeResults.Add(strafeResult);
            _tileData.Add(new StrafeTile(strafeResult));
            AdvanceToNextSlot();
            return;
        }

        var result = new TrackingResult(
            _accumulator.AccumulatedAbsDx,
            _accumulator.AccumulatedAbsDy,
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
        var hipfireFlick = SessionSummaryBuilder.Build(_hipfireController!.Results, profile, _includePerpendicularAxisData);
        var adsFlick = SessionSummaryBuilder.Build(_adsController!.Results, profile, _includePerpendicularAxisData);
        var quickFlick = SessionSummaryBuilder.Build(_quickFlickResults, profile, _includePerpendicularAxisData);
        var adsQuickFlick = SessionSummaryBuilder.Build(_adsQuickFlickResults, profile, _includePerpendicularAxisData);
        var trackingSummary = TrackingSessionSummaryBuilder.Build(_trackingResults, profile);

        double? flickSens = hipfireFlick.ErrorMessage == null ? hipfireFlick.RecommendedSensitivity : null;
        double? quickFlickSens = quickFlick.ErrorMessage == null ? quickFlick.RecommendedSensitivity : null;
        double? trackingSens = trackingSummary.ErrorMessage == null ? trackingSummary.RecommendedSensitivity : null;
        double? adsFlickSens = adsFlick.ErrorMessage == null ? adsFlick.RecommendedSensitivity : null;
        double? adsQuickFlickSens = adsQuickFlick.ErrorMessage == null ? adsQuickFlick.RecommendedSensitivity : null;

        // The flick tests and the tracking test independently estimate the
        // same underlying feel - average whichever ones succeeded into one
        // headline number instead of showing several recommendations to
        // reconcile. Quick flick's rushed, blind clicks are deliberately
        // noisier than Hipfire's - that's the point, not a bug - so they
        // pull the estimate around rather than just sitting beside it.
        var sensEstimates = new[] { flickSens, quickFlickSens, trackingSens }.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var adsSensEstimates = new[] { adsFlickSens, adsQuickFlickSens }.Where(v => v.HasValue).Select(v => v!.Value).ToList();

        var lines = new List<string>();
        double? combinedSens = null;

        if (sensEstimates.Count == 0)
        {
            lines.Add("Could not compute a recommended sensitivity from either test.");
        }
        else
        {
            // Rounded to 2 decimal places here (not just at display time) since
            // Apex's sensitivity field doesn't accept finer precision than that
            // (e.g. 1.523 gets truncated to 1.52) - every downstream use of this
            // value (cm/360 below, the simulator, persisted settings) should
            // reflect the number the player will actually type in, not the
            // unrounded average.
            combinedSens = Math.Round(sensEstimates.Average(), 2);
            var cm360 = 360.0 * 2.54 / (_mouseDpi!.Value * combinedSens.Value * ApexLegendsProfile.MYaw);
            lines.Add($"Recommended Apex Legends sensitivity: {combinedSens:F2}");
            lines.Add($"(~{cm360:F1} cm/360 at {_mouseDpi:F0} DPI)");
        }

        // Same averaging idea on the ADS side: combine whichever of ADS /
        // ADS quick flick succeeded into one sensitivity before taking the
        // ratio, instead of only ever comparing the original two trial types.
        double? flickMultiplier = combinedSens.HasValue && adsSensEstimates.Count > 0
            ? Math.Round(adsSensEstimates.Average() / combinedSens.Value, 2)
            : null;

        if (flickMultiplier == null)
        {
            lines.Add("Could not compute a recommended ADS sensitivity multiplier.");
        }
        else
        {
            lines.Add($"Recommended ADS sensitivity multiplier: {flickMultiplier:F2}");
        }

        // Stashed for the "Try It in 3D" button below and persisted so the
        // simulator can also be opened straight from the start screen later,
        // without needing a fresh calibration run first.
        _lastCombinedSensitivity = combinedSens;
        _lastAdsMultiplier = flickMultiplier;
        TrySimulatorButton.IsEnabled = combinedSens.HasValue;
        new UserSettings(_horizontalFovDegrees, _mouseDpi ?? 800.0, ClickVolumeSlider.Value / 100.0, _totalTrialCount, combinedSens, flickMultiplier).Save();

        var straightnessValues = hipfireFlick.StraightnessStats.RawValues
            .Concat(adsFlick.StraightnessStats.RawValues)
            .Concat(quickFlick.StraightnessStats.RawValues)
            .Concat(adsQuickFlick.StraightnessStats.RawValues)
            .ToList();
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
        sb.AppendLine($"  Flick-based:    sensitivity={flickSens?.ToString("F2") ?? "n/a"}  ADS multiplier={flickMultiplier?.ToString("F2") ?? "n/a"}");
        sb.AppendLine($"  Quick flick:    sensitivity={quickFlickSens?.ToString("F2") ?? "n/a"}");
        sb.AppendLine($"  Tracking-based: sensitivity={trackingSens?.ToString("F2") ?? "n/a"}");
        sb.AppendLine();
        sb.AppendLine("=== Flick test: Hipfire ===");
        AppendSessionStats(sb, hipfireFlick);
        sb.AppendLine();
        sb.AppendLine("=== Flick test: ADS (right mouse held) ===");
        AppendSessionStats(sb, adsFlick);
        sb.AppendLine();
        var quickFlickDeadlineLabel = $"{QuickFlickTimerDuration.TotalSeconds:0.##}s deadline per target";
        sb.AppendLine($"=== Flick test: Quick Flick ({quickFlickDeadlineLabel}) ===");
        AppendSessionStats(sb, quickFlick);
        sb.AppendLine();
        sb.AppendLine($"=== Flick test: ADS Quick Flick (right mouse held, {quickFlickDeadlineLabel}) ===");
        AppendSessionStats(sb, adsQuickFlick);
        sb.AppendLine();
        sb.AppendLine("=== Tracking test ===");
        AppendTrackingSessionStats(sb, trackingSummary, _trackingResults);
        sb.AppendLine();
        sb.AppendLine("=== Strafe test (side-to-side, diagnostic only - not used in the recommendation) ===");
        AppendStrafeSessionStats(sb, _strafeResults);

        StatsText.Text = sb.ToString();

        ResultHistoryStore.Add(new ResultHistoryEntry(
            DateTimeOffset.Now,
            _horizontalFovDegrees,
            _mouseDpi ?? 800.0,
            _totalTrialCount,
            _includePerpendicularAxisData,
            RecommendationText.Text,
            StatsText.Text,
            combinedSens,
            flickMultiplier));
        RefreshResultHistoryDisplay();

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

    // Reports how the user compensates specifically at direction changes -
    // none of this feeds the headline recommendation, it's purely a "are you
    // over/under-correcting on reversals" diagnostic.
    private static void AppendStrafeSessionStats(StringBuilder sb, IReadOnlyList<TrackingResult> results)
    {
        if (results.Count == 0)
        {
            sb.AppendLine("No strafe trials completed.");
            return;
        }

        var totalDuration = results.Sum(r => r.DurationSeconds);
        var allLags = results.SelectMany(r => r.ReversalLags).ToList();
        var allOvershoots = results.SelectMany(r => r.ReversalOvershoots).ToList();

        sb.AppendLine($"{results.Count} strafe trials, {totalDuration:F1}s total held, {allLags.Count} direction changes observed");

        if (allLags.Count == 0)
        {
            sb.AppendLine("Not enough direction changes recorded to judge compensation.");
            return;
        }

        var avgLagMs = allLags.Average() * 1000.0;
        var avgOvershootCounts = allOvershoots.Average();
        var avgCountsPerDegree = results.Where(r => r.CountsPerDegreeHorizontal > 0)
            .Select(r => r.CountsPerDegreeHorizontal).DefaultIfEmpty(0).Average();
        var avgOvershootDegrees = avgCountsPerDegree > 0 ? avgOvershootCounts / avgCountsPerDegree : 0;

        sb.AppendLine($"Average reaction lag on direction change: {avgLagMs:F0}ms");
        sb.AppendLine($"Average overshoot before correcting:      {avgOvershootDegrees:F2} degrees ({avgOvershootCounts:F0} raw counts)");
        sb.AppendLine(avgLagMs < 200
            ? "You're catching direction changes quickly with little overshoot."
            : "You're lagging noticeably behind direction changes - a touch more sensitivity or some focused practice on anticipating reversals could help.");
    }

    // Computes both axes unconditionally (not just the trial's own dominant
    // axis) so the perpendicular-axis toggle has a real angular gap to work
    // with - every trial has incidental off-axis movement too, this just
    // measures it the same way as the dominant axis instead of only as drift.
    // isAds: ADS targets live in a centered sub-frame (Width/AdsZoomScale x
    // Height/AdsZoomScale, see OnBeginClicked) that gets visually zoomed back
    // up to fill the screen - so the FOV math has to project through that
    // sub-frame's own dimensions and a correspondingly narrowed FOV (same
    // zoom-narrows-FOV model as Simulation/ScopeOption.ScopedFov), not the
    // full screen/FOV, or the implied angle comes out too small for the gap
    // the player actually perceived.
    private (double Dominant, double Perpendicular) ComputeImpliedDegrees(TrialDefinition definition, bool isAds)
    {
        var fov = isAds ? _horizontalFovDegrees / AdsZoomScale : _horizontalFovDegrees;
        var width = isAds ? _adsFrameWidth : Width;
        var height = isAds ? _adsFrameHeight : Height;
        var originX = isAds ? _adsFrameOriginX : 0.0;
        var originY = isAds ? _adsFrameOriginY : 0.0;

        var horizontalDegrees = FieldOfViewProjection.DegreesBetween(
            definition.TargetAPosition.X - originX, definition.TargetBPosition.X - originX, width, fov);
        var verticalFov = FieldOfViewProjection.DeriveVerticalFov(fov, width, height);
        var verticalDegrees = FieldOfViewProjection.DegreesBetween(
            definition.TargetAPosition.Y - originY, definition.TargetBPosition.Y - originY, height, verticalFov);

        var isHorizontal = definition.Direction is Direction.LeftToRight or Direction.RightToLeft;
        return isHorizontal ? (horizontalDegrees, verticalDegrees) : (verticalDegrees, horizontalDegrees);
    }

    // Always zooms in on screen center by AdsZoomScale - safe because ADS
    // targets are confined (at session start, see OnBeginClicked) to a
    // centered frame exactly that scale's inverse, so both targets are
    // always already within the post-zoom visible area.
    private void BeginAdsZoomIn()
    {
        var centerX = Width / 2.0;
        var centerY = Height / 2.0;
        TrialCanvasZoomTransform.CenterX = centerX;
        TrialCanvasZoomTransform.CenterY = centerY;
        TrialCanvasZoomTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(TrialCanvasZoomTransform.ScaleX, AdsZoomScale, AdsZoomDuration));
        TrialCanvasZoomTransform.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(TrialCanvasZoomTransform.ScaleY, AdsZoomScale, AdsZoomDuration));
    }

    // Zooms back out to the full screen, then runs onCompleted - used so a
    // trial restart or slot advance only happens once the screen has
    // visually returned to the unzoomed view.
    private void BeginAdsZoomOut(Action onCompleted)
    {
        if (TrialCanvasZoomTransform.ScaleX == 1.0 && TrialCanvasZoomTransform.ScaleY == 1.0)
        {
            onCompleted();
            return;
        }

        var xAnim = new DoubleAnimation(TrialCanvasZoomTransform.ScaleX, 1.0, AdsZoomDuration);
        xAnim.Completed += (_, _) => onCompleted();
        TrialCanvasZoomTransform.BeginAnimation(ScaleTransform.ScaleXProperty, xAnim);
        TrialCanvasZoomTransform.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(TrialCanvasZoomTransform.ScaleY, 1.0, AdsZoomDuration));
    }

    // The border is a scaled-down (same aspect ratio) outline of the full
    // screen, centered on screen center - the same anchor BeginAdsZoomIn
    // uses - so it visibly expands to fill the screen edges as the canvas
    // zooms in. Stays visible for the whole ADS trial (zoomed in or out).
    private void ShowAdsZoomPreviewBorder()
    {
        var boxWidth = Width / AdsZoomScale;
        var boxHeight = Height / AdsZoomScale;
        AdsZoomPreviewBorder.Width = boxWidth;
        AdsZoomPreviewBorder.Height = boxHeight;

        Canvas.SetLeft(AdsZoomPreviewBorder, (Width - boxWidth) / 2.0);
        Canvas.SetTop(AdsZoomPreviewBorder, (Height - boxHeight) / 2.0);
        AdsZoomPreviewBorder.Visibility = Visibility.Visible;
    }

    private void HideAdsZoomPreviewBorder()
    {
        AdsZoomPreviewBorder.Visibility = Visibility.Collapsed;
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
        var targetDiameter = _isAdsPhase ? (DefaultTargetDiameter / AdsZoomScale) : DefaultTargetDiameter;
        TargetEllipse.Width = targetDiameter;
        TargetEllipse.Height = targetDiameter;
        TargetBEllipse.Width = targetDiameter;
        TargetBEllipse.Height = targetDiameter;

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
        ProgressText.Text = $"{prefix} {_slotIndex + 1} / {_totalTrialCount}";

        if (_isAdsPhase)
        {
            ShowAdsZoomPreviewBorder();
        }
        else
        {
            HideAdsZoomPreviewBorder();
        }
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

    private void ShowSecondClickPrompt()
    {
        var colorWord = _isAdsPhase ? "magenta" : "blue";
        var baseText = $"Click the {colorWord} target";
        PromptText.Text = _isAdsPhase ? $"Hold RMB - {baseText}" : baseText;
        PromptSubText.Text = "This is a feel test, not a sight test - don't try to peek. Your cursor re-centers once you click, so you won't see where it actually landed.";
        PromptPanel.Visibility = Visibility.Visible;
    }

    // Dispatches to the kind of trial the mixed-pattern sequence calls for
    // next - the four trial types are interleaved within one session, not
    // run as separate phases, so each slot just swaps which UI is active.
    private void PresentSlot(int index)
    {
        var kind = _kindPattern[index];
        switch (kind)
        {
            case TrialKind.Tracking:
                EnterTrackingSlot();
                break;
            case TrialKind.Strafe:
                EnterStrafeSlot();
                break;
            case TrialKind.QuickFlick:
                EnterQuickFlickSlot(isAds: false);
                break;
            case TrialKind.AdsQuickFlick:
                EnterQuickFlickSlot(isAds: true);
                break;
            default:
                EnterFlickSlot(isAds: kind == TrialKind.Ads);
                break;
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

    private void EnterQuickFlickSlot(bool isAds)
    {
        _quickFlickActive = true;
        _quickFlickIsAds = isAds;
        _isAdsPhase = isAds;
        _adsRightMouseDown = false;
        _controller = null;
        _runner = null;

        TrackingPanel.Visibility = Visibility.Collapsed;
        if (TrialPanel.Visibility != Visibility.Visible)
        {
            FadeIn(TrialPanel);
        }

        StartNewQuickFlickAttempt();
    }

    // Generates a fresh 3-point chain (P1 untimed, P2/P3 each under their own
    // deadline) and presents only the first, untimed target - called both to
    // start a slot and to retry it after a miss, since a miss regenerates
    // every position rather than just resetting state.
    private void StartNewQuickFlickAttempt()
    {
        var placement = _quickFlickIsAds ? _adsQuickFlickPlacement! : _quickFlickPlacement!;
        var width = _quickFlickIsAds ? _adsFrameWidth : Width;
        var height = _quickFlickIsAds ? _adsFrameHeight : Height;

        var p1 = placement.GenerateRandomPoint(width, height);
        var (p2, dirA) = placement.GenerateNextPoint(p1, width, height);
        var (p3, dirB) = placement.GenerateNextPoint(p2, width, height);

        _quickFlickDefinitionA = new TrialDefinition(_slotIndex, dirA, p1, p2);
        _quickFlickDefinitionB = new TrialDefinition(_slotIndex, dirB, p2, p3);
        _quickFlickSegment = 1;
        _quickFlickPendingResultA = null;
        _runner = new TrialRunner(_accumulator, _quickFlickDefinitionA);

        PresentQuickFlickAttempt();
    }

    private void PresentQuickFlickAttempt()
    {
        var targetDiameter = _quickFlickIsAds ? (DefaultTargetDiameter / AdsZoomScale) : DefaultTargetDiameter;
        TargetEllipse.Width = targetDiameter;
        TargetEllipse.Height = targetDiameter;
        TargetBEllipse.Width = targetDiameter;
        TargetBEllipse.Height = targetDiameter;
        TargetBGhostEllipse.Width = targetDiameter;
        TargetBGhostEllipse.Height = targetDiameter;

        TargetEllipse.Fill = _quickFlickIsAds ? AdsQuickFlickActiveBrush : QuickFlickActiveBrush;
        ((DropShadowEffect)TargetEllipse.Effect).Color = _quickFlickIsAds ? AdsQuickFlickGlowColor : QuickFlickGlowColor;
        RepositionTargetA(_quickFlickDefinitionA!.TargetAPosition);
        TargetEllipse.Visibility = Visibility.Visible;

        TargetBEllipse.Fill = _quickFlickIsAds ? AdsQuickFlickActiveBrush : QuickFlickActiveBrush;
        ((DropShadowEffect)TargetBEllipse.Effect).Color = _quickFlickIsAds ? AdsQuickFlickGlowColor : QuickFlickGlowColor;
        TargetBEllipse.Visibility = Visibility.Collapsed;
        TargetBGhostEllipse.Fill = _quickFlickIsAds ? AdsQuickFlickGhostBrush : QuickFlickGhostBrush;
        HideQuickFlickCountdown();

        Cursor = Cursors.Arrow;
        PromptText.Text = _quickFlickIsAds ? "Hold right mouse, then click the target" : "Click the target";
        PromptSubText.Text = string.Empty;
        PromptPanel.Visibility = Visibility.Visible;

        var prefix = _quickFlickIsAds ? "ADS Quick Flick Trial" : "Quick Flick Trial";
        ProgressText.Text = $"{prefix} {_slotIndex + 1} / {_totalTrialCount}";

        if (_quickFlickIsAds)
        {
            ShowAdsZoomPreviewBorder();
        }
        else
        {
            HideAdsZoomPreviewBorder();
        }
    }

    private void HandleQuickFlickClick(MouseButtonEventArgs e)
    {
        if (_quickFlickIsAds && !_adsRightMouseDown)
        {
            return;
        }

        switch (_runner!.State)
        {
            case TrialState.AwaitingTargetAClick:
            {
                var clickPosition = e.GetPosition(TrialCanvas);
                var hitRadius = _quickFlickIsAds ? (TargetHitRadius / AdsZoomScale) : TargetHitRadius;
                if (!IsWithinTarget(clickPosition, _runner.Definition.TargetAPosition, hitRadius))
                {
                    return;
                }

                ClickSoundPlayer.PlayClick();
                _runner.OnTargetAClicked();
                Cursor = Cursors.None;
                DimQuickFlickTargetA();
                ShowTargetB(_runner.Definition.TargetBPosition);
                ShowQuickFlickGhost(_runner.Definition.TargetBPosition);
                ArmQuickFlickTimer();
                StartQuickFlickShrinkAnimation();
                ShowQuickFlickSecondClickPrompt();
                break;
            }

            case TrialState.AwaitingFeelClick:
            {
                // No hit-test - this is a blind "feel" click exactly like
                // Hipfire/ADS today; the deadline (not a missed click) is
                // what fails this stage.
                ClickSoundPlayer.PlayClick();
                DisarmQuickFlickTimer();
                HideQuickFlickCountdown();
                var (impliedDegrees, perpendicularImpliedDegrees) = ComputeImpliedDegrees(_runner.Definition, _quickFlickIsAds);
                _runner.OnFeelClicked(impliedDegrees, perpendicularImpliedDegrees);
                CenterCursor();

                if (_quickFlickSegment == 1)
                {
                    _quickFlickPendingResultA = _runner.Result!;
                    _quickFlickSegment = 2;
                    RepositionTargetA(_quickFlickDefinitionB!.TargetAPosition);
                    _runner = new TrialRunner(_accumulator, _quickFlickDefinitionB);
                    _runner.OnTargetAClicked();
                    ShowTargetB(_quickFlickDefinitionB.TargetBPosition);
                    ShowQuickFlickGhost(_quickFlickDefinitionB.TargetBPosition);
                    ArmQuickFlickTimer();
                    StartQuickFlickShrinkAnimation();
                }
                else
                {
                    CompleteQuickFlickTrial(_quickFlickPendingResultA!, _runner.Result!);
                }

                break;
            }
        }
    }

    private void RepositionTargetA(ScreenPoint position)
    {
        Canvas.SetLeft(TargetEllipse, position.X - (TargetEllipse.Width / 2));
        Canvas.SetTop(TargetEllipse, position.Y - (TargetEllipse.Height / 2));
    }

    private void DimQuickFlickTargetA()
    {
        TargetEllipse.Fill = _quickFlickIsAds ? AdsQuickFlickDimmedBrush : QuickFlickDimmedBrush;
    }

    private void ShowQuickFlickSecondClickPrompt()
    {
        PromptText.Text = _quickFlickIsAds ? "Hold RMB - quick, flick to it!" : "Quick - flick to it!";
        PromptSubText.Text = $"You have {QuickFlickTimerDuration.TotalSeconds:0.##}s - the full faint circle is clickable the whole time, the bright highlight shrinking is just the timer. You won't see your cursor, so commit to the gap you see and go.";
        PromptPanel.Visibility = Visibility.Visible;
    }

    private void ArmQuickFlickTimer()
    {
        _quickFlickTimer = new DispatcherTimer { Interval = QuickFlickTimerDuration };
        _quickFlickTimer.Tick += OnQuickFlickTimerExpired;
        _quickFlickTimer.Start();
    }

    private void DisarmQuickFlickTimer()
    {
        if (_quickFlickTimer == null)
        {
            return;
        }

        _quickFlickTimer.Stop();
        _quickFlickTimer.Tick -= OnQuickFlickTimerExpired;
        _quickFlickTimer = null;
    }

    private void OnQuickFlickTimerExpired(object? sender, EventArgs e)
    {
        DisarmQuickFlickTimer();

        if (_quickFlickIsAds)
        {
            BeginAdsZoomOut(RestartQuickFlickTrial);
        }
        else
        {
            RestartQuickFlickTrial();
        }
    }

    // No partial credit on a miss - a slot only ever contributes data to
    // _quickFlickResults/_adsQuickFlickResults once it fully succeeds, so a
    // player needing many retries doesn't end up over-represented in the
    // sample relative to one who clears it on the first try.
    private void RestartQuickFlickTrial()
    {
        CenterCursor();
        Cursor = Cursors.Arrow;
        HidePrompt();
        HideResultFeedback();
        TargetBEllipse.Visibility = Visibility.Collapsed;
        HideQuickFlickCountdown();
        _quickFlickPendingResultA = null;

        StartNewQuickFlickAttempt();
    }

    private void CompleteQuickFlickTrial(TrialResult resultA, TrialResult resultB)
    {
        var kind = _quickFlickIsAds ? TrialKind.AdsQuickFlick : TrialKind.QuickFlick;
        var resultsList = _quickFlickIsAds ? _adsQuickFlickResults : _quickFlickResults;
        resultsList.Add(resultA);
        resultsList.Add(resultB);
        _tileData.Add(new QuickFlickTile(kind, _quickFlickDefinitionA!, resultA, _quickFlickDefinitionB!, resultB));

        CenterCursor();
        Cursor = Cursors.Arrow;
        HidePrompt();
        HideResultFeedback();
        TargetEllipse.Visibility = Visibility.Collapsed;
        TargetBEllipse.Visibility = Visibility.Collapsed;
        HideQuickFlickCountdown();
        _quickFlickActive = false;
        _quickFlickPendingResultA = null;

        if (_quickFlickIsAds)
        {
            BeginAdsZoomOut(AdvanceToNextSlot);
        }
        else
        {
            AdvanceToNextSlot();
        }
    }

    // The deadline itself is purely time-based (there's no hit-test to
    // "beat" on the blind click) - TargetBEllipse shrinking is just the
    // visual cue of that same deadline, timed to finish exactly when it
    // fires. TargetBGhostEllipse is the actual full-size clickable area,
    // shown the whole time on top of it, so the shrink reads as a countdown
    // highlight rather than the real target disappearing.
    private void StartQuickFlickShrinkAnimation()
    {
        TargetBEllipseScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.0, 0.0, QuickFlickTimerDuration));
        TargetBEllipseScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.0, 0.0, QuickFlickTimerDuration));
    }

    private void ResetQuickFlickShrinkAnimation()
    {
        TargetBEllipseScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        TargetBEllipseScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        TargetBEllipseScaleTransform.ScaleX = 1.0;
        TargetBEllipseScaleTransform.ScaleY = 1.0;
    }

    private void ShowQuickFlickGhost(ScreenPoint position)
    {
        Canvas.SetLeft(TargetBGhostEllipse, position.X - (TargetBGhostEllipse.Width / 2));
        Canvas.SetTop(TargetBGhostEllipse, position.Y - (TargetBGhostEllipse.Height / 2));
        TargetBGhostEllipse.Visibility = Visibility.Visible;
    }

    private void HideQuickFlickGhost()
    {
        TargetBGhostEllipse.Visibility = Visibility.Collapsed;
    }

    private void HideQuickFlickCountdown()
    {
        ResetQuickFlickShrinkAnimation();
        HideQuickFlickGhost();
    }

    private void EnterTrackingSlot()
    {
        _activeHoldKind = TrialKind.Tracking;
        ApplyHoldVisualStyle(
            TrackingTargetBrush, TrackingGlowColor,
            "Click and hold the orange target",
            "Keep holding and track it by feel - your cursor will be hidden");

        TrialPanel.Visibility = Visibility.Collapsed;
        if (TrackingPanel.Visibility != Visibility.Visible)
        {
            FadeIn(TrackingPanel);
        }

        ProgressText.Text = $"Tracking Trial {_slotIndex + 1} / {_totalTrialCount}";
        BeginTrackingAttempt();
    }

    // Same hold mechanic as plain tracking, just a different motion model
    // (side-to-side strafing rather than free-roam wander) aimed at a
    // different diagnostic: how the user compensates when the target
    // suddenly reverses direction, rather than overall tracking smoothness.
    private void EnterStrafeSlot()
    {
        _activeHoldKind = TrialKind.Strafe;
        ApplyHoldVisualStyle(
            StrafeTargetBrush, StrafeGlowColor,
            "Click and hold the green target",
            "It strafes side to side and can reverse anytime once it's committed to a direction for a moment - keep holding and follow it by feel");

        TrialPanel.Visibility = Visibility.Collapsed;
        if (TrackingPanel.Visibility != Visibility.Visible)
        {
            FadeIn(TrackingPanel);
        }

        ProgressText.Text = $"Strafe Trial {_slotIndex + 1} / {_totalTrialCount}";
        BeginTrackingAttempt();
    }

    private void ApplyHoldVisualStyle(Brush targetBrush, Color glowColor, string heading, string subtext)
    {
        TrackingTargetEllipse.Fill = targetBrush;
        ((DropShadowEffect)TrackingTargetEllipse.Effect).Color = glowColor;
        TrackingInstructionHeading.Text = heading;
        TrackingInstructionSubtext.Text = subtext;
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
        if (_activeHoldKind == TrialKind.Strafe)
        {
            var strafeMotion = new StrafeMotionController(new Random(), Width, Height, startPosition);
            _activeStrafeController = strafeMotion;
            _trackingMotion = strafeMotion;
        }
        else
        {
            _activeStrafeController = null;
            _trackingMotion = new TrackingMotionController(new Random(), Width, Height, startPosition);
        }

        PositionTrackingTarget(startPosition);
        _awaitingTrackingPress = true;
        StartTrackingPreview();
    }

    // Briefly previews where the target is about to head before the user has
    // committed to a press, so the first real movement isn't a total
    // surprise - a dim ghost slides from the target's current position along
    // its current heading and fades out, looping until the hold begins.
    private void StartTrackingPreview()
    {
        var start = _trackingMotion!.Position;
        var ahead = _trackingMotion.PeekAhead(TrackingPreviewAheadSeconds);
        var duration = TimeSpan.FromSeconds(TrackingPreviewAheadSeconds);

        TrackingPreviewGhost.Fill = TrackingTargetEllipse.Fill;
        TrackingPreviewGhost.Visibility = Visibility.Visible;

        var leftAnim = new DoubleAnimation(start.X - (TrackingPreviewGhost.Width / 2), ahead.X - (TrackingPreviewGhost.Width / 2), duration)
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
        var topAnim = new DoubleAnimation(start.Y - (TrackingPreviewGhost.Height / 2), ahead.Y - (TrackingPreviewGhost.Height / 2), duration)
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
        var opacityAnim = new DoubleAnimation(TrackingPreviewStartOpacity, 0.0, duration)
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };

        TrackingPreviewGhost.BeginAnimation(Canvas.LeftProperty, leftAnim);
        TrackingPreviewGhost.BeginAnimation(Canvas.TopProperty, topAnim);
        TrackingPreviewGhost.BeginAnimation(OpacityProperty, opacityAnim);
    }

    private void StopTrackingPreview()
    {
        TrackingPreviewGhost.BeginAnimation(Canvas.LeftProperty, null);
        TrackingPreviewGhost.BeginAnimation(Canvas.TopProperty, null);
        TrackingPreviewGhost.BeginAnimation(OpacityProperty, null);
        TrackingPreviewGhost.Visibility = Visibility.Collapsed;
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

    private static bool IsWithinTarget(Point click, ScreenPoint target, double radius)
    {
        var dx = click.X - target.X;
        var dy = click.Y - target.Y;
        return dx * dx + dy * dy <= radius * radius;
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
