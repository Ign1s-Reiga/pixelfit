using Pixelfit.Core;
using Xunit;

namespace Pixelfit.Tests;

/// <summary>
/// The first test written. Every downstream check is meaningless until these pass.
/// </summary>
public sealed class OklabRoundTripTests
{
    [Fact]
    public void SrgbToOklabToSrgb_IsExactForEveryGreyLevel()
    {
        for (int v = 0; v <= 255; v++)
        {
            Rgb24 input = new((byte)v, (byte)v, (byte)v);
            Rgb24 output = Oklab.ToSrgb(Oklab.FromSrgb(input));
            Assert.Equal(input, output);
        }
    }

    [Fact]
    public void SrgbToOklabToSrgb_IsExactAtTheGamutCorners()
    {
        byte[] extremes = [0, 255];
        foreach (byte r in extremes)
        {
            foreach (byte g in extremes)
            {
                foreach (byte b in extremes)
                {
                    Rgb24 input = new(r, g, b);
                    Assert.Equal(input, Oklab.ToSrgb(Oklab.FromSrgb(input)));
                }
            }
        }
    }

    [Fact]
    public void SrgbToOklabToSrgb_IsWithinOneStepAcrossTheFullCube()
    {
        // Every 3rd level on each axis: 86^3 = 636,056 colours, including 0 and 255.
        int worst = 0;
        Rgb24 worstInput = default;

        for (int r = 0; r <= 255; r += 3)
        {
            for (int g = 0; g <= 255; g += 3)
            {
                for (int b = 0; b <= 255; b += 3)
                {
                    Rgb24 input = new((byte)r, (byte)g, (byte)b);
                    Rgb24 output = Oklab.ToSrgb(Oklab.FromSrgb(input));

                    int delta = Math.Max(
                        Math.Abs(input.R - output.R),
                        Math.Max(Math.Abs(input.G - output.G), Math.Abs(input.B - output.B)));

                    if (delta > worst)
                    {
                        worst = delta;
                        worstInput = input;
                    }
                }
            }
        }

        Assert.True(worst <= 1, $"worst round-trip error was {worst}/255 at {worstInput}");
    }

    [Fact]
    public void SrgbToOklabToSrgb_CoversEveryLevelOnEachAxisIncluding255()
    {
        for (int v = 0; v <= 255; v++)
        {
            foreach (Rgb24 input in new[]
            {
                new Rgb24((byte)v, 0, 0),
                new Rgb24(0, (byte)v, 0),
                new Rgb24(0, 0, (byte)v),
                new Rgb24((byte)v, 255, 128),
            })
            {
                Rgb24 output = Oklab.ToSrgb(Oklab.FromSrgb(input));
                Assert.True(
                    Math.Abs(input.R - output.R) <= 1
                    && Math.Abs(input.G - output.G) <= 1
                    && Math.Abs(input.B - output.B) <= 1,
                    $"{input} round-tripped to {output}");
            }
        }
    }

    [Fact]
    public void TransferFunction_UsesTheLinearSegmentNotAPureGamma()
    {
        // Below 0.04045 the curve is a straight line. A Pow(x, 2.2) approximation
        // is visibly wrong here, which is exactly where outline colours live.
        Assert.Equal(0.01f / 12.92f, Oklab.SrgbToLinear(0.01f), 6);
        Assert.Equal(0f, Oklab.SrgbToLinear(0f), 6);
        Assert.Equal(1f, Oklab.SrgbToLinear(1f), 6);
    }

    [Fact]
    public void OklabToOklchToOklab_RoundTrips()
    {
        for (int r = 0; r <= 255; r += 17)
        {
            for (int g = 0; g <= 255; g += 17)
            {
                for (int b = 0; b <= 255; b += 17)
                {
                    OklabColor lab = Oklab.FromSrgb(new Rgb24((byte)r, (byte)g, (byte)b));
                    OklabColor back = Oklab.FromOklch(Oklab.ToOklch(lab));

                    Assert.True(MathF.Abs(lab.L - back.L) < 1e-5f, $"L {lab.L} vs {back.L}");
                    Assert.True(MathF.Abs(lab.A - back.A) < 1e-5f, $"a {lab.A} vs {back.A}");
                    Assert.True(MathF.Abs(lab.B - back.B) < 1e-5f, $"b {lab.B} vs {back.B}");
                }
            }
        }
    }

    [Fact]
    public void Cbrt_HandlesNegativeIntermediatesWithoutNaN()
    {
        // An out-of-gamut linear triple must still produce finite OKLab.
        OklabColor lab = Oklab.FromLinear(-0.05f, 1.2f, -0.01f);
        Assert.False(float.IsNaN(lab.L) || float.IsNaN(lab.A) || float.IsNaN(lab.B));
    }

    [Fact]
    public void HueDelta_TreatsTheHueCircleAsACircle()
    {
        Assert.Equal(2f, Oklab.HueDelta(359f, 1f), 4);
        Assert.Equal(-2f, Oklab.HueDelta(1f, 359f), 4);
        Assert.Equal(10f, Oklab.HueDelta(20f, 30f), 4);
    }

    [Fact]
    public void NearestInPalette_ReturnsTheExactEntryWhenThePixelIsAPaletteColour()
    {
        Rgb24[] palette =
        [
            new(26, 28, 44), new(93, 39, 93), new(177, 62, 83), new(239, 125, 87),
            new(255, 205, 117), new(167, 240, 112), new(56, 183, 100), new(37, 113, 121),
        ];

        OklabColor[] lab = Oklab.ToOklab(palette);

        for (int i = 0; i < palette.Length; i++)
        {
            int found = Oklab.NearestInPalette(Oklab.FromSrgb(palette[i]), lab);
            Assert.Equal(i, found);
        }
    }

    [Fact]
    public void NearestInPalette_ReturnsMinusOneForAnEmptyPalette()
    {
        Assert.Equal(-1, Oklab.NearestInPalette(default, ReadOnlySpan<OklabColor>.Empty));
    }
}
