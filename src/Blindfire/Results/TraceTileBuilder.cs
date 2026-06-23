using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Blindfire.Tracking;
using Blindfire.Trials;

namespace Blindfire.Results;

// Builds one small "tile" visual per trial for the results-screen background:
// the trial's targets plus its recorded mouse trace, fitted into a tile-sized
// rectangle. Dimmed by default, brightens and grows on hover.
public static class TraceTileBuilder
{
    private const int MaxRenderedTracePoints = 150;
    private const double InnerPadding = 8.0;
    private const double MarkerDiameter = 8.0;
    private const double HoverScale = 1.25;
    private const double DimmedOpacity = 0.35;
    private static readonly TimeSpan HoverAnimationDuration = TimeSpan.FromMilliseconds(150);

    private static readonly Color HipfireAColor = Color.FromRgb(0xFF, 0x5C, 0x66);
    private static readonly Color HipfireBColor = Color.FromRgb(0x3D, 0xA9, 0xFC);
    private static readonly Color AdsAColor = Color.FromRgb(0xB1, 0x4E, 0xFF);
    private static readonly Color AdsBColor = Color.FromRgb(0xFF, 0x4F, 0xD8);
    private static readonly Color QuickFlickColor = Color.FromRgb(0xFF, 0xD2, 0x3F);
    private static readonly Color AdsQuickFlickColor = Color.FromRgb(0x00, 0xE5, 0xFF);
    private static readonly Color TrackingTargetColor = Color.FromRgb(0xFF, 0xB0, 0x20);
    private static readonly Color StrafeTargetColor = Color.FromRgb(0x2E, 0xCC, 0x71);
    private static readonly Color TraceLineColor = Colors.White;

    private sealed record TraceLine(IReadOnlyList<Point> Points, Color Color);

    private sealed record Marker(Point Position, Color Color);

    // MissDistancePixels is the on-screen distance, in the same pixel space
    // the trace itself is drawn in, between where the trace actually ends and
    // where it should have ended (Target B for a flick, the target's final
    // position for a hold) - shown as a label so a glance at any tile
    // explains whether that trial's contribution to the session's
    // CountsPerDegree median was a clean data point or a noisy one.
    private sealed record TileGeometry(List<TraceLine> Lines, List<Marker> Markers, double MissDistancePixels);

    // Builds every tile in one pass so flick tiles (hipfire/ADS) can share a
    // single pixel scale - independently fitting each tile to its own box
    // would zoom a small ADS gap and a large hipfire gap to look the same
    // size, hiding the very thing this view is meant to show.
    public static IReadOnlyList<Border> BuildAll(
        IReadOnlyList<ITrialTile> tiles, IReadOnlyList<Rect> placements, double screenWidth, double screenHeight,
        double horizontalFovDegrees, double verticalFovDegrees)
    {
        var count = Math.Min(tiles.Count, placements.Count);
        var geometries = new TileGeometry[count];
        for (var i = 0; i < count; i++)
        {
            var (lines, markers, missDistance) = tiles[i] switch
            {
                FlickTile flick => BuildFlickGeometry(flick),
                QuickFlickTile quickFlick => BuildQuickFlickGeometry(quickFlick),
                TrackingTile tracking => BuildHoldGeometry(tracking.Result, TrackingTargetColor, screenWidth, screenHeight, horizontalFovDegrees, verticalFovDegrees),
                StrafeTile strafe => BuildHoldGeometry(strafe.Result, StrafeTargetColor, screenWidth, screenHeight, horizontalFovDegrees, verticalFovDegrees),
                _ => (new List<TraceLine>(), new List<Marker>(), 0.0),
            };
            geometries[i] = new TileGeometry(lines, markers, missDistance);
        }

        var flickScale = ComputeSharedFlickScale(tiles, geometries, placements, count);

        var borders = new Border[count];
        for (var i = 0; i < count; i++)
        {
            var isFlick = tiles[i].Kind is TrialKind.Hipfire or TrialKind.Ads or TrialKind.QuickFlick or TrialKind.AdsQuickFlick;
            var transform = isFlick ? BuildCenteredTransform(geometries[i], placements[i], flickScale) : null;
            borders[i] = BuildVisual(placements[i], geometries[i].Lines, geometries[i].Markers, transform, geometries[i].MissDistancePixels);
        }

        return borders;
    }

    // Anchors the trace at Target A and scales it uniformly so the dominant
    // axis lands exactly on Target B (we know the real on-screen gap, so no
    // FOV math is needed) - any perpendicular offset at the trace's end is
    // the same thing TrialResult.PerpendicularDrift already measures.
    private static (List<TraceLine>, List<Marker>, double) BuildFlickGeometry(FlickTile flick)
    {
        var definition = flick.Definition;
        var (line, missDistance) = BuildFlickTraceLine(definition, flick.Result);
        var (aColor, bColor) = flick.Kind == TrialKind.Ads ? (AdsAColor, AdsBColor) : (HipfireAColor, HipfireBColor);

        var lines = new List<TraceLine> { line };
        var markers = new List<Marker>
        {
            new(new Point(definition.TargetAPosition.X, definition.TargetAPosition.Y), aColor),
            new(new Point(definition.TargetBPosition.X, definition.TargetBPosition.Y), bColor),
        };

        return (lines, markers, missDistance);
    }

    // Quick flick's chain is two flicks back to back (P1->P2->P3) sharing one
    // tile - same trace math as a regular flick, run twice and anchored to
    // each segment's own start point, with all 3 targets marked.
    private static (List<TraceLine>, List<Marker>, double) BuildQuickFlickGeometry(QuickFlickTile quickFlick)
    {
        var color = quickFlick.Kind == TrialKind.AdsQuickFlick ? AdsQuickFlickColor : QuickFlickColor;
        var (lineA, missA) = BuildFlickTraceLine(quickFlick.DefinitionA, quickFlick.ResultA);
        var (lineB, missB) = BuildFlickTraceLine(quickFlick.DefinitionB, quickFlick.ResultB);

        var p1 = quickFlick.DefinitionA.TargetAPosition;
        var p2 = quickFlick.DefinitionA.TargetBPosition;
        var p3 = quickFlick.DefinitionB.TargetBPosition;

        var lines = new List<TraceLine> { lineA, lineB };
        var markers = new List<Marker>
        {
            new(new Point(p1.X, p1.Y), color),
            new(new Point(p2.X, p2.Y), color),
            new(new Point(p3.X, p3.Y), color),
        };

        return (lines, markers, (missA + missB) / 2.0);
    }

    private static (TraceLine Line, double MissDistance) BuildFlickTraceLine(TrialDefinition definition, TrialResult result)
    {
        var isHorizontal = definition.Direction is Direction.LeftToRight or Direction.RightToLeft;

        var dominantRaw = isHorizontal ? result.RawDx : result.RawDy;
        var gap = isHorizontal
            ? definition.TargetBPosition.X - definition.TargetAPosition.X
            : definition.TargetBPosition.Y - definition.TargetAPosition.Y;
        var scale = dominantRaw != 0 ? gap / dominantRaw : 1.0;

        var tracePoints = Decimate(result.Trace, MaxRenderedTracePoints)
            .Select(p => new Point(definition.TargetAPosition.X + (scale * p.X), definition.TargetAPosition.Y + (scale * p.Y)))
            .ToList();

        var targetB = new Point(definition.TargetBPosition.X, definition.TargetBPosition.Y);
        var finalPoint = tracePoints.Count > 0 ? tracePoints[^1] : new Point(definition.TargetAPosition.X, definition.TargetAPosition.Y);
        var missDistance = Distance(finalPoint, targetB);

        return (new TraceLine(tracePoints, TraceLineColor), missDistance);
    }

    // The target's path is already absolute screen pixels - used as-is. The
    // user's mouse path has no fixed anchor like the flick trials do, so it's
    // approximated: raw counts -> degrees via this trial's own aggregate
    // counts-per-degree, degrees -> pixels via a flat (non-FOV-projected)
    // scale, anchored at the target's starting position. Good for comparing
    // shape/lag, not a calibrated overlay.
    private static (List<TraceLine>, List<Marker>, double) BuildHoldGeometry(
        TrackingResult result, Color targetColor, double screenWidth, double screenHeight, double horizontalFovDegrees, double verticalFovDegrees)
    {
        var targetPoints = Decimate(result.Target, MaxRenderedTracePoints)
            .Select(p => new Point(p.X, p.Y))
            .ToList();

        var start = result.Target.Count > 0 ? result.Target[0] : new ScreenPoint(0, 0);
        var pixelsPerDegreeX = screenWidth / horizontalFovDegrees;
        var pixelsPerDegreeY = screenHeight / verticalFovDegrees;
        var pixelsPerCountX = result.CountsPerDegreeHorizontal > 0 ? pixelsPerDegreeX / result.CountsPerDegreeHorizontal : 0;
        var pixelsPerCountY = result.CountsPerDegreeVertical > 0 ? pixelsPerDegreeY / result.CountsPerDegreeVertical : 0;

        var mousePoints = Decimate(result.MouseTrace, MaxRenderedTracePoints)
            .Select(p => new Point(start.X + (p.X * pixelsPerCountX), start.Y + (p.Y * pixelsPerCountY)))
            .ToList();

        var lines = new List<TraceLine>
        {
            new(targetPoints, targetColor),
            new(mousePoints, TraceLineColor),
        };
        var markers = new List<Marker> { new(new Point(start.X, start.Y), targetColor) };

        // Both traces are sampled on different, unsynchronized clocks
        // (target ticks vs. raw input events), so only their endpoints are
        // anchored to the same instant (the moment the hold ended) - an
        // index-paired average across the whole decimated trace would
        // compare positions from different times and not mean anything.
        var finalTarget = targetPoints.Count > 0 ? targetPoints[^1] : new Point(start.X, start.Y);
        var finalMouse = mousePoints.Count > 0 ? mousePoints[^1] : new Point(start.X, start.Y);
        var missDistance = Distance(finalTarget, finalMouse);

        return (lines, markers, missDistance);
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    // One scale for every flick tile: the largest real A-to-B extent among
    // them is sized to just fit the smallest flick tile's box, so every
    // other (smaller-gap) trial renders proportionally smaller within its
    // own tile - that's what makes ADS's tighter gap visibly read as tighter.
    private static double ComputeSharedFlickScale(IReadOnlyList<ITrialTile> tiles, TileGeometry[] geometries, IReadOnlyList<Rect> placements, int count)
    {
        var maxExtent = 0.0;
        var minTileSize = double.MaxValue;

        for (var i = 0; i < count; i++)
        {
            if (tiles[i].Kind is not (TrialKind.Hipfire or TrialKind.Ads or TrialKind.QuickFlick or TrialKind.AdsQuickFlick))
            {
                continue;
            }

            var extent = BoundingExtent(geometries[i]);
            if (extent == null)
            {
                continue;
            }

            maxExtent = Math.Max(maxExtent, Math.Max(extent.Value.Width, extent.Value.Height));
            minTileSize = Math.Min(minTileSize, Math.Min(placements[i].Width, placements[i].Height));
        }

        if (maxExtent <= 0 || minTileSize == double.MaxValue)
        {
            return 1.0;
        }

        var available = Math.Max(1, minTileSize - (2 * InnerPadding));
        return available / Math.Max(1, maxExtent);
    }

    private static Func<Point, Point> BuildCenteredTransform(TileGeometry geometry, Rect placement, double scale)
    {
        var extent = BoundingExtent(geometry);
        if (extent == null)
        {
            return p => p;
        }

        var (minX, minY, _, _) = extent.Value;
        var centerX = minX + (extent.Value.Width / 2);
        var centerY = minY + (extent.Value.Height / 2);

        var tileCenterX = placement.Width / 2;
        var tileCenterY = placement.Height / 2;

        return p => new Point(tileCenterX + ((p.X - centerX) * scale), tileCenterY + ((p.Y - centerY) * scale));
    }

    private static (double MinX, double MinY, double Width, double Height)? BoundingExtent(TileGeometry geometry)
    {
        var allPoints = geometry.Lines.SelectMany(l => l.Points).Concat(geometry.Markers.Select(m => m.Position)).ToList();
        if (allPoints.Count == 0)
        {
            return null;
        }

        var minX = allPoints.Min(p => p.X);
        var maxX = allPoints.Max(p => p.X);
        var minY = allPoints.Min(p => p.Y);
        var maxY = allPoints.Max(p => p.Y);

        return (minX, minY, maxX - minX, maxY - minY);
    }

    private static Border BuildVisual(Rect placement, List<TraceLine> lines, List<Marker> markers, Func<Point, Point>? explicitTransform, double missDistancePixels)
    {
        var border = new Border
        {
            Width = placement.Width,
            Height = placement.Height,
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Opacity = DimmedOpacity,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
        };

        var canvas = new Canvas { Width = placement.Width, Height = placement.Height, ClipToBounds = true };
        border.Child = canvas;

        var allPoints = lines.SelectMany(l => l.Points).Concat(markers.Select(m => m.Position)).ToList();
        if (allPoints.Count > 0)
        {
            var transform = explicitTransform ?? FitTransform(allPoints, placement.Width, placement.Height, InnerPadding);

            foreach (var line in lines)
            {
                if (line.Points.Count < 2)
                {
                    continue;
                }

                var polyline = new Polyline
                {
                    Stroke = new SolidColorBrush(line.Color),
                    StrokeThickness = 1.2,
                    Points = new PointCollection(line.Points.Select(transform)),
                };
                canvas.Children.Add(polyline);
            }

            foreach (var marker in markers)
            {
                var p = transform(marker.Position);
                var ellipse = new Ellipse { Width = MarkerDiameter, Height = MarkerDiameter, Fill = new SolidColorBrush(marker.Color) };
                Canvas.SetLeft(ellipse, p.X - (MarkerDiameter / 2));
                Canvas.SetTop(ellipse, p.Y - (MarkerDiameter / 2));
                canvas.Children.Add(ellipse);
            }
        }

        // Lives on the same Canvas as the trace, so it inherits the border's
        // own dimmed/hover opacity and scale instead of needing separate
        // hover wiring - dim by default like the rest of the tile, crisp on
        // hover like the rest of the tile.
        var distanceLabel = new TextBlock
        {
            Text = $"{missDistancePixels:F0}px off",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Width = placement.Width,
            TextAlignment = TextAlignment.Center,
        };
        Canvas.SetLeft(distanceLabel, 0);
        Canvas.SetBottom(distanceLabel, 1);
        canvas.Children.Add(distanceLabel);

        WireHoverAnimation(border);

        return border;
    }

    private static Func<Point, Point> FitTransform(List<Point> points, double tileWidth, double tileHeight, double padding)
    {
        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);

        var boundsWidth = Math.Max(1, maxX - minX);
        var boundsHeight = Math.Max(1, maxY - minY);

        var availableWidth = Math.Max(1, tileWidth - (2 * padding));
        var availableHeight = Math.Max(1, tileHeight - (2 * padding));

        var scale = Math.Min(availableWidth / boundsWidth, availableHeight / boundsHeight);

        var offsetX = padding + ((availableWidth - (boundsWidth * scale)) / 2);
        var offsetY = padding + ((availableHeight - (boundsHeight * scale)) / 2);

        return p => new Point(offsetX + ((p.X - minX) * scale), offsetY + ((p.Y - minY) * scale));
    }

    private static void WireHoverAnimation(Border border)
    {
        border.MouseEnter += (_, _) =>
        {
            Panel.SetZIndex(border, 100);
            Animate(border, DimmedOpacity, 1.0, 1.0, HoverScale);
        };
        border.MouseLeave += (_, _) =>
        {
            Animate(border, 1.0, DimmedOpacity, HoverScale, 1.0);
            Panel.SetZIndex(border, 0);
        };
    }

    private static void Animate(Border border, double fromOpacity, double toOpacity, double fromScale, double toScale)
    {
        border.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(fromOpacity, toOpacity, HoverAnimationDuration));

        var scaleTransform = (ScaleTransform)border.RenderTransform;
        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(fromScale, toScale, HoverAnimationDuration));
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(fromScale, toScale, HoverAnimationDuration));
    }

    private static IReadOnlyList<T> Decimate<T>(IReadOnlyList<T> points, int maxCount)
    {
        if (points.Count <= maxCount)
        {
            return points;
        }

        var result = new List<T>(maxCount);
        for (var i = 0; i < maxCount; i++)
        {
            var index = (int)((long)i * (points.Count - 1) / (maxCount - 1));
            result.Add(points[index]);
        }

        return result;
    }
}
