using Pixelfit.Core;
using SixLabors.ImageSharp;

using Rgb24Pixel = SixLabors.ImageSharp.PixelFormats.Rgb24;
using Rgba32Pixel = SixLabors.ImageSharp.PixelFormats.Rgba32;

namespace Pixelfit.Cli;

/// <summary>PNG in, PNG out. The only place in the project that knows what a file is.</summary>
internal static class ImageIo
{
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
        byte[] copy = rgb.ToArray();
        using Image<Rgb24Pixel> image = new(width, height);

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

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        image.SaveAsPng(path);
    }

    public static Rgb24[] UniqueColors(ReadOnlySpan<byte> rgb, int width, int height, out int distinctTotal) =>
        Pixelize.UniqueColors(rgb, width, height, Pixelize.DefaultUniqueColorLimit, out distinctTotal);

    /// <summary>
    /// The opaque pixels of a PNG, packed as one row.
    /// </summary>
    /// <remarks>
    /// <see cref="Load"/> decodes straight to RGB and drops alpha, which is right for the
    /// pixelize path — every pixel there is a pixel of artwork. It is wrong for anything that
    /// counts pixels: a fully transparent pixel still carries an RGB value, and on a cut-out
    /// sprite the invisible background is usually most of the canvas and would dominate.
    /// </remarks>
    public static byte[] LoadOpaque(string path, out int count)
    {
        using Image<Rgba32Pixel> image = Image.Load<Rgba32Pixel>(path);
        byte[] packed = new byte[image.Width * image.Height * 3];
        int written = 0;

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32Pixel> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].A == 0)
                    {
                        continue;
                    }

                    packed[written * 3] = row[x].R;
                    packed[(written * 3) + 1] = row[x].G;
                    packed[(written * 3) + 2] = row[x].B;
                    written++;
                }
            }
        });

        count = written;
        return packed;
    }
}
