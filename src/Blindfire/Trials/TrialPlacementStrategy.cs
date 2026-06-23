namespace Blindfire.Trials;

// Target A spawns in a randomized "start zone" near the edge implied by the
// trial's direction; the nominal (never-rendered) B point spawns in the
// opposite zone, so the resulting distance varies trial-to-trial rather than
// using fixed anchors.
public sealed class TrialPlacementStrategy
{
    // Visual distance between Target A and B is kept small relative to the
    // chosen gap, so the on-screen pair never strays far past the requested
    // gap range even with the perpendicular wobble applied.
    private const double CloseGapPerpendicularJitterRatio = 0.2;

    private readonly Random _random;
    private readonly double _edgeMargin;
    private readonly double _startZoneFraction;
    private readonly double _jitterFraction;
    private readonly double? _gapMinPixels;
    private readonly double? _gapMaxPixels;
    private readonly double _originX;
    private readonly double _originY;

    // originX/originY shift every generated point by a fixed offset after
    // placement - used to confine a trial type (e.g. ADS) to a sub-region of
    // the screen by generating positions as if that sub-region's own
    // dimensions were the full screen, then translating into real screen
    // coordinates.
    public TrialPlacementStrategy(Random random, double edgeMargin = 80.0, double startZoneFraction = 0.35, double jitterFraction = 0.20, double? gapMinPixels = null, double? gapMaxPixels = null, double originX = 0.0, double originY = 0.0)
    {
        _random = random;
        _edgeMargin = edgeMargin;
        _startZoneFraction = startZoneFraction;
        _jitterFraction = jitterFraction;
        _gapMinPixels = gapMinPixels;
        _gapMaxPixels = gapMaxPixels;
        _originX = originX;
        _originY = originY;
    }

    public (ScreenPoint TargetA, ScreenPoint NominalB) GeneratePositions(Direction direction, double screenWidth, double screenHeight)
    {
        var (targetA, nominalB) = GenerateLocalPositions(direction, screenWidth, screenHeight);
        return (Translate(targetA), Translate(nominalB));
    }

    // A free-standing point anywhere within the margin - used as the lead-in
    // point of a chain (quick flick) that has no fixed "opposite zone" to
    // anchor against like GeneratePositions's edge-to-edge layout does.
    public ScreenPoint GenerateRandomPoint(double width, double height)
    {
        var local = new ScreenPoint(RandomInRange(_edgeMargin, width - _edgeMargin), RandomInRange(_edgeMargin, height - _edgeMargin));
        return Translate(local);
    }

    // Picks a fresh random direction and gap (reusing the same gap-range
    // fields GenerateCloseGap reads) from a given point, instead of
    // GeneratePositions's fixed edge-zone-to-edge-zone layout - used to chain
    // several flicks back-to-back (quick flick). Flips toward whichever side
    // actually has room if the random direction would run past the margin.
    public (ScreenPoint Point, Direction Direction) GenerateNextPoint(ScreenPoint from, double width, double height)
    {
        var gapMin = _gapMinPixels ?? throw new InvalidOperationException("GenerateNextPoint requires a configured gap range.");
        var gapMax = _gapMaxPixels ?? throw new InvalidOperationException("GenerateNextPoint requires a configured gap range.");

        var local = Untranslate(from);
        var gap = RandomInRange(gapMin, gapMax);
        var jitter = gap * CloseGapPerpendicularJitterRatio;

        if (_random.NextDouble() < 0.5)
        {
            var canGoRight = local.X + gap <= width - _edgeMargin;
            var canGoLeft = local.X - gap >= _edgeMargin;
            var goRight = canGoRight && (!canGoLeft || _random.NextDouble() < 0.5);
            var nextX = goRight ? local.X + gap : local.X - gap;
            var nextY = Clamp(local.Y + RandomInRange(-jitter, jitter), _edgeMargin, height - _edgeMargin);
            return (Translate(new ScreenPoint(nextX, nextY)), goRight ? Direction.LeftToRight : Direction.RightToLeft);
        }

        var canGoDown = local.Y + gap <= height - _edgeMargin;
        var canGoUp = local.Y - gap >= _edgeMargin;
        var goDown = canGoDown && (!canGoUp || _random.NextDouble() < 0.5);
        var nextYVertical = goDown ? local.Y + gap : local.Y - gap;
        var nextXVertical = Clamp(local.X + RandomInRange(-jitter, jitter), _edgeMargin, width - _edgeMargin);
        return (Translate(new ScreenPoint(nextXVertical, nextYVertical)), goDown ? Direction.UpToDown : Direction.DownToUp);
    }

    private (ScreenPoint, ScreenPoint) GenerateLocalPositions(Direction direction, double screenWidth, double screenHeight)
    {
        if (_gapMinPixels is double gapMin && _gapMaxPixels is double gapMax)
        {
            return GenerateCloseGap(direction, screenWidth, screenHeight, gapMin, gapMax);
        }

        return direction switch
        {
            Direction.LeftToRight => GenerateHorizontal(screenWidth, screenHeight, leftToRight: true),
            Direction.RightToLeft => GenerateHorizontal(screenWidth, screenHeight, leftToRight: false),
            Direction.UpToDown => GenerateVertical(screenWidth, screenHeight, topToBottom: true),
            Direction.DownToUp => GenerateVertical(screenWidth, screenHeight, topToBottom: false),
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };
    }

    // Places Target A anywhere with room to spare in the travel direction,
    // then puts B a small fixed-range distance away along that axis - used
    // for the ADS phase, where the on-screen gap should stay within a few
    // inches rather than spanning most of the screen.
    private (ScreenPoint, ScreenPoint) GenerateCloseGap(Direction direction, double width, double height, double gapMin, double gapMax)
    {
        var gap = RandomInRange(gapMin, gapMax);
        var jitter = gap * CloseGapPerpendicularJitterRatio;
        var isHorizontal = direction is Direction.LeftToRight or Direction.RightToLeft;

        if (isHorizontal)
        {
            var sign = direction == Direction.LeftToRight ? 1 : -1;
            var startX = RandomInRange(_edgeMargin + gap, width - _edgeMargin - gap);
            var startY = RandomInRange(_edgeMargin, height - _edgeMargin);
            var endX = startX + sign * gap;
            var endY = Clamp(startY + RandomInRange(-jitter, jitter), _edgeMargin, height - _edgeMargin);
            return (new ScreenPoint(startX, startY), new ScreenPoint(endX, endY));
        }

        var verticalSign = direction == Direction.UpToDown ? 1 : -1;
        var startYVertical = RandomInRange(_edgeMargin + gap, height - _edgeMargin - gap);
        var startXVertical = RandomInRange(_edgeMargin, width - _edgeMargin);
        var endYVertical = startYVertical + verticalSign * gap;
        var endXVertical = Clamp(startXVertical + RandomInRange(-jitter, jitter), _edgeMargin, width - _edgeMargin);
        return (new ScreenPoint(startXVertical, startYVertical), new ScreenPoint(endXVertical, endYVertical));
    }

    private (ScreenPoint, ScreenPoint) GenerateHorizontal(double width, double height, bool leftToRight)
    {
        var startZoneWidth = width * _startZoneFraction;

        var startX = leftToRight
            ? RandomInRange(_edgeMargin, startZoneWidth)
            : RandomInRange(width - startZoneWidth, width - _edgeMargin);
        var endX = leftToRight
            ? RandomInRange(width - startZoneWidth, width - _edgeMargin)
            : RandomInRange(_edgeMargin, startZoneWidth);

        var startY = RandomInRange(_edgeMargin, height - _edgeMargin);
        var jitter = height * _jitterFraction;
        var endY = Clamp(startY + RandomInRange(-jitter, jitter), _edgeMargin, height - _edgeMargin);

        return (new ScreenPoint(startX, startY), new ScreenPoint(endX, endY));
    }

    private (ScreenPoint, ScreenPoint) GenerateVertical(double width, double height, bool topToBottom)
    {
        var startZoneHeight = height * _startZoneFraction;

        var startY = topToBottom
            ? RandomInRange(_edgeMargin, startZoneHeight)
            : RandomInRange(height - startZoneHeight, height - _edgeMargin);
        var endY = topToBottom
            ? RandomInRange(height - startZoneHeight, height - _edgeMargin)
            : RandomInRange(_edgeMargin, startZoneHeight);

        var startX = RandomInRange(_edgeMargin, width - _edgeMargin);
        var jitter = width * _jitterFraction;
        var endX = Clamp(startX + RandomInRange(-jitter, jitter), _edgeMargin, width - _edgeMargin);

        return (new ScreenPoint(startX, startY), new ScreenPoint(endX, endY));
    }

    private ScreenPoint Translate(ScreenPoint point) =>
        _originX == 0.0 && _originY == 0.0 ? point : new ScreenPoint(point.X + _originX, point.Y + _originY);

    private ScreenPoint Untranslate(ScreenPoint point) =>
        _originX == 0.0 && _originY == 0.0 ? point : new ScreenPoint(point.X - _originX, point.Y - _originY);

    private double RandomInRange(double min, double max)
    {
        if (max <= min)
        {
            return min;
        }

        return min + _random.NextDouble() * (max - min);
    }

    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
}
