using System.Windows;

namespace Blindfire.Results;

// Lays tiles out in the four bands surrounding a central exclusion rectangle
// (the results card) - top and bottom bands span the full width, left/right
// bands fill the strip beside the card - so tiles never sit underneath it.
// Pure geometry, no rendering: returns one placement Rect per requested tile,
// in the same order they were requested.
public static class TileLayoutPlanner
{
    private const double TilePadding = 6.0;
    private const double MinCellSize = 70.0;
    private const double MaxCellSize = 200.0;

    public static IReadOnlyList<Rect> PlanTileLayout(double windowWidth, double windowHeight, Rect exclusionZone, int tileCount)
    {
        if (tileCount < 1)
        {
            return Array.Empty<Rect>();
        }

        var bands = new[]
        {
            new Rect(0, 0, windowWidth, Math.Max(0, exclusionZone.Top)),
            new Rect(0, exclusionZone.Bottom, windowWidth, Math.Max(0, windowHeight - exclusionZone.Bottom)),
            new Rect(0, exclusionZone.Top, Math.Max(0, exclusionZone.Left), exclusionZone.Height),
            new Rect(exclusionZone.Right, exclusionZone.Top, Math.Max(0, windowWidth - exclusionZone.Right), exclusionZone.Height),
        };

        // One uniform cell size for every band, rather than each band fitting
        // its own share independently - that's what makes the tiles read as
        // one even, continuously-sized ring around the card instead of a few
        // big tiles in roomy bands and squished ones in tight bands.
        var cellSize = PickUniformCellSize(bands, tileCount);

        var capacities = bands.Select(b => (double)CapacityFor(b, cellSize)).ToArray();
        var totalCapacity = (int)capacities.Sum();
        var counts = DistributeProportionally(capacities, Math.Min(tileCount, totalCapacity));

        var result = new List<Rect>(tileCount);
        for (var i = 0; i < bands.Length; i++)
        {
            result.AddRange(LayOutBand(bands[i], counts[i], cellSize));
        }

        return result;
    }

    private static double PickUniformCellSize(Rect[] bands, int tileCount)
    {
        // The thinnest usable band (typically the top/bottom strips above
        // and below the card) caps how big a tile can be while still fitting
        // at least one row/column there - without this cap an area-driven
        // size could leave that whole band empty.
        var thinnestUsableExtent = bands
            .Where(b => b.Width > 0 && b.Height > 0)
            .Select(b => Math.Min(b.Width, b.Height))
            .DefaultIfEmpty(MaxCellSize)
            .Min();

        var totalArea = bands.Sum(b => b.Width * b.Height);
        var areaDrivenSize = totalArea > 0 ? Math.Max(MinCellSize, Math.Sqrt(totalArea / tileCount)) : MinCellSize;

        return Math.Min(Math.Min(thinnestUsableExtent, areaDrivenSize), MaxCellSize);
    }

    private static int CapacityFor(Rect band, double cellSize)
    {
        if (band.Width <= 0 || band.Height <= 0)
        {
            return 0;
        }

        var columns = (int)Math.Floor(band.Width / cellSize);
        var rows = (int)Math.Floor(band.Height / cellSize);
        return Math.Max(0, columns * rows);
    }

    private static int[] DistributeProportionally(double[] weights, int total)
    {
        var totalWeight = weights.Sum();
        var counts = new int[weights.Length];

        if (totalWeight <= 0)
        {
            counts[0] = total;
            return counts;
        }

        var assigned = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            counts[i] = (int)Math.Floor(total * weights[i] / totalWeight);
            assigned += counts[i];
        }

        // Largest-remainder leftovers go to the largest band so the counts
        // always sum to exactly `total`.
        var order = Enumerable.Range(0, weights.Length).OrderByDescending(i => weights[i]).ToList();
        var remaining = total - assigned;
        for (var i = 0; remaining > 0 && i < order.Count; i++, remaining--)
        {
            counts[order[i % order.Count]]++;
        }

        return counts;
    }

    private static IEnumerable<Rect> LayOutBand(Rect band, int count, double cellSize)
    {
        if (count < 1 || band.Width <= 0 || band.Height <= 0)
        {
            yield break;
        }

        var columns = Math.Max(1, (int)Math.Floor(band.Width / cellSize));
        var rows = (int)Math.Ceiling(count / (double)columns);

        // Center the used block of cells within the band so tiles read as
        // evenly spaced padding-from-card rather than hugging one edge.
        var usedWidth = Math.Min(columns, count) * cellSize;
        var usedHeight = Math.Min(rows * cellSize, band.Height);
        var offsetX = band.X + ((band.Width - usedWidth) / 2);
        var offsetY = band.Y + ((band.Height - usedHeight) / 2);

        for (var i = 0; i < count; i++)
        {
            var col = i % columns;
            var row = i / columns;

            var x = offsetX + (col * cellSize) + TilePadding;
            var y = offsetY + (row * cellSize) + TilePadding;
            var size = cellSize - (2 * TilePadding);

            yield return new Rect(x, y, Math.Max(1, size), Math.Max(1, size));
        }
    }
}
