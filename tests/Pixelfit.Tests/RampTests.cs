using Pixelfit.Core;
using Xunit;

namespace Pixelfit.Tests;

public sealed class RampTests
{
    /// <summary>
    /// A clean four-step warm ramp: monotonic in lightness, hues at 28, 39, 48 and 63 degrees
    /// so every neighbouring pair is inside the grouping tolerance.
    /// </summary>
    private static readonly Rgb24[] GoodWarmRamp =
    [
        new(74, 42, 38),
        new(140, 74, 52),
        new(204, 116, 68),
        new(246, 176, 112),
    ];

    [Fact]
    public void AWellFormedRampIsReportedAsMonotonic()
    {
        PaletteReport report = Ramp.Analyze(GoodWarmRamp);

        RampReport ramp = Assert.Single(report.Ramps);
        Assert.True(ramp.IsMonotonicLightness);
        Assert.Empty(ramp.OutOfOrderPositions);
        Assert.DoesNotContain(report.Warnings, w => w.Kind == PaletteWarningKind.NonMonotonicRamp);
    }

    [Fact]
    public void ARampWhoseLightnessReversesIsFlagged()
    {
        // Entries three and four swapped: the ramp still looks plausible in RGB.
        Rgb24[] palette =
        [
            GoodWarmRamp[0],
            GoodWarmRamp[1],
            GoodWarmRamp[3],
            GoodWarmRamp[2],
        ];

        PaletteReport report = Ramp.Analyze(palette);

        RampReport ramp = Assert.Single(report.Ramps);
        Assert.False(ramp.IsMonotonicLightness);
        Assert.Equal([4], ramp.OutOfOrderPositions);

        PaletteWarning warning = Assert.Single(report.Warnings, w => w.Kind == PaletteWarningKind.NonMonotonicRamp);
        Assert.Contains("reverses", warning.Message);
    }

    [Fact]
    public void ARampAuthoredLightToDarkIsAlsoMonotonic()
    {
        PaletteReport report = Ramp.Analyze([.. GoodWarmRamp.Reverse()]);

        RampReport ramp = Assert.Single(report.Ramps);
        Assert.True(ramp.IsMonotonicLightness);
    }

    [Fact]
    public void ACrampedStepShowsUpAsStepDeviation()
    {
        Rgb24[] even = GoodWarmRamp;
        Rgb24[] cramped =
        [
            GoodWarmRamp[0],
            GoodWarmRamp[1],
            new(150, 80, 56), // barely above the previous entry
            GoodWarmRamp[3],
        ];

        float evenDeviation = Assert.Single(Ramp.Analyze(even).Ramps).LightnessStepDeviation;
        float crampedDeviation = Assert.Single(Ramp.Analyze(cramped).Ramps).LightnessStepDeviation;

        Assert.True(
            crampedDeviation > evenDeviation,
            $"cramped sigma {crampedDeviation} should exceed even sigma {evenDeviation}");
    }

    [Fact]
    public void TwoEntriesTooCloseTogetherAreReportedAsOnePair()
    {
        Rgb24[] palette = [new(138, 89, 64), new(139, 90, 60), new(40, 120, 200), new(230, 230, 230)];

        PaletteReport report = Ramp.Analyze(palette);

        PaletteWarning warning = Assert.Single(report.Warnings, w => w.Kind == PaletteWarningKind.TooClose);
        Assert.Equal([0, 1], warning.Indices);
        Assert.True(warning.Value < 0.02f);
    }

    [Fact]
    public void EntriesThatMergeInGreyscaleAreReported()
    {
        // Very different hue, nearly identical lightness.
        Rgb24[] palette = [new(196, 96, 96), new(64, 148, 148), new(20, 20, 20)];

        PaletteReport report = Ramp.Analyze(palette);

        PaletteWarning warning = Assert.Single(report.Warnings, w => w.Kind == PaletteWarningKind.GreyscaleCollision);
        Assert.Equal([0, 1], warning.Indices);
        Assert.Contains("desaturated", warning.Message);
    }

    [Fact]
    public void APairIsNeverBothTooCloseAndAGreyscaleCollision()
    {
        Rgb24[] palette = [new(138, 89, 64), new(139, 90, 60)];

        PaletteReport report = Ramp.Analyze(palette);

        Assert.Single(report.Warnings, w => w.Kind == PaletteWarningKind.TooClose);
        Assert.DoesNotContain(report.Warnings, w => w.Kind == PaletteWarningKind.GreyscaleCollision);
    }

    [Fact]
    public void HueShiftIsReportedAsAFactAndNeverAsAWarning()
    {
        PaletteReport report = Ramp.Analyze(GoodWarmRamp);

        RampReport ramp = Assert.Single(report.Ramps);
        Assert.NotEqual(0f, ramp.HueShiftDegrees);

        // Whether to shift hue is the author's call. Nothing in the report may nudge them.
        Assert.DoesNotContain(report.Warnings, w =>
            w.Message.Contains("hue", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ARampWithNoHueShiftReportsZero()
    {
        // Same hue and chroma direction throughout, lightness only.
        Rgb24[] palette = [new(40, 40, 40), new(120, 120, 120), new(200, 200, 200)];

        PaletteReport report = Ramp.Analyze(palette);

        RampReport ramp = Assert.Single(report.Ramps);
        Assert.True(ramp.IsNeutral);
        Assert.Equal(0f, ramp.HueShiftDegrees);
        Assert.Null(ramp.HueDegrees);
    }

    [Fact]
    public void TwoDistinctHuesProduceTwoRamps()
    {
        Rgb24[] palette =
        [
            .. GoodWarmRamp,
            new(28, 48, 88),
            new(56, 96, 154),
            new(104, 158, 212),
        ];

        PaletteReport report = Ramp.Analyze(palette);

        Assert.Equal(2, report.Ramps.Count);
        Assert.Equal([0, 1, 2, 3], report.Ramps[0].Indices);
        Assert.Equal([4, 5, 6], report.Ramps[1].Indices);
    }

    [Fact]
    public void ARampStraddlingZeroDegreesStaysWhole()
    {
        // Built in OKLCh so the hues are exactly where the test says they are: two entries
        // just below the 0/360 seam and two just above it. Naive grouping puts these in two
        // ramps 350 degrees apart.
        Rgb24[] palette =
        [
            .. new[] { 346f, 356f, 6f, 16f }
                .Select((hue, i) => Oklab.ToSrgb(Oklab.FromOklch(new OklchColor(0.32f + (i * 0.15f), 0.08f, hue)))),
        ];

        PaletteReport report = Ramp.Analyze(palette);

        RampReport ramp = Assert.Single(report.Ramps);
        Assert.Equal(4, ramp.Indices.Count);
        Assert.True(ramp.IsMonotonicLightness);
    }

    [Fact]
    public void UnusedEntriesAreReportedOnlyWhenAnImageIsSupplied()
    {
        Rgb24[] palette = [.. GoodWarmRamp, new(46, 31, 61)];
        Rgb24[] used = [GoodWarmRamp[0], GoodWarmRamp[1], GoodWarmRamp[2], GoodWarmRamp[3]];

        Assert.DoesNotContain(Ramp.Analyze(palette).Warnings, w => w.Kind == PaletteWarningKind.Unused);

        PaletteWarning warning = Assert.Single(
            Ramp.Analyze(palette, imageColors: used).Warnings,
            w => w.Kind == PaletteWarningKind.Unused);
        Assert.Equal([4], warning.Indices);
    }

    [Fact]
    public void FewerThanThreeEntriesIsNotARamp()
    {
        Rgb24[] palette = [GoodWarmRamp[0], GoodWarmRamp[3]];
        Assert.Empty(Ramp.Analyze(palette).Ramps);
    }

    [Fact]
    public void AnEmptyPaletteProducesAnEmptyReport()
    {
        PaletteReport report = Ramp.Analyze(ReadOnlySpan<Rgb24>.Empty);

        Assert.Empty(report.Entries);
        Assert.Empty(report.Ramps);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void NoTwoFixturePaletteEntriesAreIndistinguishable()
    {
        // If two fixture entries were a hair apart, the round-trip test would be measuring
        // which of them a reduced cell happened to land nearer, not whether the pipeline works.
        PaletteReport report = Ramp.Analyze(FixtureSprites.PaletteEntries());

        Assert.DoesNotContain(report.Warnings, w => w.Kind == PaletteWarningKind.TooClose);
    }

    [Fact]
    public void TheFixturePalettesGreyscaleCollisionsAreReported()
    {
        // The backdrop blocks sit at the same lightness as two entries of the warm ramp.
        // They stay far apart in chroma, so nothing in the pipeline confuses them — but
        // desaturate the image and they merge, which is exactly the kind of thing that is
        // invisible until someone looks for it.
        PaletteReport report = Ramp.Analyze(FixtureSprites.PaletteEntries());

        Assert.Contains(report.Warnings, w => w.Kind == PaletteWarningKind.GreyscaleCollision);
        Assert.All(
            report.Warnings.Where(w => w.Kind == PaletteWarningKind.GreyscaleCollision),
            w => Assert.True(w.Value < 0.015f, $"{w.Message} reported at L delta {w.Value}"));
    }

    [Fact]
    public void TheFixturesWarmRampReadsAsAProgression()
    {
        PaletteReport report = Ramp.Analyze(FixtureSprites.PaletteEntries());

        RampReport warm = Assert.Single(report.Ramps, r => r.HueDegrees is > 0f and < 90f);
        Assert.True(warm.IsMonotonicLightness);
        Assert.Equal(4, warm.Indices.Count);
    }

    [Fact]
    public void InterleavedFamiliesOfOneHueAreReportedAsOneNonMonotonicRamp()
    {
        // The fixture palette lists two blue families that are not contiguous: the two
        // backdrop blocks and the three-step cool ramp. They share a hue, so they are one
        // ramp, and in palette order their lightness does not climb. That is a real
        // structural observation about the palette, and the tool is supposed to make it.
        PaletteReport report = Ramp.Analyze(FixtureSprites.PaletteEntries());

        RampReport cool = Assert.Single(report.Ramps, r => r.HueDegrees is > 180f and < 320f);
        Assert.False(cool.IsMonotonicLightness);
        Assert.NotEmpty(cool.OutOfOrderPositions);
    }

    /// <summary>
    /// Pair checks are quadratic, and a set of colours read out of an image is not a palette:
    /// 4096 entries make 8.4 million pairs, of which enough match to bury the report. The
    /// listing has to stop somewhere, and where it stopped has to be said.
    /// </summary>
    [Fact]
    public void PairWarningsStopListingAtTheCapAndReportTheRemainder()
    {
        // A tolerance this wide makes every pair too close, so the count is exactly known:
        // 20 entries are 190 pairs, of which 10 are listed and 180 are not.
        Rgb24[] palette = [.. Enumerable.Range(0, 20).Select(i => new Rgb24((byte)i, (byte)i, (byte)i))];
        RampOptions options = new() { TooCloseDistance = 10f, MaxPairWarningsPerKind = 10 };

        PaletteWarning[] tooClose =
            [.. Ramp.Analyze(palette, options).Warnings.Where(w => w.Kind == PaletteWarningKind.TooClose)];

        Assert.Equal(11, tooClose.Length);
        Assert.Equal(10, tooClose.Count(w => w.Indices.Count == 2));

        PaletteWarning remainder = Assert.Single(tooClose, w => w.Indices.Count == 0);
        Assert.Equal(180f, remainder.Value);
        Assert.Contains("180 more", remainder.Message);
    }

    [Fact]
    public void APaletteUnderTheCapIsListedInFullAndSaysNothingAboutAnyRemainder()
    {
        PaletteReport report = Ramp.Analyze(FixtureSprites.PaletteEntries());

        Assert.All(report.Warnings, w => Assert.NotEmpty(w.Indices));
        Assert.DoesNotContain(report.Warnings, w => w.Message.Contains("listing stopped"));
    }
}
