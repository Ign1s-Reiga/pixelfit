namespace Pixelfit.Core;

/// <summary>One palette entry, with the conversions every check needs computed once.</summary>
public sealed record PaletteEntry(int Index, Rgb24 Color, OklabColor Lab, OklchColor Lch);

/// <summary>
/// A group of entries sharing a hue whose lightness values form a progression.
/// Members are listed in palette order, because that is the order the author wrote them in
/// and the order a non-monotonic ramp is non-monotonic with respect to.
/// </summary>
public sealed record RampReport
{
    public required IReadOnlyList<int> Indices { get; init; }

    /// <summary>Mean hue of the ramp in degrees, or null when the ramp is neutral.</summary>
    public float? HueDegrees { get; init; }

    public bool IsNeutral { get; init; }

    /// <summary>Lightness is strictly increasing or strictly decreasing in palette order.</summary>
    public required bool IsMonotonicLightness { get; init; }

    /// <summary>Positions in palette order, 1-based within the ramp, where the progression reverses.</summary>
    public required IReadOnlyList<int> OutOfOrderPositions { get; init; }

    /// <summary>Standard deviation of successive lightness steps. One cramped step is a rung that disappears.</summary>
    public required float LightnessStepDeviation { get; init; }

    /// <summary>Signed hue change from the darkest entry to the lightest, in degrees. Reported, never judged.</summary>
    public required float HueShiftDegrees { get; init; }

    public required IReadOnlyList<float> Lightness { get; init; }
}

public enum PaletteWarningKind
{
    /// <summary>Two entries closer than the eye can separate. One of them is a wasted slot.</summary>
    TooClose,

    /// <summary>Two entries that differ in chroma but not in lightness, and merge when desaturated.</summary>
    GreyscaleCollision,

    /// <summary>A ramp whose lightness does not read as a progression.</summary>
    NonMonotonicRamp,

    /// <summary>A ramp with one step much tighter or wider than the rest.</summary>
    UnevenLightnessSteps,

    /// <summary>A palette entry the image does not use.</summary>
    Unused,
}

public sealed record PaletteWarning(PaletteWarningKind Kind, IReadOnlyList<int> Indices, string Message, float Value);

public sealed record PaletteReport(
    IReadOnlyList<PaletteEntry> Entries,
    IReadOnlyList<RampReport> Ramps,
    IReadOnlyList<PaletteWarning> Warnings);

/// <summary>Thresholds for the structural checks. Every one of them is a measurement, not a taste.</summary>
public sealed record RampOptions
{
    /// <summary>How far apart in hue two entries can be and still belong to the same ramp.</summary>
    public float HueToleranceDegrees { get; init; } = 25f;

    /// <summary>Below this chroma, hue is undefined noise; the entry is treated as neutral.</summary>
    public float NeutralChroma { get; init; } = 0.02f;

    /// <summary>Entries closer than this in OKLab are not tellable apart.</summary>
    public float TooCloseDistance { get; init; } = 0.02f;

    /// <summary>Entries whose lightness differs by less than this merge when desaturated.</summary>
    public float GreyscaleLightnessDelta { get; init; } = 0.015f;

    /// <summary>Fewer entries than this is not a progression.</summary>
    public int MinimumRampLength { get; init; } = 3;

    /// <summary>Step spread above this is worth reporting.</summary>
    public float UnevenStepDeviation { get; init; } = 0.03f;
}

/// <summary>
/// Structural analysis of a palette: whether it is internally consistent, never whether it
/// is pretty. The tool does not have taste and must not pretend to.
/// </summary>
public static class Ramp
{
    public static PaletteReport Analyze(
        ReadOnlySpan<Rgb24> palette,
        RampOptions? options = null,
        ReadOnlySpan<Rgb24> imageColors = default)
    {
        options ??= new RampOptions();

        PaletteEntry[] entries = new PaletteEntry[palette.Length];
        for (int i = 0; i < palette.Length; i++)
        {
            OklabColor lab = Oklab.FromSrgb(palette[i]);
            entries[i] = new PaletteEntry(i, palette[i], lab, Oklab.ToOklch(lab));
        }

        List<RampReport> ramps = DetectRamps(entries, options);
        List<PaletteWarning> warnings = [];

        warnings.AddRange(RampWarnings(ramps, options));
        warnings.AddRange(PairWarnings(entries, options));
        warnings.AddRange(UnusedWarnings(entries, imageColors));

        return new PaletteReport(entries, ramps, warnings);
    }

    /// <summary>
    /// Groups entries by hue, then reports the lightness progression of each group. Neutral
    /// entries go in one group of their own: their hue is noise, but their lightness is not.
    /// </summary>
    private static List<RampReport> DetectRamps(PaletteEntry[] entries, RampOptions options)
    {
        List<RampReport> ramps = [];

        PaletteEntry[] neutrals = [.. entries.Where(e => e.Lch.C < options.NeutralChroma)];
        if (neutrals.Length >= options.MinimumRampLength)
        {
            ramps.Add(Describe(neutrals, isNeutral: true));
        }

        foreach (List<PaletteEntry> group in GroupByHue([.. entries.Where(e => e.Lch.C >= options.NeutralChroma)], options))
        {
            if (group.Count >= options.MinimumRampLength)
            {
                ramps.Add(Describe([.. group.OrderBy(e => e.Index)], isNeutral: false));
            }
        }

        return [.. ramps.OrderBy(r => r.Indices[0])];
    }

    /// <summary>
    /// Sweeps entries in hue order and cuts a new group wherever the gap exceeds the
    /// tolerance, then closes the circle so that a ramp straddling 0 degrees stays whole.
    /// </summary>
    private static List<List<PaletteEntry>> GroupByHue(PaletteEntry[] chromatic, RampOptions options)
    {
        List<List<PaletteEntry>> groups = [];
        if (chromatic.Length == 0)
        {
            return groups;
        }

        PaletteEntry[] byHue = [.. chromatic.OrderBy(e => e.Lch.H)];
        List<PaletteEntry> current = [byHue[0]];

        for (int i = 1; i < byHue.Length; i++)
        {
            if (byHue[i].Lch.H - byHue[i - 1].Lch.H <= options.HueToleranceDegrees)
            {
                current.Add(byHue[i]);
            }
            else
            {
                groups.Add(current);
                current = [byHue[i]];
            }
        }

        groups.Add(current);

        // Hue is a circle: 359 and 1 are two degrees apart, not 358.
        if (groups.Count > 1
            && Math.Abs(Oklab.HueDelta(groups[^1][^1].Lch.H, groups[0][0].Lch.H)) <= options.HueToleranceDegrees)
        {
            groups[0].AddRange(groups[^1]);
            groups.RemoveAt(groups.Count - 1);
        }

        return groups;
    }

    private static RampReport Describe(PaletteEntry[] members, bool isNeutral)
    {
        float[] lightness = [.. members.Select(m => m.Lab.L)];
        (bool monotonic, int[] outOfOrder) = CheckMonotonic(lightness);

        float[] sorted = [.. lightness.OrderBy(l => l)];
        float deviation = StepDeviation(sorted);

        PaletteEntry darkest = members.MinBy(m => m.Lab.L)!;
        PaletteEntry lightest = members.MaxBy(m => m.Lab.L)!;

        return new RampReport
        {
            Indices = [.. members.Select(m => m.Index)],
            HueDegrees = isNeutral ? null : MeanHue(members),
            IsNeutral = isNeutral,
            IsMonotonicLightness = monotonic,
            OutOfOrderPositions = outOfOrder,
            LightnessStepDeviation = deviation,
            HueShiftDegrees = isNeutral ? 0f : Oklab.HueDelta(darkest.Lch.H, lightest.Lch.H),
            Lightness = lightness,
        };
    }

    /// <summary>
    /// A ramp may be authored dark to light or light to dark; either reads as a progression.
    /// What does not read is a reversal partway through.
    /// </summary>
    private static (bool Monotonic, int[] OutOfOrder) CheckMonotonic(float[] lightness)
    {
        if (lightness.Length < 2)
        {
            return (true, []);
        }

        bool ascending = lightness[^1] >= lightness[0];
        List<int> outOfOrder = [];

        for (int i = 1; i < lightness.Length; i++)
        {
            float delta = lightness[i] - lightness[i - 1];
            if (ascending ? delta <= 0 : delta >= 0)
            {
                outOfOrder.Add(i + 1);
            }
        }

        return (outOfOrder.Count == 0, [.. outOfOrder]);
    }

    private static float StepDeviation(float[] sortedLightness)
    {
        if (sortedLightness.Length < 3)
        {
            return 0f;
        }

        float[] steps = new float[sortedLightness.Length - 1];
        for (int i = 0; i < steps.Length; i++)
        {
            steps[i] = sortedLightness[i + 1] - sortedLightness[i];
        }

        float mean = steps.Average();
        float variance = steps.Sum(s => (s - mean) * (s - mean)) / steps.Length;
        return MathF.Sqrt(variance);
    }

    /// <summary>Circular mean, so a group spanning 350..10 degrees averages to zero and not to 180.</summary>
    private static float MeanHue(PaletteEntry[] members)
    {
        float x = 0f;
        float y = 0f;
        foreach (PaletteEntry m in members)
        {
            float radians = m.Lch.H * (MathF.PI / 180f);
            x += MathF.Cos(radians);
            y += MathF.Sin(radians);
        }

        float hue = MathF.Atan2(y, x) * (180f / MathF.PI);
        return hue < 0f ? hue + 360f : hue;
    }

    private static IEnumerable<PaletteWarning> RampWarnings(List<RampReport> ramps, RampOptions options)
    {
        foreach (RampReport ramp in ramps)
        {
            if (!ramp.IsMonotonicLightness)
            {
                string positions = string.Join(", ", ramp.OutOfOrderPositions);
                yield return new PaletteWarning(
                    PaletteWarningKind.NonMonotonicRamp,
                    ramp.Indices,
                    $"lightness reverses at position {positions} of {ramp.Indices.Count}",
                    0f);
            }

            if (ramp.LightnessStepDeviation > options.UnevenStepDeviation)
            {
                yield return new PaletteWarning(
                    PaletteWarningKind.UnevenLightnessSteps,
                    ramp.Indices,
                    $"lightness steps vary by sigma {ramp.LightnessStepDeviation:F3}",
                    ramp.LightnessStepDeviation);
            }
        }
    }

    private static IEnumerable<PaletteWarning> PairWarnings(PaletteEntry[] entries, RampOptions options)
    {
        for (int i = 0; i < entries.Length; i++)
        {
            for (int j = i + 1; j < entries.Length; j++)
            {
                float distance = Oklab.Distance(entries[i].Lab, entries[j].Lab);
                if (distance < options.TooCloseDistance)
                {
                    yield return new PaletteWarning(
                        PaletteWarningKind.TooClose,
                        [i, j],
                        $"{entries[i].Color} and {entries[j].Color} differ by dE {distance:F3}",
                        distance);
                    continue;
                }

                // Only worth reporting for entries that are otherwise distinguishable —
                // a pair already flagged as too close is one problem, not two.
                float lightnessDelta = MathF.Abs(entries[i].Lab.L - entries[j].Lab.L);
                if (lightnessDelta < options.GreyscaleLightnessDelta)
                {
                    yield return new PaletteWarning(
                        PaletteWarningKind.GreyscaleCollision,
                        [i, j],
                        $"{entries[i].Color} and {entries[j].Color} differ by L {lightnessDelta:F3}"
                        + " and will merge when desaturated",
                        lightnessDelta);
                }
            }
        }
    }

    private static IEnumerable<PaletteWarning> UnusedWarnings(PaletteEntry[] entries, ReadOnlySpan<Rgb24> imageColors)
    {
        if (imageColors.IsEmpty)
        {
            return [];
        }

        HashSet<Rgb24> used = [];
        foreach (Rgb24 c in imageColors)
        {
            used.Add(c);
        }

        return entries
            .Where(e => !used.Contains(e.Color))
            .Select(e => new PaletteWarning(
                PaletteWarningKind.Unused,
                [e.Index],
                $"{e.Color} is not present in this image",
                0f))
            .ToList();
    }
}
