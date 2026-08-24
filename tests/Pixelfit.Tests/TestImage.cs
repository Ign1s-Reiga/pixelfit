using Pixelfit.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

// ImageSharp has its own Rgb24. Aliasing keeps Core's the unqualified one everywhere else.
using Rgb24Pixel = SixLabors.ImageSharp.PixelFormats.Rgb24;

namespace Pixelfit.Tests;

/// <summary>
/// PNG load/save and the degradations that turn pixel art into the kind of image this
/// tool is pointed at. ImageSharp is confined to the test project and the CLI.
/// </summary>
internal static class TestImage
{
    /// <summary>Fixtures as committed in the repository, not the copy under bin/.</summary>
    public static string FixtureDirectory { get; } =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures"));

    public static byte[] Load(string path, out int width, out int height)
    {
        using Image<Rgb24Pixel> image = Image.Load<Rgb24Pixel>(path);
        int w = image.Width;
        int h = image.Height;
        width = w;
        height = h;

        byte[] rgb = new byte[w * h * 3];
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgb24Pixel> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    int o = ((y * w) + x) * 3;
                    rgb[o] = row[x].R;
                    rgb[o + 1] = row[x].G;
                    rgb[o + 2] = row[x].B;
                }
            }
        });

        return rgb;
    }

    public static void Save(ReadOnlySpan<byte> rgb, int width, int height, string path)
    {
        using Image<Rgb24Pixel> image = new(width, height);
        byte[] copy = rgb.ToArray();

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgb24Pixel> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    int o = ((y * width) + x) * 3;
                    row[x] = new Rgb24Pixel(copy[o], copy[o + 1], copy[o + 2]);
                }
            }
        });

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        image.SaveAsPng(path);
    }

    /// <summary>
    /// Bicubic upscale — the interpolation that leaves no consistent grid to downscale onto,
    /// which is exactly the problem Pixelize exists to undo.
    /// </summary>
    public static byte[] UpscaleBicubic(ReadOnlySpan<byte> rgb, int width, int height, int factor)
    {
        using Image<Rgb24Pixel> image = new(width, height);
        byte[] copy = rgb.ToArray();

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgb24Pixel> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    int o = ((y * width) + x) * 3;
                    row[x] = new Rgb24Pixel(copy[o], copy[o + 1], copy[o + 2]);
                }
            }
        });

        image.Mutate(context => context.Resize(new ResizeOptions
        {
            Size = new Size(width * factor, height * factor),
            Sampler = KnownResamplers.Bicubic,
            Mode = ResizeMode.Stretch,
        }));

        int outWidth = width * factor;
        int outHeight = height * factor;
        byte[] result = new byte[outWidth * outHeight * 3];
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgb24Pixel> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    int o = ((y * outWidth) + x) * 3;
                    result[o] = row[x].R;
                    result[o + 1] = row[x].G;
                    result[o + 2] = row[x].B;
                }
            }
        });

        return result;
    }

    /// <summary>Mild deterministic noise. Fixed seed, so the committed inputs never drift.</summary>
    public static byte[] AddNoise(ReadOnlySpan<byte> rgb, int seed, int amplitude)
    {
        byte[] result = rgb.ToArray();
        Random random = new(seed);

        for (int i = 0; i < result.Length; i++)
        {
            int v = result[i] + random.Next(-amplitude, amplitude + 1);
            result[i] = (byte)Math.Clamp(v, 0, 255);
        }

        return result;
    }

    /// <summary>Counts pixels that differ, for a failure message worth reading.</summary>
    public static (int Differing, string FirstDifference) Compare(
        ReadOnlySpan<byte> actual,
        ReadOnlySpan<byte> expected,
        int width)
    {
        int differing = 0;
        string first = "none";

        for (int i = 0; i * 3 + 2 < expected.Length && i * 3 + 2 < actual.Length; i++)
        {
            int o = i * 3;
            if (actual[o] != expected[o] || actual[o + 1] != expected[o + 1] || actual[o + 2] != expected[o + 2])
            {
                if (differing == 0)
                {
                    Rgb24 a = new(actual[o], actual[o + 1], actual[o + 2]);
                    Rgb24 e = new(expected[o], expected[o + 1], expected[o + 2]);
                    first = $"({i % width},{i / width}) got {a}, want {e}";
                }

                differing++;
            }
        }

        return (differing, first);
    }
}
