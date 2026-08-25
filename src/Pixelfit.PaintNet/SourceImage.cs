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

    /// <summary>
    /// The opaque pixels packed as one row, for the Core routines that take a whole image.
    /// </summary>
    /// <remarks>
    /// A fully transparent pixel still carries an RGB value, and on a cut-out sprite it is
    /// usually black or white — a colour the artwork does not contain and that would otherwise
    /// dominate anything counting by pixel. Alpha stays out of Core, so the filtering happens
    /// on this side and Core is handed pixels that all count.
    /// </remarks>
    public byte[] OpaquePixels(out int count)
    {
        // Counted first so the buffer is exactly the size it needs to be. Allocating one the
        // size of the whole canvas would ask for three bytes per pixel a second time, on a
        // layer that may be mostly transparent and may already be large.
        int opaque = 0;
        for (int i = 0; i < Width * Height; i++)
        {
            if (Alpha[i] != 0)
            {
                opaque++;
            }
        }

        byte[] packed = new byte[opaque * 3];
        count = 0;

        for (int i = 0; i < Width * Height && count < opaque; i++)
        {
            if (Alpha[i] == 0)
            {
                continue;
            }

            packed[count * 3] = Rgb[i * 3];
            packed[(count * 3) + 1] = Rgb[(i * 3) + 1];
            packed[(count * 3) + 2] = Rgb[(i * 3) + 2];
            count++;
        }

        return packed;
    }

    /// <summary>
    /// Distinct colours of the layer, ignoring fully transparent pixels, and how many of them
    /// there really are.
    /// </summary>
    /// <remarks>
    /// The limit caps what is collected. Reaching it is not the same answer as the layer having
    /// that many colours, and the difference decides whether an "unused" verdict may be given
    /// at all: measured against a truncated list, every entry the artwork uses further down
    /// looks absent from it.
    /// <para>
    /// The total is counted in a two-megabyte bitmap over the 24-bit colour space, one bit per
    /// representable colour. That is the same size whatever the layer holds, which a set of
    /// every distinct colour in a photographic layer would very much not be.
    /// </para>
    /// </remarks>
    public Rgb24[] UniqueColors(out int distinctTotal, int limit = 512)
    {
        byte[] seen = new byte[1 << 21];
        List<Rgb24> result = [];
        int pixels = Width * Height;
        distinctTotal = 0;

        for (int i = 0; i < pixels; i++)
        {
            if (Alpha[i] == 0)
            {
                continue;
            }

            int o = i * 3;
            int key = (Rgb[o] << 16) | (Rgb[o + 1] << 8) | Rgb[o + 2];
            byte mask = (byte)(1 << (key & 7));
            if ((seen[key >> 3] & mask) != 0)
            {
                continue;
            }

            seen[key >> 3] |= mask;
            distinctTotal++;

            if (result.Count < limit)
            {
                result.Add(new Rgb24(Rgb[o], Rgb[o + 1], Rgb[o + 2]));
            }
        }

        return [.. result];
    }
}
