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
