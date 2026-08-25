namespace Pixelfit.Core;

/// <summary>
/// Reduces the colours of an image to the few it is actually made of.
/// </summary>
/// <remarks>
/// This is a measurement, not a design. It reports which colours carry the image, in the same
/// category as reporting which palette slots an image uses — it does not propose colours, rate
/// them, or adjust them. Choosing a palette to draw with is a decision and stays the author's.
/// <para>
/// Every colour returned is one the image contains, copied verbatim. Cluster centres are means,
/// and emitting one would invent a colour present nowhere in the source — the same failure
/// <see cref="Reduce"/> refuses when it takes the mode over the mean — as well as putting an
/// OKLab-to-sRGB conversion in a production path, which this project does not have.
/// </para>
/// </remarks>
public static class Extract
{
    /// <summary>Colours are binned at 5 bits per channel, as in <see cref="Reduce"/>.</summary>
    private const int QuantizeShift = 3;

    /// <summary>Fixed, because a palette that changes between runs of the same image is a bug.</summary>
    private const int Seed = 0x5EED;

    private const int MaxIterations = 32;

    /// <summary>
    /// The <paramref name="count"/> colours that best represent the image, ordered so the
    /// result reads as ramps rather than as scan order.
    /// </summary>
    public static Rgb24[] Palette(ReadOnlySpan<byte> rgb, int width, int height, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        if (rgb.Length < width * height * 3)
        {
            throw new ArgumentException("Pixel buffer is smaller than the stated dimensions.", nameof(rgb));
        }

        Bin[] bins = Histogram(rgb, width * height);
        if (bins.Length <= count)
        {
            // Fewer distinct colours than asked for: the image is already its own palette.
            return Order([.. bins.Select(b => b.Color)]);
        }

        OklabColor[] centres = Iterate(bins, InitialCentres(bins, count));
        return Order(Snap(bins, centres));
    }

    /// <summary>
    /// One entry per occupied 5-bit bin, carrying a colour the image really contains and how
    /// many pixels landed in it. Weighting by pixel count is the point: a colour covering a
    /// third of the image matters more than one stray anti-aliasing artefact, and a list of
    /// distinct colours throws exactly that away.
    /// </summary>
    private static Bin[] Histogram(ReadOnlySpan<byte> rgb, int pixels)
    {
        long[] counts = new long[1 << 15];
        int[] representative = new int[1 << 15];
        Array.Fill(representative, -1);

        for (int i = 0; i < pixels; i++)
        {
            int o = i * 3;
            int key = ((rgb[o] >> QuantizeShift) << 10)
                | ((rgb[o + 1] >> QuantizeShift) << 5)
                | (rgb[o + 2] >> QuantizeShift);

            counts[key]++;
            if (representative[key] < 0)
            {
                representative[key] = (rgb[o] << 16) | (rgb[o + 1] << 8) | rgb[o + 2];
            }
        }

        List<Bin> bins = [];
        for (int key = 0; key < counts.Length; key++)
        {
            if (counts[key] == 0)
            {
                continue;
            }

            int packed = representative[key];
            Rgb24 color = new((byte)(packed >> 16), (byte)((packed >> 8) & 0xFF), (byte)(packed & 0xFF));
            bins.Add(new Bin(color, Oklab.FromSrgb(color), counts[key]));
        }

        return [.. bins];
    }

    /// <summary>
    /// k-means++ seeding, weighted by pixel count: spread the initial centres out instead of
    /// dropping them at random, which is what stops two of them starting inside one colour.
    /// </summary>
    private static OklabColor[] InitialCentres(Bin[] bins, int count)
    {
        Random random = new(Seed);
        OklabColor[] centres = new OklabColor[count];
        centres[0] = bins.MaxBy(b => b.Count).Lab;

        double[] nearest = new double[bins.Length];
        Array.Fill(nearest, double.MaxValue);

        for (int c = 1; c < count; c++)
        {
            double total = 0;
            for (int i = 0; i < bins.Length; i++)
            {
                nearest[i] = Math.Min(nearest[i], Oklab.DistanceSquared(bins[i].Lab, centres[c - 1]));
                total += nearest[i] * bins[i].Count;
            }

            centres[c] = total <= 0 ? bins[0].Lab : PickWeighted(bins, nearest, total * random.NextDouble());
        }

        return centres;
    }

    private static OklabColor PickWeighted(Bin[] bins, double[] nearest, double target)
    {
        double running = 0;
        for (int i = 0; i < bins.Length; i++)
        {
            running += nearest[i] * bins[i].Count;
            if (running >= target)
            {
                return bins[i].Lab;
            }
        }

        return bins[^1].Lab;
    }

    /// <summary>Lloyd's algorithm, to convergence or the iteration cap.</summary>
    private static OklabColor[] Iterate(Bin[] bins, OklabColor[] centres)
    {
        int[] assignment = new int[bins.Length];

        for (int pass = 0; pass < MaxIterations; pass++)
        {
            bool moved = false;
            for (int i = 0; i < bins.Length; i++)
            {
                int best = Nearest(bins[i].Lab, centres);
                if (best != assignment[i])
                {
                    assignment[i] = best;
                    moved = true;
                }
            }

            if (!moved && pass > 0)
            {
                break;
            }

            Recentre(bins, assignment, centres);
        }

        return centres;
    }

    /// <summary>
    /// Moves each centre to the weighted mean of what it owns. An empty cluster keeps its
    /// previous position rather than being re-seeded; it will still snap to a real colour.
    /// </summary>
    private static void Recentre(Bin[] bins, int[] assignment, OklabColor[] centres)
    {
        double[] l = new double[centres.Length];
        double[] a = new double[centres.Length];
        double[] b = new double[centres.Length];
        double[] weight = new double[centres.Length];

        for (int i = 0; i < bins.Length; i++)
        {
            int c = assignment[i];
            long w = bins[i].Count;
            l[c] += bins[i].Lab.L * w;
            a[c] += bins[i].Lab.A * w;
            b[c] += bins[i].Lab.B * w;
            weight[c] += w;
        }

        for (int c = 0; c < centres.Length; c++)
        {
            if (weight[c] > 0)
            {
                centres[c] = new OklabColor(
                    (float)(l[c] / weight[c]),
                    (float)(a[c] / weight[c]),
                    (float)(b[c] / weight[c]));
            }
        }
    }

    /// <summary>
    /// Replaces every centre with the nearest colour the image actually contains. This is what
    /// keeps the invariant that a colour out is a colour in; a centre is a mean and is not.
    /// </summary>
    private static Rgb24[] Snap(Bin[] bins, OklabColor[] centres)
    {
        HashSet<int> taken = [];
        List<Rgb24> palette = [];

        foreach (OklabColor centre in centres)
        {
            int best = -1;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < bins.Length; i++)
            {
                if (taken.Contains(i))
                {
                    continue;
                }

                float d = Oklab.DistanceSquared(centre, bins[i].Lab);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }

            if (best >= 0)
            {
                taken.Add(best);
                palette.Add(bins[best].Color);
            }
        }

        return [.. palette];
    }

    /// <summary>
    /// Emits each ramp's members together and ascending in lightness, neutrals first.
    /// </summary>
    /// <remarks>
    /// Ordered by the ramp detector's own grouping rather than by a hue sort. Sorting on hue
    /// looks equivalent and is not: the detector chains entries within a tolerance, so a run
    /// spanning 204 to 294 degrees is one ramp while a bucketed sort cuts it into three and
    /// interleaves their lightness. The palette then reports itself as non-monotonic on the
    /// strength of nothing but the order it was written in.
    /// </remarks>
    private static Rgb24[] Order(Rgb24[] palette)
    {
        List<Rgb24> ordered = [];
        HashSet<int> placed = [];

        foreach (RampReport ramp in Ramp.Ramps(palette)
            .OrderBy(r => r.IsNeutral ? 0 : 1)
            .ThenBy(r => r.HueDegrees ?? 0f))
        {
            foreach (int index in ramp.Indices.OrderBy(i => Oklab.FromSrgb(palette[i]).L))
            {
                if (placed.Add(index))
                {
                    ordered.Add(palette[index]);
                }
            }
        }

        // Entries in no ramp at all still have to come out, and hue then lightness is as good
        // an order as any for colours with no structure to preserve.
        foreach ((Rgb24 color, int _) in palette
            .Select((c, i) => (Color: c, Index: i))
            .Where(e => !placed.Contains(e.Index))
            .OrderBy(e => Oklab.OklchFromSrgb(e.Color).H)
            .ThenBy(e => Oklab.FromSrgb(e.Color).L))
        {
            ordered.Add(color);
        }

        return [.. ordered];
    }

    private static int Nearest(OklabColor c, OklabColor[] centres)
    {
        int best = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < centres.Length; i++)
        {
            float d = Oklab.DistanceSquared(c, centres[i]);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        return best;
    }

    /// <summary>An occupied colour bin: a colour the image contains, and how much of it there is.</summary>
    private readonly record struct Bin(Rgb24 Color, OklabColor Lab, long Count);
}
