using Pixelfit.Core;
using Xunit;

namespace Pixelfit.Tests;

/// <summary>
/// Ordered dither trades spatial resolution for apparent colour resolution. The trade is only
/// honest if the threshold pattern is centred on zero: offsets that average below it darken
/// every dithered region by a fraction of a step, everywhere, silently.
/// </summary>
public sealed class DitherTests
{
    private static readonly Rgb24 Dark = new(20, 20, 20);
    private static readonly Rgb24 Light = new(230, 230, 230);

    /// <summary>
    /// The property that pins the pattern down. A colour <c>f</c> of the way from dark to light
    /// and its mirror at <c>1 - f</c> must together use all sixteen cells of the 4x4 block: as
    /// many cells land light for one as land dark for the other. That holds exactly when the
    /// sixteen offsets are symmetric about zero, and fails when they run -0.5 to +0.4375.
    /// </summary>
    /// <remarks>
    /// Testing a single midpoint field does not work. The uncentred pattern has one offset of
    /// exactly zero, and which entry that cell lands on is decided by where the midpoint
    /// happened to round in 8-bit sRGB, so it can score an even split by luck.
    /// </remarks>
    [Theory]
    [InlineData(0.20f)]
    [InlineData(0.30f)]
    [InlineData(0.40f)]
    [InlineData(0.45f)]
    public void TheDitherPatternIsSymmetricAboutZero(float fraction)
    {
        int lightAtF = LightCellsFor(fraction);
        int lightAtMirror = LightCellsFor(1f - fraction);

        Assert.True(
            lightAtF + lightAtMirror == 16,
            $"at {fraction:F2} {lightAtF}/16 cells went light and at {1f - fraction:F2} {lightAtMirror}/16 did; "
            + "a centred pattern makes those sum to 16");
    }

    [Fact]
    public void DitheringAColourMidwayBetweenTwoEntriesSplitsCellsEvenly()
    {
        Assert.Equal(8, LightCellsFor(0.5f));
    }

    [Fact]
    public void DitheringStillEmitsNothingButPaletteEntries()
    {
        Rgb24[] palette = [new(20, 30, 40), new(200, 190, 180), new(90, 140, 60)];
        CellGrid field = new(
            [.. Enumerable.Range(0, 64).Select(i => new Rgb24((byte)(i * 3), 128, (byte)(255 - (i * 3))))],
            8,
            8);

        CellGrid dithered = Pixelize.SnapToPalette(field, palette, dither: true);

        HashSet<Rgb24> allowed = [.. palette];
        Assert.All(dithered.Cells, c => Assert.Contains(c, allowed));
    }

    /// <summary>
    /// How many of a 4x4 block's cells land on the light entry when the whole block is one
    /// colour, <paramref name="fraction"/> of the way from the dark entry to the light one in
    /// OKLab.
    /// </summary>
    private static int LightCellsFor(float fraction)
    {
        OklabColor a = Oklab.FromSrgb(Dark);
        OklabColor b = Oklab.FromSrgb(Light);
        Rgb24 field = Oklab.ToSrgb(new OklabColor(
            a.L + ((b.L - a.L) * fraction),
            a.A + ((b.A - a.A) * fraction),
            a.B + ((b.B - a.B) * fraction)));

        CellGrid block = new([.. Enumerable.Repeat(field, 16)], 4, 4);
        CellGrid dithered = Pixelize.SnapToPalette(block, [Dark, Light], dither: true);

        return dithered.Cells.Count(c => c == Light);
    }
}
