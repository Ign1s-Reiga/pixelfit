using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.Imaging;
using PaintDotNet.Rendering;
using Pixelfit.Core;

namespace Pixelfit.PaintNet;

/// <summary>
/// The layer the effect was invoked on, pulled out once as the plain arrays Core takes.
/// </summary>
/// <remarks>
/// Core never sees a Paint.NET type, which is what lets its tests run without a host and
/// keeps the plugin and the CLI from drifting apart. Alpha is carried alongside rather than
/// through Core: it plays no part in grid recovery or colour distance, and putting it in the
/// buffer Core reads would mean explaining to every algorithm why it should ignore a channel.
/// </remarks>
internal sealed class SourceImage
{
    private SourceImage(byte[] rgb, byte[] alpha, int width, int height)
    {
        Rgb = rgb;
        Alpha = alpha;
        Width = width;
        Height = height;
    }

    public byte[] Rgb { get; }

    public byte[] Alpha { get; }

    public int Width { get; }

    public int Height { get; }

    public static SourceImage Read(IEffectEnvironment environment)
    {
        IEffectInputBitmap<ColorBgra32> source = environment.GetSourceBitmapBgra32();
        SizeInt32 size = source.Size;

        using IBitmapLock<ColorBgra32> locked = source.Lock(new RectInt32(0, 0, size.Width, size.Height));
        RegionPtr<ColorBgra32> region = locked.AsRegionPtr();

        byte[] rgb = new byte[size.Width * size.Height * 3];
        byte[] alpha = new byte[size.Width * size.Height];

        for (int y = 0; y < size.Height; y++)
        {
            for (int x = 0; x < size.Width; x++)
            {
                ColorBgra32 c = region[x, y];
                int i = (y * size.Width) + x;
                rgb[i * 3] = c.R;
                rgb[(i * 3) + 1] = c.G;
                rgb[(i * 3) + 2] = c.B;
                alpha[i] = c.A;
            }
        }

        return new SourceImage(rgb, alpha, size.Width, size.Height);
    }

    public Rgb24 ColorAt(int x, int y)
    {
        int o = ((y * Width) + x) * 3;
        return new Rgb24(Rgb[o], Rgb[o + 1], Rgb[o + 2]);
    }

    /// <summary>Distinct colours of the layer, ignoring fully transparent pixels.</summary>
    public Rgb24[] UniqueColors(int limit = 512)
    {
        HashSet<int> seen = [];
        List<Rgb24> result = [];

        for (int i = 0; i < Width * Height && result.Count < limit; i++)
        {
            if (Alpha[i] == 0)
            {
                continue;
            }

            int key = (Rgb[i * 3] << 16) | (Rgb[(i * 3) + 1] << 8) | Rgb[(i * 3) + 2];
            if (seen.Add(key))
            {
                result.Add(new Rgb24(Rgb[i * 3], Rgb[(i * 3) + 1], Rgb[(i * 3) + 2]));
            }
        }

        return [.. result];
    }
}
