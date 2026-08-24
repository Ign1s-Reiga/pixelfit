using Pixelfit.Core;

namespace Pixelfit.Tests;

/// <summary>
/// The authoring record for the committed fixture PNGs. The sprites are written as
/// character maps so the artwork itself lives in the repository and the PNGs under
/// Fixtures/ are reproducible artefacts rather than opaque binaries.
/// </summary>
internal static class FixtureSprites
{
    /// <summary>
    /// Two deliberate ramps — a warm one and a cool one — plus an outline and a highlight.
    /// Structurally sound, so ramp analysis has a clean baseline to be tested against.
    /// </summary>
    /// <remarks>
    /// Every entry is comfortably separated from every other. That is deliberate: a fixture
    /// palette with two entries a hair apart would make the round-trip test fail for a reason
    /// that has nothing to do with the pipeline — the reduced colour would land between two
    /// almost identical entries and snap to whichever happened to be nearer. Palettes with
    /// that defect are what PaletteLens exists to report, and RampTests uses one on purpose.
    /// </remarks>
    public static readonly Dictionary<char, Rgb24> Palette = new()
    {
        ['.'] = new Rgb24(52, 60, 82),    // backdrop, dark block
        [','] = new Rgb24(92, 102, 128),  // backdrop, light block
        ['x'] = new Rgb24(18, 14, 24),    // outline
        ['A'] = new Rgb24(90, 48, 56),    // warm ramp, darkest
        ['B'] = new Rgb24(150, 74, 68),
        ['C'] = new Rgb24(207, 111, 76),
        ['D'] = new Rgb24(246, 173, 106), // warm ramp, lightest
        ['e'] = new Rgb24(30, 74, 120),   // cool ramp, darkest
        ['f'] = new Rgb24(58, 122, 178),
        ['g'] = new Rgb24(108, 178, 220), // cool ramp, lightest
        ['w'] = new Rgb24(237, 237, 226), // highlight
    };

    /// <summary>A 16x16 character sprite: outline, three-step body ramp, eyes, highlight.</summary>
    public const string Slime =
        """
        ................
        .....xxxxxx.....
        ....xDDDDDDx....
        ...xDDCCCCDDx...
        ..xDCCCCCCCCDx..
        ..xCCCCCCCCCCx..
        .xCCwCCCCCCwCCx.
        .xCCCCCCCCCCCCx.
        .xCBBCCCCCCBBCx.
        .xBBxxBBBBxxBBx.
        .xBBBBBBBBBBBBx.
        .xABBBBBBBBBBAx.
        ..xAABBBBBBAAx..
        ..xAAAAAAAAAAx..
        ...xxxxxxxxxx...
        ................
        """;

    /// <summary>A 24x24 sprite with a long vertical run: flame, then a handle of constant width.</summary>
    public const string Torch =
        """
        ........................
        ..........xxxx..........
        .........xDDDDx.........
        ........xDDwwDDx........
        ........xDCwwCDx........
        ........xCCwwCCx........
        ........xCCCCCCx........
        .........xCCCCx.........
        .........xBBBBx.........
        ........xxBBBBxx........
        ........xeeffeex........
        ........xeeffeex........
        ........xeeffeex........
        ........xeeffeex........
        ........xeeffeex........
        ........xeeffeex........
        ........xeeffeex........
        ........xeeffeex........
        ........xeeffeex........
        ........xeeffeex........
        ........xeeggeex........
        ........xeeggeex........
        .........xxxxxx.........
        ........................
        """;

    /// <summary>A 20x20 sprite that is mostly flat colour, to catch phase errors on long runs.</summary>
    public const string Shield =
        """
        ....................
        ...xxxxxxxxxxxxxx...
        ..xffffffffffffffx..
        ..xffffffffffffffx..
        ..xffggggggggggffx..
        ..xffggwwggggwwffx..
        ..xffggwwggggwwffx..
        ..xffggggeeggggffx..
        ..xffgggeeeegggffx..
        ..xffggeeeeeeggffx..
        ..xffggeeeeeeggffx..
        ..xffgggeeeegggffx..
        ..xffggggeeggggffx..
        ..xffggggggggggffx..
        ..xffffggggggffffx..
        ...xffffggggffffx...
        ....xffffggffffx....
        .....xffffffffx.....
        ......xxffffxx......
        ........xxxx........
        """;

    /// <summary>Turns a character map into an RGB buffer.</summary>
    public static byte[] Rasterize(string art, out int width, out int height)
    {
        string[] rows = art.Replace("\r\n", "\n").Split('\n');
        height = rows.Length;
        width = rows.Max(row => row.Length);

        byte[] rgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                char c = x < rows[y].Length ? rows[y][x] : '.';
                if (c == '.')
                {
                    c = BackdropAt(x, y);
                }

                if (!Palette.TryGetValue(c, out Rgb24 color))
                {
                    color = Palette['.'];
                }

                int o = ((y * width) + x) * 3;
                rgb[o] = color.R;
                rgb[o + 1] = color.G;
                rgb[o + 2] = color.B;
            }
        }

        return rgb;
    }

    /// <summary>
    /// A chunky, irregular stone backdrop rather than a flat field.
    /// </summary>
    /// <remarks>
    /// Both properties matter and they pull against each other. A flat backdrop carries no
    /// evidence of where the logical pixel boundaries are, so grid estimation has nothing to
    /// find. A one-pixel checkerboard carries plenty, but does not survive interpolation:
    /// bicubic averages each pair together and the original cannot be recovered. Blocks of
    /// irregular width put an edge at many different offsets — which is what the estimator
    /// needs — while keeping every feature at least two pixels across, which is what survives.
    /// </remarks>
    private static char BackdropAt(int x, int y)
    {
        int column = IndexOf(x, ColumnWidths);
        int row = IndexOf(y, RowHeights);
        return BackdropShades[((column * 7) + (row * 13)) % BackdropShades.Length];
    }

    private static readonly int[] ColumnWidths = [3, 2, 4, 2, 5, 3];
    private static readonly int[] RowHeights = [2, 3, 2, 4, 3];
    private static readonly char[] BackdropShades = ['.', ','];

    /// <summary>Which block a coordinate falls in, given a repeating cycle of block sizes.</summary>
    private static int IndexOf(int coordinate, int[] sizes)
    {
        int cycle = sizes.Sum();
        int whole = coordinate / cycle;
        int remainder = coordinate % cycle;

        int index = 0;
        while (remainder >= sizes[index])
        {
            remainder -= sizes[index];
            index++;
        }

        return (whole * sizes.Length) + index;
    }

    /// <summary>
    /// A dense mosaic of irregular chunky blocks, at the size real work actually arrives in.
    /// </summary>
    /// <remarks>
    /// The small sprites above are the honest hard case and cannot be recovered exactly from a
    /// bicubic upscale: their outlines and highlights are one pixel across, and interpolation
    /// destroys that outright. This fixture is what the tool is really pointed at — a large
    /// image whose logical pixels number in the dozens per axis, with detail at every scale
    /// above one pixel. It has both of the properties the small sprites cannot have at once:
    /// enough edges for the grid to be recoverable, and no feature so fine that upscaling
    /// erases it.
    /// </remarks>
    public static byte[] Mosaic(int size, out int width, out int height)
    {
        width = size;
        height = size;
        char[] shades = [.. Palette.Keys];
        byte[] rgb = new byte[width * height * 3];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int column = IndexOf(x, MosaicColumnWidths);
                int row = IndexOf(y, MosaicRowHeights);

                // Mixing the two indices with coprime multipliers keeps neighbouring blocks
                // from repeating a colour often enough to flatten the local contrast.
                char c = shades[(((column * 5) + (row * 11)) % shades.Length + shades.Length) % shades.Length];
                Rgb24 color = Palette[c];

                int o = ((y * width) + x) * 3;
                rgb[o] = color.R;
                rgb[o + 1] = color.G;
                rgb[o + 2] = color.B;
            }
        }

        return rgb;
    }

    private static readonly int[] MosaicColumnWidths = [2, 3, 2, 4, 2, 3, 5, 2];
    private static readonly int[] MosaicRowHeights = [3, 2, 4, 2, 2, 5, 3, 2];

    public static Rgb24[] PaletteEntries() => [.. Palette.Values];

    public static IEnumerable<(string Name, string Art)> All()
    {
        yield return ("slime", Slime);
        yield return ("torch", Torch);
        yield return ("shield", Shield);
    }

    /// <summary>The size of the generated mosaic fixture, in logical pixels.</summary>
    public const int MosaicSize = 48;
}
