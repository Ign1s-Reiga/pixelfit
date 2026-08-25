using Pixelfit.Core;
using Xunit;

namespace Pixelfit.Tests;

/// <summary>
/// Extraction reports which colours carry an image. The load-bearing assertion is the first
/// one: every colour it returns is a colour the image contains, copied verbatim.
/// </summary>
public sealed class ExtractTests
{
    private const int Sprite = 32;
    private const int Seed = 4242;

    /// <summary>
    /// The invariant the whole design is arranged around. A cluster centre is a mean, and
    /// emitting one would invent a colour present nowhere in the source.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void EveryExtractedColourIsPresentInTheImage(int count)
    {
        byte[] image = Degraded(out int width, out int height);

        Rgb24[] palette = Extract.Palette(image, width, height, count);

        HashSet<Rgb24> present = [.. Pixelize.UniqueColors(image, width, height, int.MaxValue)];
        Assert.All(palette, c => Assert.Contains(c, present));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(16)]
    public void TheRequestedNumberOfColoursComesBack(int count)
    {
        byte[] image = Degraded(out int width, out int height);

        Assert.Equal(count, Extract.Palette(image, width, height, count).Length);
    }

    /// <summary>A palette that changes between runs of the same image would be unusable.</summary>
    [Fact]
    public void ExtractionIsDeterministic()
    {
        byte[] image = Degraded(out int width, out int height);

        Assert.Equal(
            Extract.Palette(image, width, height, 12),
            Extract.Palette(image, width, height, 12));
    }

    /// <summary>
    /// Weighting by pixel count, not by distinct colour. A colour covering most of the image
    /// has to survive however many one-off noise colours surround it.
    /// </summary>
    [Fact]
    public void ADominantColourSurvivesAFieldOfNoise()
    {
        const int side = 64;
        Rgb24 dominant = new(200, 60, 40);
        byte[] image = new byte[side * side * 3];
        Random random = new(Seed);

        for (int i = 0; i < side * side; i++)
        {
            // Nine tenths one colour; the rest scattered and all different.
            Rgb24 c = i % 10 == 0
                ? new Rgb24((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256))
                : dominant;

            image[i * 3] = c.R;
            image[(i * 3) + 1] = c.G;
            image[(i * 3) + 2] = c.B;
        }

        Assert.Contains(dominant, Extract.Palette(image, side, side, 8));
    }

    [Fact]
    public void AnImageWithFewerColoursThanAskedForReturnsWhatItHas()
    {
        Rgb24[] three = [new(10, 20, 30), new(200, 30, 40), new(240, 240, 240)];
        byte[] image = new byte[three.Length * 3];
        for (int i = 0; i < three.Length; i++)
        {
            image[i * 3] = three[i].R;
            image[(i * 3) + 1] = three[i].G;
            image[(i * 3) + 2] = three[i].B;
        }

        Rgb24[] palette = Extract.Palette(image, three.Length, 1, 16);

        Assert.Equal(3, palette.Length);
        Assert.All(palette, c => Assert.Contains(c, three));
    }

    /// <summary>
    /// The acceptance case. A sprite drawn from 16 colours, upscaled and noised into tens of
    /// thousands, should give back a palette that reconstructs it — which is the whole reason
    /// to extract one.
    /// </summary>
    [Fact]
    public void AnExtractedPaletteReconstructsTheSpriteItCameFrom()
    {
        byte[] art = SyntheticSprite.Create(Sprite, Sprite, Seed, SyntheticSprite.Sweetie16);
        byte[] degraded = TestImage.AddNoise(SyntheticSprite.Upscale(art, Sprite, Sprite, 7), Seed, 4);
        int side = Sprite * 7;

        Rgb24[] extracted = Extract.Palette(degraded, side, side, SyntheticSprite.Sweetie16.Length);

        PixelizeResult result = Pixelize.Run(
            degraded,
            side,
            side,
            new PixelizeOptions { GridSize = 7, Phase = (0, 0), Palette = extracted });

        // Pixel identity is the wrong test here, and asking for it was a mistake worth
        // recording: extraction can only return colours the image actually contains, and after
        // noise those are all a shade off the pristine ones the sprite was drawn from. Every
        // pixel differs by a value or two and the sprite is nonetheless correct. What has to
        // hold is that each reconstructed pixel is perceptually the colour it should be.
        (float mean, float visible) = Difference(Pixelize.ToRgbBytes(result.Cells), art);

        // Mean, not worst. Sixteen clusters over a noisy image will not always give a rarely
        // used source colour its own cluster, so a handful of pixels land on a neighbour and
        // the worst case says more about the rarest colour than about the palette.
        Assert.True(mean < 0.02f, $"mean dE {mean:F3}, {visible:P1} of pixels visibly off");
        Assert.True(visible < 0.05f, $"{visible:P1} of pixels differ by more than dE 0.05");
    }

    /// <summary>
    /// Mean OKLab distance pixel for pixel, and the fraction differing by enough to see.
    /// </summary>
    private static (float Mean, float VisiblyOff) Difference(byte[] actual, byte[] expected)
    {
        double total = 0;
        int off = 0;
        int pixels = expected.Length / 3;

        for (int i = 0; i < pixels; i++)
        {
            int o = i * 3;
            float d = Oklab.Distance(
                Oklab.FromSrgb(actual[o], actual[o + 1], actual[o + 2]),
                Oklab.FromSrgb(expected[o], expected[o + 1], expected[o + 2]));

            total += d;
            if (d > 0.05f)
            {
                off++;
            }
        }

        return ((float)(total / pixels), (float)off / pixels);
    }

    /// <summary>
    /// A bin holds a range of colours, so occupied bins are not distinct colours. Three shades
    /// inside one 5-bit bin are still three colours, and asking for two has to give two.
    /// </summary>
    [Fact]
    public void ColoursSharingOneBinAreStillDistinctColours()
    {
        Rgb24[] sameBin = [new(0, 0, 0), new(3, 3, 3), new(7, 7, 7)];
        byte[] image = Row(sameBin);

        Rgb24[] palette = Extract.Palette(image, sameBin.Length, 1, 2);

        Assert.Equal(2, palette.Length);
        Assert.All(palette, c => Assert.Contains(c, sameBin));
    }

    /// <summary>
    /// A bin's representative has to answer to its population, not to which pixel the scan
    /// reached first. Otherwise the same colours in a different order give a different palette.
    /// </summary>
    [Fact]
    public void ABinIsNamedByItsPopulationAndNotByScanOrder()
    {
        // One black pixel, then a hundred near-white ones — all inside the same 5-bit bin.
        List<Rgb24> pixels = [new(0, 0, 0)];
        pixels.AddRange(Enumerable.Repeat(new Rgb24(7, 7, 7), 100));

        Rgb24 chosen = Assert.Single(Extract.Palette(Row([.. pixels]), pixels.Count, 1, 1));
        Assert.Equal(new Rgb24(7, 7, 7), chosen);

        // And reversing the image must not change the answer.
        pixels.Reverse();
        Assert.Equal(chosen, Assert.Single(Extract.Palette(Row([.. pixels]), pixels.Count, 1, 1)));
    }

    /// <summary>
    /// Two colours equidistant from their bin's centre are equally good representatives, so
    /// something has to break the tie — and if that something is scan order, the same colours
    /// arranged differently give different palettes.
    /// </summary>
    [Fact]
    public void ABinWithATiedRepresentativeStillIgnoresScanOrder()
    {
        // (1,1,1) and (5,5,5) share a bin and sit either side of its centre, so nothing but a
        // tiebreak separates them. Three further bins keep the count above the request, which
        // is what routes this through binning rather than the exact-colour path.
        Rgb24[] tied =
        [
            new(1, 1, 1), new(5, 5, 5),
            new(80, 80, 80), new(150, 150, 150), new(220, 220, 220),
        ];

        Rgb24[] forward = Extract.Palette(Row(tied), tied.Length, 1, 3);
        Rgb24[] backward = Extract.Palette(Row([.. tied.Reverse()]), tied.Length, 1, 3);

        Assert.Equal(forward, backward);
    }

    private static byte[] Row(Rgb24[] colors)
    {
        byte[] rgb = new byte[colors.Length * 3];
        for (int i = 0; i < colors.Length; i++)
        {
            rgb[i * 3] = colors[i].R;
            rgb[(i * 3) + 1] = colors[i].G;
            rgb[(i * 3) + 2] = colors[i].B;
        }

        return rgb;
    }

    [Fact]
    public void AskingForNoColoursIsAnError()
    {
        byte[] image = Degraded(out int width, out int height);

        Assert.Throws<ArgumentOutOfRangeException>(() => Extract.Palette(image, width, height, 0));
    }

    /// <summary>A sprite upscaled and noised, which is the shape of image this exists for.</summary>
    private static byte[] Degraded(out int width, out int height)
    {
        byte[] art = SyntheticSprite.Create(Sprite, Sprite, Seed, SyntheticSprite.Sweetie16);
        width = Sprite * 6;
        height = Sprite * 6;
        return TestImage.AddNoise(TestImage.UpscaleBicubic(art, Sprite, Sprite, 6), Seed, 4);
    }
}
