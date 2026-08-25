using System.Globalization;
using System.Text;
using Pixelfit.Core;

namespace Pixelfit.Cli;

/// <summary>
/// Batch front-end. It exists for bulk work and for exercising the algorithms without
/// restarting Paint.NET, which only scans for plugins at startup.
/// </summary>
internal static class Program
{
    private const string Usage =
        """
        pixelfit — pixel-art tooling

          pixelfit <input.png> [--palette p.gpl] [--grid N] [--phase X,Y] [--dither]
                   [--scale N] -o <out.png>
          pixelfit <input.png> --probe
          pixelfit check <palette.gpl | image.png>
          pixelfit collide <palette.gpl | image.png> <#RRGGBB>
          pixelfit palette <image.png> -n <count> -o <out.gpl>

          --palette PATH   GIMP palette (.gpl). Without one, cell colours are left as reduced.
          --grid N         Override the estimated logical pixel size.
          --phase X,Y      Override the estimated grid offset.
          --probe          Print the grid estimate and exit.
          --scale N        Also write out@Nx.png at nearest-neighbour N times.
          --dither         Ordered 4x4 Bayer. Usually looks wrong at sprite sizes.
        """;

    private static int Main(string[] args)
    {
        UseUtf8Output();

        try
        {
            return Run(args);
        }
        catch (Exception e) when (e is ArgumentException or IOException or FormatException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"pixelfit: {e.Message}");
            return 1;
        }
    }

    /// <summary>
    /// The report is not ASCII: it uses an em dash, a sigma for the step deviation and a
    /// degree sign for every hue. A console whose code page cannot represent those prints
    /// them as "?", which loses the units that make the numbers mean anything — on a Japanese
    /// Windows, "shield.png ? 7 distinct colours" and "step sigma" reading as "step ?".
    /// </summary>
    private static void UseUtf8Output()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // No console attached, or one that will not take the change. The report is still
            // correct, it just loses a few glyphs, and that is not worth failing a run over.
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        if (args[0] == "check")
        {
            return args.Length == 2
                ? Check(args[1])
                : throw new ArgumentException("check needs exactly one palette or image path.");
        }

        if (args[0] == "collide")
        {
            return args.Length == 3
                ? Collide(args[1], args[2])
                : throw new ArgumentException("collide needs a palette or image path and a colour.");
        }

        if (args[0] == "palette")
        {
            return ExtractPalette(args);
        }

        Options options = Options.Parse(args);
        string input = options.Input ?? throw new ArgumentException("No input image given.");
        byte[] rgb = ImageIo.Load(input, out int width, out int height);

        if (options.Probe)
        {
            return Probe(rgb, width, height);
        }

        string output = options.Output ?? throw new ArgumentException("No output given; use -o out.png.");
        return Convert(rgb, width, height, options, output);
    }

    private static int Probe(byte[] rgb, int width, int height)
    {
        GridEstimate estimate = Grid.Estimate(rgb, width, height);

        Console.WriteLine($"image      {width}x{height}");
        Console.WriteLine($"grid       {estimate.Size}");
        Console.WriteLine($"  from x   {estimate.SizeX}");
        Console.WriteLine($"  from y   {estimate.SizeY}");
        Console.WriteLine($"phase      {estimate.PhaseX},{estimate.PhaseY}");

        if (estimate.Size <= 1)
        {
            Console.WriteLine();
            Console.WriteLine("No grid found. Either this is already pixel art, or its detail is too");
            Console.WriteLine("coarse to leave a trace at cell boundaries. Use --grid to say what it is.");
            return 0;
        }

        int cellsX = CellCount(width, estimate.PhaseX, estimate.Size);
        int cellsY = CellCount(height, estimate.PhaseY, estimate.Size);
        Console.WriteLine($"cells      {cellsX}x{cellsY}");

        if (estimate.AxesDisagree)
        {
            Console.Error.WriteLine(
                $"warning: the axes disagree ({estimate.SizeX} across, {estimate.SizeY} down); using the smaller.");
        }

        return 0;
    }

    private static int CellCount(int extent, int phase, int size) =>
        Reduce.CellCount(extent, Reduce.CellOrigin(phase, size), size);

    private static int Convert(byte[] rgb, int width, int height, Options options, string output)
    {
        Rgb24[]? palette = options.PalettePath is null
            ? null
            : [.. GplPalette.Load(options.PalettePath).Colors];

        PixelizeResult result = Pixelize.Run(
            rgb,
            width,
            height,
            new PixelizeOptions
            {
                GridSize = options.GridSize,
                Phase = options.Phase,
                Palette = palette,
                Dither = options.Dither,
            });

        WarnAboutGrid(result, options);

        ImageIo.Save(Pixelize.ToRgbBytes(result.Cells), result.Cells.Width, result.Cells.Height, output);
        Console.WriteLine(
            $"{result.Cells.Width}x{result.Cells.Height} at grid {result.Grid.Size}, "
            + $"phase {result.Grid.PhaseX},{result.Grid.PhaseY} -> {output}");

        if (options.Scale is int scale && scale > 1)
        {
            WriteMagnified(result, scale, output);
        }

        return 0;
    }

    private static void WarnAboutGrid(PixelizeResult result, Options options)
    {
        if (result.Grid.AxesDisagree)
        {
            Console.Error.WriteLine(
                $"warning: the axes disagree ({result.Grid.SizeX} across, {result.Grid.SizeY} down); "
                + $"using {result.Grid.Size}.");
        }

        if (result.Grid.Size <= 1 && options.GridSize is null)
        {
            Console.Error.WriteLine(
                "warning: no grid was found, so every pixel became its own cell. Use --grid to say what it is.");
        }
    }

    private static void WriteMagnified(PixelizeResult result, int scale, string output)
    {
        byte[] magnified = Pixelize.Magnify(result.Cells, scale, out int width, out int height);
        string path = Path.Combine(
            Path.GetDirectoryName(output) ?? string.Empty,
            $"{Path.GetFileNameWithoutExtension(output)}@{scale.ToString(CultureInfo.InvariantCulture)}x"
            + Path.GetExtension(output));

        ImageIo.Save(magnified, width, height, path);
        Console.WriteLine($"{width}x{height} -> {path}");
    }

    /// <summary>
    /// Reduces an image to the colours it is actually made of, and writes them as a .gpl.
    /// </summary>
    /// <remarks>
    /// A separate step from converting on purpose. Baking an extracted palette straight into
    /// an image would leave no moment to look at it, and looking at it — and editing it — is
    /// the point of having it in a file.
    /// </remarks>
    private static int ExtractPalette(string[] args)
    {
        string input = args.Length > 1 && !args[1].StartsWith('-')
            ? args[1]
            : throw new ArgumentException("palette needs an image path.");

        int count = 16;
        string? output = null;

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-n":
                case "--count":
                    count = ParseCount(NextArg(args, ref i, args[i]));
                    break;

                case "-o":
                case "--output":
                    output = NextArg(args, ref i, args[i]);
                    break;

                default:
                    throw new ArgumentException($"Unknown option {args[i]}.");
            }
        }

        if (output is null)
        {
            throw new ArgumentException("No output given; use -o out.gpl.");
        }

        byte[] rgb = ImageIo.Load(input, out int width, out int height);
        Rgb24[] colors = Extract.Palette(rgb, width, height, count);

        GplPalette palette = new(
            Path.GetFileNameWithoutExtension(input),
            colors,
            [.. colors.Select(c => c.ToString())]) { Columns = 8 };
        palette.Save(output);

        Pixelize.UniqueColors(rgb, width, height, 1, out int distinctTotal);
        Console.WriteLine($"{distinctTotal} distinct colours -> {colors.Length} entries -> {output}");
        return 0;
    }

    private static string NextArg(string[] args, ref int i, string flag) =>
        i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{flag} needs a value.");

    private static int ParseCount(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
            ? value
            : throw new ArgumentException($"-n needs a whole number above zero, got \"{text}\".");

    /// <summary>
    /// Measures one colour against a palette. Everything it prints is a fact about colours
    /// that already exist; it does not propose one.
    /// </summary>
    private static int Collide(string path, string color)
    {
        Rgb24 candidate = ParseHex(color);
        Rgb24[] palette = LoadPalette(path, out string label);

        Console.WriteLine($"{candidate} against {label}");
        Console.WriteLine();
        Console.Write(CollisionReportWriter.Render(Core.Collide.Check(candidate, palette), palette));
        return 0;
    }

    /// <summary>A palette from a .gpl, or the colours an image is made of.</summary>
    private static Rgb24[] LoadPalette(string path, out string label)
    {
        if (Path.GetExtension(path).Equals(".gpl", StringComparison.OrdinalIgnoreCase))
        {
            GplPalette loaded = GplPalette.Load(path);
            label = $"{loaded.Name} ({loaded.Colors.Count} entries)";
            return [.. loaded.Colors];
        }

        byte[] rgb = ImageIo.Load(path, out int width, out int height);
        Rgb24[] colors = ImageIo.UniqueColors(rgb, width, height, out int distinctTotal);
        label = distinctTotal > colors.Length
            ? $"{path} ({distinctTotal} distinct colours; comparing against the first {colors.Length})"
            : $"{path} ({colors.Length} distinct colours)";
        return colors;
    }

    private static Rgb24 ParseHex(string text)
    {
        string hex = text.TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
        {
            throw new ArgumentException($"Colour must be six hex digits, got \"{text}\".");
        }

        return new Rgb24((byte)(value >> 16), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF));
    }

    private static int Check(string path)
    {
        Rgb24[] palette;
        Rgb24[] imageColors = [];

        if (Path.GetExtension(path).Equals(".gpl", StringComparison.OrdinalIgnoreCase))
        {
            GplPalette loaded = GplPalette.Load(path);
            palette = [.. loaded.Colors];
            Console.WriteLine($"{loaded.Name} — {palette.Length} entries");
        }
        else
        {
            byte[] rgb = ImageIo.Load(path, out int width, out int height);
            imageColors = ImageIo.UniqueColors(rgb, width, height, out int distinctTotal);
            palette = imageColors;

            // The cap is a cap, not a count. Printing the collected length as the number of
            // colours in the image is a measurement, and it would be the wrong one.
            Console.WriteLine(
                distinctTotal > palette.Length
                    ? $"{path} — {distinctTotal} distinct colours; analysing the first {palette.Length} in scan order"
                    : $"{path} — {distinctTotal} distinct colours");
        }

        Console.WriteLine();
        Console.Write(PaletteReportWriter.Render(Ramp.Analyze(palette, imageColors: imageColors)));
        return 0;
    }
}
