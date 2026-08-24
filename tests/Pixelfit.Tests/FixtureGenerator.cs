using Xunit;

namespace Pixelfit.Tests;

/// <summary>
/// Writes the fixture PNGs into the source tree. The generated inputs are committed so the
/// round-trip test is deterministic; this exists so the recipe that produced them is too.
/// </summary>
/// <remarks>
/// Run with: <c>PIXELFIT_WRITE_FIXTURES=1 dotnet test --filter FixtureGenerator</c>
/// </remarks>
public sealed class FixtureGenerator
{
    private const int NoiseSeed = 20240824;
    private const int NoiseAmplitude = 4;

    [Fact]
    public void WriteFixtures()
    {
        // A no-op unless explicitly asked for: regenerating fixtures during an ordinary
        // test run would let the inputs drift out from under the round-trip assertion.
        if (Environment.GetEnvironmentVariable("PIXELFIT_WRITE_FIXTURES") != "1")
        {
            return;
        }

        Directory.CreateDirectory(TestImage.FixtureDirectory);

        foreach ((string name, byte[] art, int width, int height) in Originals())
        {
            TestImage.Save(art, width, height, Path.Combine(TestImage.FixtureDirectory, $"{name}.png"));

            foreach (int factor in PixelizeRoundTripTests.Factors)
            {
                byte[] scaled = TestImage.UpscaleBicubic(art, width, height, factor);
                byte[] noisy = TestImage.AddNoise(scaled, NoiseSeed + factor, NoiseAmplitude);
                TestImage.Save(
                    noisy,
                    width * factor,
                    height * factor,
                    Path.Combine(TestImage.FixtureDirectory, $"{name}@{factor}x.png"));
            }
        }
    }

    internal static IEnumerable<(string Name, byte[] Art, int Width, int Height)> Originals()
    {
        foreach ((string name, string art) in FixtureSprites.All())
        {
            byte[] rgb = FixtureSprites.Rasterize(art, out int width, out int height);
            yield return (name, rgb, width, height);
        }

        byte[] mosaic = FixtureSprites.Mosaic(FixtureSprites.MosaicSize, out int size, out _);
        yield return ("mosaic", mosaic, size, size);
    }
}
