using Pixelfit.Core;
using Xunit;

namespace Pixelfit.Tests;

/// <summary>
/// One colour measured against a palette. Every assertion here is about what the palette
/// already contains — nothing in this feature proposes a colour, and nothing rates one.
/// </summary>
public sealed class CollisionTests
{
    /// <summary>
    /// Four steps of one hue, monotonic in lightness, evenly spaced. Built in OKLCh so the
    /// hues are exactly where the tests say they are: lightening an orange in sRGB drags its
    /// hue toward yellow, which would leave the placement cases hanging on a degree of
    /// tolerance rather than on the behaviour they mean to check.
    /// </summary>
    private static readonly Rgb24[] WarmRamp =
    [
        At(0.30f, 0.07f, 45f),
        At(0.45f, 0.07f, 45f),
        At(0.60f, 0.07f, 45f),
        At(0.75f, 0.07f, 45f),
    ];

    private static Rgb24 At(float lightness, float chroma, float hue) =>
        Oklab.ToSrgb(Oklab.FromOklch(new OklchColor(lightness, chroma, hue)));

    /// <summary>One step off in every channel: a colour nobody could tell from the original.</summary>
    private static Rgb24 Nudge(Rgb24 c) =>
        new((byte)Math.Min(255, c.R + 1), (byte)Math.Min(255, c.G + 1), (byte)Math.Min(255, c.B + 1));

    [Fact]
    public void TheNearestEntryIsReportedEvenWhenNothingCollides()
    {
        CollisionReport report = Collide.Check(At(0.55f, 0.10f, 250f), WarmRamp);

        Assert.Empty(report.Collisions);
        Assert.InRange(report.NearestIndex, 0, WarmRamp.Length - 1);
        Assert.True(report.NearestDistance > 0f, "a distinct colour still has a nearest entry");
    }

    [Fact]
    public void AColourAlreadyInThePaletteCollidesWithItselfAtZero()
    {
        CollisionReport report = Collide.Check(WarmRamp[2], WarmRamp);

        Assert.Equal(2, report.NearestIndex);
        Assert.Equal(0f, report.NearestDistance);

        Collision self = Assert.Single(report.Collisions);
        Assert.Equal(2, self.Index);
        Assert.Equal(PaletteWarningKind.TooClose, self.Kind);
    }

    /// <summary>
    /// The dialog asks this of an entry that is already in the palette, so without the
    /// exclusion every answer would be "itself, at zero" and the question would be useless.
    /// </summary>
    [Fact]
    public void IgnoringAnIndexAsksWhatThatEntryCollidesWith()
    {
        CollisionReport report = Collide.Check(WarmRamp[2], WarmRamp, ignoreIndex: 2);

        Assert.NotEqual(2, report.NearestIndex);
        Assert.DoesNotContain(report.Collisions, c => c.Index == 2);
        Assert.True(report.NearestDistance > 0f);
    }

    [Fact]
    public void AColourAHairFromAnEntryIsReportedTooClose()
    {
        // One step off entry 1 in every channel: indistinguishable, and a wasted slot.
        CollisionReport report = Collide.Check(Nudge(WarmRamp[1]), WarmRamp);

        Collision collision = Assert.Single(report.Collisions, c => c.Kind == PaletteWarningKind.TooClose);
        Assert.Equal(1, collision.Index);
        Assert.True(collision.Value < 0.02f, $"reported dE {collision.Value}");
    }

    [Fact]
    public void AColourMatchingAnEntrysLightnessButNotItsHueIsAGreyscaleCollision()
    {
        Rgb24[] palette = [new(196, 96, 96)];

        // Opposite hue, deliberately the same lightness: these merge when desaturated.
        CollisionReport report = Collide.Check(new Rgb24(64, 148, 148), palette);

        Collision collision = Assert.Single(report.Collisions);
        Assert.Equal(PaletteWarningKind.GreyscaleCollision, collision.Kind);
        Assert.True(collision.Value < 0.015f, $"reported L delta {collision.Value}");
    }

    [Fact]
    public void APairIsNeverBothTooCloseAndAGreyscaleCollision()
    {
        CollisionReport report = Collide.Check(Nudge(WarmRamp[1]), WarmRamp);

        Assert.All(report.Collisions, c => Assert.Equal(PaletteWarningKind.TooClose, c.Kind));
    }

    [Fact]
    public void AnEmptyPaletteCollidesWithNothingAndDoesNotThrow()
    {
        CollisionReport report = Collide.Check(new Rgb24(10, 20, 30), ReadOnlySpan<Rgb24>.Empty);

        Assert.Equal(-1, report.NearestIndex);
        Assert.Equal(0f, report.NearestDistance);
        Assert.Empty(report.Collisions);
        Assert.Empty(report.Placements);
    }

    [Fact]
    public void ACandidateSharingTheRampsHueIsPlacedInIt()
    {
        // Between entries 1 and 2 in lightness, same warm hue.
        CollisionReport report = Collide.Check(At(0.52f, 0.07f, 45f), WarmRamp);

        RampPlacement placement = Assert.Single(report.Placements);
        Assert.Equal(2, placement.PositionByLightness);
        Assert.True(placement.GapBelow > 0f && placement.GapAbove > 0f, "it lands between two entries");
    }

    [Fact]
    public void ACandidateOfADifferentHueIsPlacedInNoRamp()
    {
        CollisionReport report = Collide.Check(At(0.55f, 0.10f, 250f), WarmRamp);

        Assert.Empty(report.Placements);
    }

    [Fact]
    public void ACandidateLighterThanTheWholeRampExtendsIt()
    {
        CollisionReport report = Collide.Check(At(0.88f, 0.07f, 45f), WarmRamp);

        RampPlacement placement = Assert.Single(report.Placements);
        Assert.Equal(WarmRamp.Length, placement.PositionByLightness);
        Assert.True(placement.ExtendsProgression);
        Assert.Equal(0f, placement.GapAbove);
    }

    /// <summary>
    /// The measurement that earns its place: a colour landing in the middle of a ramp does not
    /// continue it, however good it looks. Whether that matters is the author's call.
    /// </summary>
    [Fact]
    public void ACandidateInTheMiddleOfARampDoesNotExtendIt()
    {
        CollisionReport report = Collide.Check(At(0.52f, 0.07f, 45f), WarmRamp);

        Assert.False(Assert.Single(report.Placements).ExtendsProgression);
    }

    [Fact]
    public void ANeutralCandidateJoinsTheNeutralRampAndNotAChromaticOne()
    {
        Rgb24[] palette = [.. WarmRamp, new(40, 40, 40), new(120, 120, 120), new(200, 200, 200)];

        CollisionReport report = Collide.Check(new Rgb24(160, 160, 160), palette);

        RampPlacement placement = Assert.Single(report.Placements);
        Assert.True(Ramp.Ramps(palette)[placement.RampIndex].IsNeutral);
    }

    /// <summary>
    /// The exclusion has to reach ramp detection, not just the lightness list afterwards. Two
    /// members left of a three-member ramp are below the minimum and are no longer a ramp.
    /// </summary>
    [Fact]
    public void AnIgnoredEntryCannotLeaveARampBelowTheMinimumBehind()
    {
        Rgb24[] threeStep = [At(0.30f, 0.07f, 45f), At(0.50f, 0.07f, 45f), At(0.70f, 0.07f, 45f)];

        CollisionReport report = Collide.Check(threeStep[1], threeStep, ignoreIndex: 1);

        Assert.Empty(report.Placements);
    }

    /// <summary>
    /// And where the ramp does survive, it must describe itself without the ignored entry:
    /// four members less one is a three-member ramp, not a four-member one.
    /// </summary>
    [Fact]
    public void ASurvivingRampReportsItsSizeWithoutTheIgnoredEntry()
    {
        CollisionReport report = Collide.Check(WarmRamp[0], WarmRamp, ignoreIndex: 0);

        RampPlacement placement = Assert.Single(report.Placements);
        Assert.Equal(WarmRamp.Length - 1, placement.MemberCount);
    }

    [Fact]
    public void ThresholdsComeFromRampOptionsSoOneSetOfNumbersGovernsBoth()
    {
        Rgb24 candidate = At(0.50f, 0.07f, 45f);

        Assert.Empty(Collide.Check(candidate, WarmRamp).Collisions);
        Assert.NotEmpty(Collide.Check(candidate, WarmRamp, new RampOptions { TooCloseDistance = 0.5f }).Collisions);
    }
}
