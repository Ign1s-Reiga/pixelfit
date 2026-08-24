using Pixelfit.Core;

namespace Pixelfit.Tests;

/// <summary>Deterministic sprite and upscale helpers. No randomness without a fixed seed.</summary>
internal static class SyntheticSprite
{
    /// <summary>Sweetie 16, a widely used pixel-art palette. Structurally ordinary, which is what we want here.</summary>
    public static readonly Rgb24[] Sweetie16 =
    [
        new(26, 28, 44), new(93, 39, 93), new(177, 62, 83), new(239, 125, 87),
        new(255, 205, 117), new(167, 240, 112), new(56, 183, 100), new(37, 113, 121),
        new(41, 54, 111), new(59, 93, 201), new(65, 166, 246), new(115, 239, 247),
        new(244, 244, 244), new(148, 176, 194), new(86, 108, 134), new(51, 60, 87),
    ];

    /// <summary>
    /// A sprite of irregular solid regions with one-pixel outlines and scattered single-pixel
    /// detail — the shape real pixel art has. Region placement is deliberately non-periodic,
    /// so that any period a grid estimator finds came from upscaling and not from the sprite.
    /// </summary>
    public static byte[] Create(int width, int height, int seed, Rgb24[] palette)
    {
        Random random = new(seed);
        int[] region = new int[width * height];
        int[] regionColor = new int[(width * height / 4) + 2];
        regionColor[0] = palette.Length - 4;

        // Irregular rectangles at random positions, so no fixed block lattice exists.
        int rectangles = Math.Max(6, width * height / 24);
        for (int i = 1; i <= rectangles && i < regionColor.Length; i++)
        {
            int w = random.Next(2, Math.Max(3, width / 4));
            int h = random.Next(2, Math.Max(3, height / 4));
            int x0 = random.Next(0, Math.Max(1, width - w));
            int y0 = random.Next(0, Math.Max(1, height - h));
            regionColor[i] = random.Next(1, palette.Length);

            for (int y = y0; y < Math.Min(height, y0 + h); y++)
            {
                for (int x = x0; x < Math.Min(width, x0 + w); x++)
                {
                    region[(y * width) + x] = i;
                }
            }
        }

        byte[] rgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int here = region[(y * width) + x];
                bool onEdge =
                    x == 0 || y == 0 || x == width - 1 || y == height - 1
                    || region[(y * width) + x - 1] != here
                    || region[(y * width) + x + 1] != here
                    || region[((y - 1) * width) + x] != here
                    || region[((y + 1) * width) + x] != here;

                // Index 0 is the darkest entry, used as the outline. Single-pixel highlights
                // put real detail at the 1px scale, which is what pixel art actually looks like.
                int index = onEdge ? 0 : regionColor[here];
                if (!onEdge && random.Next(8) == 0)
                {
                    index = random.Next(1, palette.Length);
                }

                Rgb24 c = palette[index];
                int o = ((y * width) + x) * 3;
                rgb[o] = c.R;
                rgb[o + 1] = c.G;
                rgb[o + 2] = c.B;
            }
        }

        return rgb;
    }

    /// <summary>Nearest-neighbour upscale — exact cell blocks, no blending.</summary>
    public static byte[] Upscale(ReadOnlySpan<byte> rgb, int width, int height, int factor)
    {
        int outWidth = width * factor;
        byte[] result = new byte[outWidth * height * factor * 3];

        for (int y = 0; y < height * factor; y++)
        {
            int srcRow = (y / factor) * width;
            for (int x = 0; x < outWidth; x++)
            {
                int src = (srcRow + (x / factor)) * 3;
                int dst = ((y * outWidth) + x) * 3;
                result[dst] = rgb[src];
                result[dst + 1] = rgb[src + 1];
                result[dst + 2] = rgb[src + 2];
            }
        }

        return result;
    }

    /// <summary>Crops from the top-left, which shifts the grid phase by a known amount.</summary>
    public static byte[] Crop(ReadOnlySpan<byte> rgb, int width, int cropX, int cropY, int outWidth, int outHeight)
    {
        byte[] result = new byte[outWidth * outHeight * 3];
        for (int y = 0; y < outHeight; y++)
        {
            for (int x = 0; x < outWidth; x++)
            {
                int src = (((y + cropY) * width) + x + cropX) * 3;
                int dst = ((y * outWidth) + x) * 3;
                result[dst] = rgb[src];
                result[dst + 1] = rgb[src + 1];
                result[dst + 2] = rgb[src + 2];
            }
        }

        return result;
    }
}
