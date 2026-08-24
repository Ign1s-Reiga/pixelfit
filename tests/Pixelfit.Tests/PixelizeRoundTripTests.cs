using Pixelfit.Core;
using Xunit;

namespace Pixelfit.Tests;

/// <summary>
/// The load-bearing test. Real pixel art, upscaled and degraded the way an image that only
/// looks like pixel art arrives, must come back out pixel-identical.
/// </summary>
public sealed class PixelizeRoundTripTests
{
    /// <summary>
    /// 6 and 11 are the ones that matter. 8 divides evenly into the cell insets and the
    /// quantiser's bins, and can pass with a phase bug still in place.
    /// </summary>
    public static readonly int[] Factors = [6, 8, 11];

    public static TheoryData<string, int> Cases()
    {
        TheoryData<string, int> data = [];
        foreach ((string name, _, _, _) in FixtureGenerator.Originals())
        {
            foreach (int factor in Factors)
            {
                data.Add(name, factor);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void UpscaledAndNoisedArtRoundTripsToTheOriginal(string name, int factor)
    {
        (byte[] original, int width, int height, byte[] degraded, int degradedWidth, int degradedHeight) =
            LoadPair(name, factor);

        // The grid size is given, which is the override that ships from day one. Estimation
        // is exercised separately, against inputs that carry enough evidence to support it.
        PixelizeResult result = Pixelize.Run(
            degraded,
            degradedWidth,
            degradedHeight,
            new PixelizeOptions
            {
                GridSize = factor,
                Phase = (0, 0),
                Palette = FixtureSprites.PaletteEntries(),
            });

        Assert.Equal(width, result.Cells.Width);
        Assert.Equal(height, result.Cells.Height);

        byte[] actual = Pixelize.ToRgbBytes(result.Cells);
        (int differing, string firstDifference) = TestImage.Compare(actual, original, width);

        Assert.True(
            differing == 0,
            $"{name}@{factor}x: {differing}/{width * height} pixels differ; first at {firstDifference}");
    }

    /// <summary>
    /// The same assertion with nothing overridden at all — size and phase both estimated.
    /// </summary>
    /// <remarks>
    /// The art here changes colour at nearly every logical pixel, and that is the point.
    /// A grid leaves no trace at boundaries where the colour does not change, so a period can
    /// only be recovered from art with detail at the single-pixel scale. The committed
    /// fixtures deliberately have none — every feature in them is at least two pixels across,
    /// because that is what survives interpolation — so asking the estimator to find their
    /// grid would be asking it to read evidence that is not in the image. The two demands
    /// genuinely pull against each other, which is why the grid override ships from day one.
    /// </remarks>
    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(11)]
    public void FullyEstimatedPipelineRoundTripsOnArtWithSinglePixelDetail(int factor)
    {
        const int spriteSize = 32;
        byte[] art = SyntheticSprite.Create(spriteSize, spriteSize, seed: 19, SyntheticSprite.Sweetie16);
        byte[] scaled = SyntheticSprite.Upscale(art, spriteSize, spriteSize, factor);

        PixelizeResult result = Pixelize.Run(
            scaled,
            spriteSize * factor,
            spriteSize * factor,
            new PixelizeOptions { Palette = SyntheticSprite.Sweetie16 });

        Assert.Equal(factor, result.Grid.Size);
        Assert.Equal(0, result.Grid.PhaseX);
        Assert.Equal(0, result.Grid.PhaseY);

        (int differing, string first) = TestImage.Compare(
            Pixelize.ToRgbBytes(result.Cells),
            art,
            spriteSize);

        Assert.True(differing == 0, $"x{factor}: {differing} pixels differ; first at {first}");
    }

    [Fact]
    public void OutputContainsOnlyPaletteColoursWhenAPaletteIsGiven()
    {
        (byte[] _, int _, int _, byte[] degraded, int degradedWidth, int degradedHeight) =
            LoadPair("torch", 6);

        Rgb24[] palette = FixtureSprites.PaletteEntries();
        PixelizeResult result = Pixelize.Run(
            degraded,
            degradedWidth,
            degradedHeight,
            new PixelizeOptions { GridSize = 6, Palette = palette });

        HashSet<Rgb24> allowed = [.. palette];
        Assert.All(result.Cells.Cells, c => Assert.Contains(c, allowed));
    }

    [Fact]
    public void DitheringStillOnlyEmitsPaletteColours()
    {
        (byte[] _, int _, int _, byte[] degraded, int degradedWidth, int degradedHeight) =
            LoadPair("mosaic", 8);

        Rgb24[] palette = FixtureSprites.PaletteEntries();
        PixelizeResult result = Pixelize.Run(
            degraded,
            degradedWidth,
            degradedHeight,
            new PixelizeOptions { GridSize = 8, Palette = palette, Dither = true });

        HashSet<Rgb24> allowed = [.. palette];
        Assert.All(result.Cells.Cells, c => Assert.Contains(c, allowed));
    }

    [Fact]
    public void WithoutAPaletteTheCellColoursAreLeftAlone()
    {
        (byte[] _, int width, int height, byte[] degraded, int degradedWidth, int degradedHeight) =
            LoadPair("slime", 8);

        PixelizeResult result = Pixelize.Run(
            degraded,
            degradedWidth,
            degradedHeight,
            new PixelizeOptions { GridSize = 8 });

        Assert.Equal(width * height, result.Cells.Cells.Length);
    }

    [Fact]
    public void ManualPhaseOverrideIsHonouredExactly()
    {
        byte[] art = FixtureSprites.Rasterize(FixtureSprites.Slime, out int width, out int height);
        byte[] scaled = SyntheticSprite.Upscale(art, width, height, 7);

        // Drop three columns and two rows, so the first whole cell no longer starts at zero.
        byte[] cropped = SyntheticSprite.Crop(scaled, width * 7, 3, 2, (width * 7) - 7, (height * 7) - 7);

        PixelizeResult result = Pixelize.Run(
            cropped,
            (width * 7) - 7,
            (height * 7) - 7,
            new PixelizeOptions
            {
                GridSize = 7,
                Phase = (4, 5),
                Palette = FixtureSprites.PaletteEntries(),
            });

        Assert.Equal(7, result.Grid.Size);
        Assert.Equal(4, result.Grid.PhaseX);
        Assert.Equal(5, result.Grid.PhaseY);
    }

    [Fact]
    public void MagnifyRestoresTheOriginalDimensions()
    {
        (byte[] original, int width, int height, byte[] degraded, int dw, int dh) = LoadPair("shield", 6);

        PixelizeResult result = Pixelize.Run(
            degraded,
            dw,
            dh,
            new PixelizeOptions { GridSize = 6, Phase = (0, 0), Palette = FixtureSprites.PaletteEntries() });

        byte[] magnified = Pixelize.Magnify(result.Cells, 6, out int mw, out int mh);

        Assert.Equal(width * 6, mw);
        Assert.Equal(height * 6, mh);

        // Every source pixel of a magnified cell is that cell, so sampling any one recovers it.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int cell = (((y * 6) * mw) + (x * 6)) * 3;
                int source = ((y * width) + x) * 3;
                Assert.Equal(original[source], magnified[cell]);
            }
        }
    }

    private static (byte[] Original, int Width, int Height, byte[] Degraded, int DegradedWidth, int DegradedHeight)
        LoadPair(string name, int factor)
    {
        string originalPath = Path.Combine(TestImage.FixtureDirectory, $"{name}.png");
        string degradedPath = Path.Combine(TestImage.FixtureDirectory, $"{name}@{factor}x.png");

        Assert.True(File.Exists(originalPath), $"missing fixture {originalPath}; run FixtureGenerator");
        Assert.True(File.Exists(degradedPath), $"missing fixture {degradedPath}; run FixtureGenerator");

        byte[] original = TestImage.Load(originalPath, out int width, out int height);
        byte[] degraded = TestImage.Load(degradedPath, out int degradedWidth, out int degradedHeight);
        return (original, width, height, degraded, degradedWidth, degradedHeight);
    }
}
