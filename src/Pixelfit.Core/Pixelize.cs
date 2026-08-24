namespace Pixelfit.Core;

/// <summary>Everything the pixelize pipeline can be told. All of it has a working default.</summary>
public sealed record PixelizeOptions
{
    /// <summary>Overrides grid estimation. The escape hatch that makes 90%-accurate estimation acceptable.</summary>
    public int? GridSize { get; init; }

    /// <summary>Overrides phase estimation. Only consulted when both components are given.</summary>
    public (int X, int Y)? Phase { get; init; }

    /// <summary>Snaps every cell to its nearest entry. Without one, cell colours are left as reduced.</summary>
    public IReadOnlyList<Rgb24>? Palette { get; init; }

    /// <summary>Ordered 4x4 Bayer. Usually looks wrong at sprite sizes.</summary>
    public bool Dither { get; init; }
}

/// <summary>The pixelized result, plus what the estimator concluded about the grid.</summary>
public sealed record PixelizeResult(CellGrid Cells, GridEstimate Grid);

/// <summary>
/// The whole pipeline, in the one place both front-ends call. Anything that lives here
/// instead of in the CLI or the plugin is something the two cannot disagree about.
/// </summary>
public static class Pixelize
{
    private static readonly int[,] Bayer4x4 =
    {
        { 0, 8, 2, 10 },
        { 12, 4, 14, 6 },
        { 3, 11, 1, 9 },
        { 15, 7, 13, 5 },
    };

    public static PixelizeResult Run(ReadOnlySpan<byte> rgb, int width, int height, PixelizeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        GridEstimate grid = ResolveGrid(rgb, width, height, options);
        CellGrid cells = Reduce.ToCells(rgb, width, height, grid.Size, grid.PhaseX, grid.PhaseY);

        if (options.Palette is { Count: > 0 } palette)
        {
            cells = SnapToPalette(cells, palette, options.Dither);
        }

        return new PixelizeResult(cells, grid);
    }

    private static GridEstimate ResolveGrid(
        ReadOnlySpan<byte> rgb,
        int width,
        int height,
        PixelizeOptions options)
    {
        if (options.GridSize is not int size)
        {
            GridEstimate estimated = Grid.Estimate(rgb, width, height);
            return options.Phase is (int px, int py)
                ? estimated with { PhaseX = px, PhaseY = py }
                : estimated;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);

        if (options.Phase is (int manualX, int manualY))
        {
            return new GridEstimate(size, size, size, manualX, manualY, false);
        }

        (int phaseX, int phaseY) = Grid.EstimatePhase(rgb, width, height, size);
        return new GridEstimate(size, size, size, phaseX, phaseY, false);
    }

    /// <summary>
    /// Replaces every cell with a palette entry copied verbatim. Nothing here converts OKLab
    /// back to sRGB, so an out-of-gamut colour cannot reach the output.
    /// </summary>
    public static CellGrid SnapToPalette(CellGrid cells, IReadOnlyList<Rgb24> palette, bool dither = false)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.Count == 0)
        {
            return cells;
        }

        Rgb24[] entries = [.. palette];
        OklabColor[] lab = Oklab.ToOklab(entries);
        float spread = dither ? MeanNearestNeighbourDistance(lab) : 0f;

        Rgb24[] snapped = new Rgb24[cells.Cells.Length];
        int width = cells.Width;

        Parallel.For(0, cells.Height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                OklabColor c = Oklab.FromSrgb(cells.Cells[i]);

                if (dither)
                {
                    // Perturb lightness only. Nudging chroma as well shifts hue, which is a
                    // decision about the artwork rather than a quantisation step.
                    float t = (Bayer4x4[y & 3, x & 3] / 16f) - 0.5f;
                    c = c with { L = c.L + (t * spread) };
                }

                snapped[i] = entries[Oklab.NearestInPalette(c, lab)];
            }
        });

        return new CellGrid(snapped, cells.Width, cells.Height);
    }

    /// <summary>Sets the dither amplitude from the palette's own spacing rather than a magic constant.</summary>
    private static float MeanNearestNeighbourDistance(ReadOnlySpan<OklabColor> lab)
    {
        if (lab.Length < 2)
        {
            return 0f;
        }

        float total = 0f;
        for (int i = 0; i < lab.Length; i++)
        {
            float nearest = float.MaxValue;
            for (int j = 0; j < lab.Length; j++)
            {
                if (i != j)
                {
                    nearest = MathF.Min(nearest, Oklab.DistanceSquared(lab[i], lab[j]));
                }
            }

            total += MathF.Sqrt(nearest);
        }

        return total / lab.Length;
    }

    /// <summary>Flattens a cell grid to an RGB byte buffer, one byte triple per cell.</summary>
    public static byte[] ToRgbBytes(CellGrid cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        byte[] rgb = new byte[cells.Cells.Length * 3];
        for (int i = 0; i < cells.Cells.Length; i++)
        {
            rgb[i * 3] = cells.Cells[i].R;
            rgb[(i * 3) + 1] = cells.Cells[i].G;
            rgb[(i * 3) + 2] = cells.Cells[i].B;
        }

        return rgb;
    }

    /// <summary>Nearest-neighbour magnification, for viewing a sprite at a usable size.</summary>
    public static byte[] Magnify(CellGrid cells, int factor, out int width, out int height)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentOutOfRangeException.ThrowIfLessThan(factor, 1);

        width = cells.Width * factor;
        height = cells.Height * factor;
        byte[] rgb = new byte[width * height * 3];

        for (int y = 0; y < height; y++)
        {
            int sourceRow = (y / factor) * cells.Width;
            for (int x = 0; x < width; x++)
            {
                Rgb24 c = cells.Cells[sourceRow + (x / factor)];
                int o = ((y * width) + x) * 3;
                rgb[o] = c.R;
                rgb[o + 1] = c.G;
                rgb[o + 2] = c.B;
            }
        }

        return rgb;
    }

    /// <summary>The distinct colours of an image, in first-seen order. The palette source for PaletteLens.</summary>
    public static Rgb24[] UniqueColors(ReadOnlySpan<byte> rgb, int width, int height, int limit = 4096)
    {
        HashSet<int> seen = [];
        List<Rgb24> result = [];
        int pixels = width * height;

        for (int i = 0; i < pixels && result.Count < limit; i++)
        {
            int o = i * 3;
            int key = (rgb[o] << 16) | (rgb[o + 1] << 8) | rgb[o + 2];
            if (seen.Add(key))
            {
                result.Add(new Rgb24(rgb[o], rgb[o + 1], rgb[o + 2]));
            }
        }

        return [.. result];
    }
}
