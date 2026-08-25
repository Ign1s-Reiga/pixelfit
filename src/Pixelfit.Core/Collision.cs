namespace Pixelfit.Core;

/// <summary>One reason a candidate colour conflicts with a palette entry.</summary>
/// <param name="Index">The entry it conflicts with.</param>
/// <param name="Kind">Always <see cref="PaletteWarningKind.TooClose"/> or
/// <see cref="PaletteWarningKind.GreyscaleCollision"/>; a pair is never both.</param>
/// <param name="Value">The measurement behind it — OKLab dE, or lightness difference.</param>
public sealed record Collision(int Index, Rgb24 Color, PaletteWarningKind Kind, float Value);

/// <summary>
/// Where a candidate would sit in a ramp it shares a hue with.
/// </summary>
/// <param name="RampIndex">Index into the ramp list this placement refers to.</param>
/// <param name="PositionByLightness">
/// How many of the ramp's entries are darker than the candidate, so 0 means it would be the
/// darkest and Count means the lightest.
/// </param>
/// <param name="MemberCount">How many entries the ramp has, not counting the candidate.</param>
/// <param name="GapBelow">Lightness distance to the next entry down, or 0 if there is none.</param>
/// <param name="GapAbove">Lightness distance to the next entry up, or 0 if there is none.</param>
/// <param name="ExtendsProgression">
/// The ramp climbs or falls throughout, and putting the candidate after its last entry would
/// carry on in the same direction. A statement about the structure, not about what to do.
/// </param>
public sealed record RampPlacement(
    int RampIndex,
    int PositionByLightness,
    int MemberCount,
    float GapBelow,
    float GapAbove,
    bool ExtendsProgression);

/// <summary>What a candidate colour runs into.</summary>
/// <param name="NearestIndex">Nearest entry, or -1 when there is nothing to compare against.</param>
/// <param name="NearestDistance">
/// OKLab distance to that entry, reported whether or not anything collided. "The closest thing
/// you have is this far away" is the useful answer when the answer is no conflict.
/// </param>
public sealed record CollisionReport(
    Rgb24 Candidate,
    int NearestIndex,
    float NearestDistance,
    IReadOnlyList<Collision> Collisions,
    IReadOnlyList<RampPlacement> Placements);

/// <summary>
/// Measures one colour against a palette: what it is indistinguishable from, what it merges
/// with in greyscale, and where it falls in the ramps it shares a hue with.
/// </summary>
/// <remarks>
/// Every field is a measurement of colours that already exist. Nothing here proposes a colour
/// or rates one, which is the same line <see cref="Ramp"/> holds — the tool reports structure
/// and the author decides what to do about it.
/// <para>
/// Thresholds come from <see cref="RampOptions"/> unchanged, so a pair called too close here is
/// a pair called too close by the palette report, and there is only one set of numbers to reason
/// about.
/// </para>
/// </remarks>
public static class Collide
{
    /// <summary>
    /// Checks <paramref name="candidate"/> against <paramref name="palette"/>.
    /// </summary>
    /// <param name="ignoreIndex">
    /// An entry to leave out, for asking what an entry already in the palette collides with.
    /// Without it every such question answers "itself, at distance zero".
    /// </param>
    public static CollisionReport Check(
        Rgb24 candidate,
        ReadOnlySpan<Rgb24> palette,
        RampOptions? options = null,
        int ignoreIndex = -1)
    {
        options ??= new RampOptions();

        OklabColor lab = Oklab.FromSrgb(candidate);
        OklchColor lch = Oklab.ToOklch(lab);

        float tooCloseSquared = options.TooCloseDistance * options.TooCloseDistance;
        List<Collision> collisions = [];
        int nearest = -1;
        float nearestSquared = float.MaxValue;

        for (int i = 0; i < palette.Length; i++)
        {
            if (i == ignoreIndex)
            {
                continue;
            }

            OklabColor other = Oklab.FromSrgb(palette[i]);
            float squared = Oklab.DistanceSquared(lab, other);
            if (squared < nearestSquared)
            {
                nearestSquared = squared;
                nearest = i;
            }

            if (squared < tooCloseSquared)
            {
                collisions.Add(new Collision(i, palette[i], PaletteWarningKind.TooClose, MathF.Sqrt(squared)));
                continue;
            }

            // Same rule as the palette report: a pair already too close is one problem, not two.
            float lightnessDelta = MathF.Abs(lab.L - other.L);
            if (lightnessDelta < options.GreyscaleLightnessDelta)
            {
                collisions.Add(
                    new Collision(i, palette[i], PaletteWarningKind.GreyscaleCollision, lightnessDelta));
            }
        }

        return new CollisionReport(
            candidate,
            nearest,
            nearest < 0 ? 0f : MathF.Sqrt(nearestSquared),
            collisions,
            Place(lab, lch, palette, options, ignoreIndex));
    }

    /// <summary>Placements in every ramp whose hue the candidate shares.</summary>
    private static List<RampPlacement> Place(
        OklabColor lab,
        OklchColor lch,
        ReadOnlySpan<Rgb24> palette,
        RampOptions options,
        int ignoreIndex)
    {
        // Ramps are detected on the entries actually being compared against. Detecting them on
        // the full palette and dropping the ignored entry's lightness afterwards is not the
        // same thing: two members left of a three-member ramp are no longer a progression, and
        // the ramp's hue and monotonicity would still be answering for an entry excluded by
        // the caller.
        Rgb24[] considered = Without(palette, ignoreIndex);
        IReadOnlyList<RampReport> ramps = Ramp.Ramps(considered, options);
        List<RampPlacement> placements = [];

        for (int i = 0; i < ramps.Count; i++)
        {
            RampReport ramp = ramps[i];
            if (!SharesHue(ramp, lch, options) || ramp.Lightness.Count == 0)
            {
                continue;
            }

            placements.Add(Placement(i, lab.L, [.. ramp.Lightness], ramp.IsMonotonicLightness));
        }

        return placements;
    }

    /// <summary>The palette without one entry, or unchanged when there is nothing to leave out.</summary>
    private static Rgb24[] Without(ReadOnlySpan<Rgb24> palette, int ignoreIndex)
    {
        if (ignoreIndex < 0 || ignoreIndex >= palette.Length)
        {
            return palette.ToArray();
        }

        Rgb24[] kept = new Rgb24[palette.Length - 1];
        for (int i = 0, j = 0; i < palette.Length; i++)
        {
            if (i != ignoreIndex)
            {
                kept[j++] = palette[i];
            }
        }

        return kept;
    }

    /// <summary>
    /// A neutral ramp takes any candidate too grey to have a meaningful hue; a chromatic one
    /// takes a candidate within the same tolerance that grouped its own members.
    /// </summary>
    private static bool SharesHue(RampReport ramp, OklchColor lch, RampOptions options) =>
        ramp.IsNeutral
            ? lch.C < options.NeutralChroma
            : lch.C >= options.NeutralChroma
                && ramp.HueDegrees is float hue
                && MathF.Abs(Oklab.HueDelta(hue, lch.H)) <= options.HueToleranceDegrees;

    private static RampPlacement Placement(int rampIndex, float candidate, float[] lightness, bool monotonic)
    {
        float[] sorted = [.. lightness.OrderBy(l => l)];
        int position = 0;
        while (position < sorted.Length && sorted[position] < candidate)
        {
            position++;
        }

        return new RampPlacement(
            rampIndex,
            position,
            sorted.Length,
            position > 0 ? candidate - sorted[position - 1] : 0f,
            position < sorted.Length ? sorted[position] - candidate : 0f,
            Extends(candidate, lightness, monotonic));
    }

    /// <summary>
    /// Whether adding the candidate after the ramp's last entry carries the progression on.
    /// Palette order is where a new entry lands, so that is the order this asks about.
    /// </summary>
    private static bool Extends(float candidate, float[] lightness, bool monotonic)
    {
        if (!monotonic || lightness.Length < 2)
        {
            return monotonic;
        }

        return lightness[^1] >= lightness[0]
            ? candidate > lightness[^1]
            : candidate < lightness[^1];
    }
}
