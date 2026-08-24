using System.Globalization;

namespace Pixelfit.Cli;

/// <summary>Everything the command line can say. Hand-parsed; the surface is six flags wide.</summary>
internal sealed record Options
{
    public string? Input { get; init; }

    public string? Output { get; init; }

    public string? PalettePath { get; init; }

    public int? GridSize { get; init; }

    public (int X, int Y)? Phase { get; init; }

    public bool Probe { get; init; }

    public bool Dither { get; init; }

    public int? Scale { get; init; }

    public static Options Parse(string[] args)
    {
        Options options = new();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--palette":
                    options = options with { PalettePath = Next(args, ref i, arg) };
                    break;

                case "--grid":
                    options = options with { GridSize = ParseInt(Next(args, ref i, arg), arg) };
                    break;

                case "--phase":
                    options = options with { Phase = ParsePhase(Next(args, ref i, arg)) };
                    break;

                case "--scale":
                    options = options with { Scale = ParseInt(Next(args, ref i, arg), arg) };
                    break;

                case "-o":
                case "--output":
                    options = options with { Output = Next(args, ref i, arg) };
                    break;

                case "--probe":
                    options = options with { Probe = true };
                    break;

                case "--dither":
                    options = options with { Dither = true };
                    break;

                default:
                    if (arg.StartsWith('-'))
                    {
                        throw new ArgumentException($"Unknown option {arg}.");
                    }

                    if (options.Input is not null)
                    {
                        throw new ArgumentException($"Unexpected extra argument {arg}.");
                    }

                    options = options with { Input = arg };
                    break;
            }
        }

        return options;
    }

    private static string Next(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"{flag} needs a value.");
        }

        return args[++i];
    }

    private static int ParseInt(string text, string flag) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new ArgumentException($"{flag} needs a whole number, got \"{text}\".");

    private static (int X, int Y) ParsePhase(string text)
    {
        string[] parts = text.Split(',');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
        {
            throw new ArgumentException($"--phase needs X,Y, got \"{text}\".");
        }

        return (x, y);
    }
}
