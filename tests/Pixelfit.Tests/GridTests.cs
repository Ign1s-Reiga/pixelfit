using Pixelfit.Core;
using Xunit;

namespace Pixelfit.Tests;

public sealed class GridTests
{
    // 6 and 11 are the ones that matter: a power of two can pass by accident.
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(16)]
    public void EstimateGridSize_RecoversTheUpscaleFactor(int factor)
    {
        const int spriteSize = 32;
        byte[] sprite = SyntheticSprite.Create(spriteSize, spriteSize, seed: 7, SyntheticSprite.Sweetie16);
        byte[] scaled = SyntheticSprite.Upscale(sprite, spriteSize, spriteSize, factor);

        int size = Grid.EstimateGridSize(scaled, spriteSize * factor, spriteSize * factor);

        Assert.Equal(factor, size);
    }

    [Theory]
    [InlineData(6, 0, 0)]
    [InlineData(6, 1, 4)]
    [InlineData(6, 5, 3)]
    [InlineData(8, 3, 7)]
    [InlineData(11, 4, 9)]
    [InlineData(11, 10, 1)]
    public void EstimatePhase_RecoversTheOffsetOfTheFirstCellBoundary(int factor, int cropX, int cropY)
    {
        const int spriteSize = 32;
        byte[] sprite = SyntheticSprite.Create(spriteSize, spriteSize, seed: 11, SyntheticSprite.Sweetie16);
        byte[] scaled = SyntheticSprite.Upscale(sprite, spriteSize, spriteSize, factor);

        int scaledSize = spriteSize * factor;
        int croppedWidth = scaledSize - factor;
        byte[] cropped = SyntheticSprite.Crop(scaled, scaledSize, cropX, cropY, croppedWidth, croppedWidth);

        GridEstimate estimate = Grid.Estimate(cropped, croppedWidth, croppedWidth);

        Assert.Equal(factor, estimate.Size);
        Assert.Equal((factor - cropX) % factor, estimate.PhaseX);
        Assert.Equal((factor - cropY) % factor, estimate.PhaseY);
    }

    [Fact]
    public void Estimate_ReportsSizeOneForArtworkThatIsAlreadyPixelArt()
    {
        const int spriteSize = 64;
        byte[] sprite = SyntheticSprite.Create(spriteSize, spriteSize, seed: 3, SyntheticSprite.Sweetie16);

        GridEstimate estimate = Grid.Estimate(sprite, spriteSize, spriteSize);

        Assert.Equal(1, estimate.Size);
    }

    [Fact]
    public void Estimate_ReportsSizeOneForAFlatImageWithNoStructure()
    {
        byte[] flat = new byte[64 * 64 * 3];
        Array.Fill(flat, (byte)200);

        Assert.Equal(1, Grid.EstimateGridSize(flat, 64, 64));
    }

    [Fact]
    public void Estimate_FlagsDisagreementWhenTheAxesHaveDifferentPeriods()
    {
        // Deliberately anisotropic: 6 across, 10 down.
        const int spriteSize = 24;
        byte[] sprite = SyntheticSprite.Create(spriteSize, spriteSize, seed: 5, SyntheticSprite.Sweetie16);
        byte[] wide = Stretch(sprite, spriteSize, spriteSize, 6, 10);

        GridEstimate estimate = Grid.Estimate(wide, spriteSize * 6, spriteSize * 10);

        Assert.True(estimate.AxesDisagree, $"expected disagreement, got x={estimate.SizeX} y={estimate.SizeY}");
        Assert.Equal(Math.Min(estimate.SizeX, estimate.SizeY), estimate.Size);
    }

    [Fact]
    public void Estimate_DoesNotThrowOnAnImageSmallerThanTheSearchWindow()
    {
        byte[] tiny = new byte[3 * 3 * 3];
        GridEstimate estimate = Grid.Estimate(tiny, 3, 3);
        Assert.Equal(1, estimate.Size);
    }

    private static byte[] Stretch(ReadOnlySpan<byte> rgb, int width, int height, int factorX, int factorY)
    {
        int outWidth = width * factorX;
        int outHeight = height * factorY;
        byte[] result = new byte[outWidth * outHeight * 3];

        for (int y = 0; y < outHeight; y++)
        {
            int srcRow = (y / factorY) * width;
            for (int x = 0; x < outWidth; x++)
            {
                int src = (srcRow + (x / factorX)) * 3;
                int dst = ((y * outWidth) + x) * 3;
                result[dst] = rgb[src];
                result[dst + 1] = rgb[src + 1];
                result[dst + 2] = rgb[src + 2];
            }
        }

        return result;
    }
}
