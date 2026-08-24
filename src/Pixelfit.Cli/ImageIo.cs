using Pixelfit.Core;
using SixLabors.ImageSharp;

using Rgb24Pixel = SixLabors.ImageSharp.PixelFormats.Rgb24;

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
}
