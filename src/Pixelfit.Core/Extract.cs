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

        int pixels = width * height;

        // Occupied bins are not distinct colours: several shades can share one 5-bit bin, so a
        // bin count at or below the request says nothing about whether the image has enough
        // colours to fill it. Only the exact count can answer that, and when bins are that few
        // it is cheap to take — at most `count` bins of 512 colours each.
        Bin[] bins = Histogram(rgb, pixels);
        if (bins.Length <= count)
        {
            Bin[] exact = ExactColors(rgb, pixels);
            return exact.Length <= count
                ? Order([.. exact.Select(b => b.Color)])
                : Order(Snap(exact, Iterate(exact, InitialCentres(exact, count))));
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
        long[] sumR = new long[1 << 15];
        long[] sumG = new long[1 << 15];
        long[] sumB = new long[1 << 15];

        for (int i = 0; i < pixels; i++)
        {
            int o = i * 3;
            int key = Key(rgb[o], rgb[o + 1], rgb[o + 2]);
            counts[key]++;
            sumR[key] += rgb[o];
            sumG[key] += rgb[o + 1];
            sumB[key] += rgb[o + 2];
        }

        return Representatives(rgb, pixels, counts, sumR, sumG, sumB);
    }

    /// <summary>
    /// Picks each bin's colour as the one nearest that bin's weighted centre.
    /// </summary>
    /// <remarks>
    /// Not the first colour seen, which was the earlier choice and was wrong twice over: it
    /// ignored the weights, so a single stray pixel could name a bin its whole population
    /// disagreed with, and it depended on scan order, so the same colours arranged differently
    /// produced a different palette. The centre is a mean and is never emitted — it only says
    /// which of the real colours in the bin stands for it.
    /// </remarks>
    private static Bin[] Representatives(
        ReadOnlySpan<byte> rgb,
        int pixels,
        long[] counts,
        long[] sumR,
        long[] sumG,
        long[] sumB)
    {
        int[] best = new int[1 << 15];
        long[] bestDistance = new long[1 << 15];
        Array.Fill(best, -1);

        for (int i = 0; i < pixels; i++)
        {
            int o = i * 3;
            int key = Key(rgb[o], rgb[o + 1], rgb[o + 2]);
            long n = counts[key];

            // Squared distance to the centre, kept in integers by scaling both sides by n.
            long dr = (rgb[o] * n) - sumR[key];
            long dg = (rgb[o + 1] * n) - sumG[key];
            long db = (rgb[o + 2] * n) - sumB[key];
            long d = (dr * dr) + (dg * dg) + (db * db);

            if (best[key] < 0 || d < bestDistance[key])
            {
                best[key] = (rgb[o] << 16) | (rgb[o + 1] << 8) | rgb[o + 2];
                bestDistance[key] = d;
            }
        }

        List<Bin> bins = [];
        for (int key = 0; key < counts.Length; key++)
        {
            if (counts[key] > 0)
            {
                Rgb24 color = new((byte)(best[key] >> 16), (byte)((best[key] >> 8) & 0xFF), (byte)(best[key] & 0xFF));
                bins.Add(new Bin(color, Oklab.FromSrgb(color), counts[key]));
            }
        }

        return [.. bins];
    }

    /// <summary>
    /// Every distinct colour with its exact pixel count, for the case where the bins collapse
    /// to fewer entries than were asked for and only exact colours can fill the request.
    /// </summary>
    private static Bin[] ExactColors(ReadOnlySpan<byte> rgb, int pixels)
    {
        Dictionary<int, long> counts = [];
        for (int i = 0; i < pixels; i++)
        {
            int o = i * 3;
            int packed = (rgb[o] << 16) | (rgb[o + 1] << 8) | rgb[o + 2];
            counts[packed] = counts.TryGetValue(packed, out long n) ? n + 1 : 1;
        }

        return
        [
            .. counts
                .OrderBy(e => e.Key)
                .Select(e =>
                {
                    Rgb24 color = new((byte)(e.Key >> 16), (byte)((e.Key >> 8) & 0xFF), (byte)(e.Key & 0xFF));
                    return new Bin(color, Oklab.FromSrgb(color), e.Value);
                }),
        ];
    }

    private static int Key(byte r, byte g, byte b) =>
        ((r >> QuantizeShift) << 10) | ((g >> QuantizeShift) << 5) | (b >> QuantizeShift);

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
