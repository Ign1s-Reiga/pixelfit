using System.Globalization;
using System.Text;

namespace Pixelfit.Core;

/// <summary>A GIMP palette: a name and an ordered list of colours, each optionally named.</summary>
public sealed record GplPalette(string Name, IReadOnlyList<Rgb24> Colors, IReadOnlyList<string> ColorNames)
{
    public int Columns { get; init; }

    public static GplPalette Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using StringReader reader = new(text);
        return Parse(reader);
    }

    public static GplPalette Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        string? header = reader.ReadLine();
        if (header is null || !header.TrimStart('﻿').Trim().StartsWith("GIMP Palette", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Not a GIMP palette: the first line must be \"GIMP Palette\".");
        }

        string name = "Untitled";
        int columns = 0;
        List<Rgb24> colors = [];
        List<string> colorNames = [];

        while (reader.ReadLine() is string line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            if (TryReadHeader(trimmed, "Name:", out string? headerValue))
            {
                name = headerValue;
                continue;
            }

            if (TryReadHeader(trimmed, "Columns:", out string? columnsValue))
            {
                columns = int.TryParse(columnsValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int c) ? c : 0;
                continue;
            }

            if (TryReadEntry(line, out Rgb24 color, out string entryName))
            {
                colors.Add(color);
                colorNames.Add(entryName);
            }
        }

        return new GplPalette(name, colors, colorNames) { Columns = columns };
    }

    public static GplPalette Load(string path) => Parse(File.ReadAllText(path));

    public string ToGpl()
    {
        StringBuilder builder = new();
        builder.Append("GIMP Palette\n");
        builder.Append(CultureInfo.InvariantCulture, $"Name: {Name}\n");
        if (Columns > 0)
        {
            builder.Append(CultureInfo.InvariantCulture, $"Columns: {Columns}\n");
        }

        builder.Append("#\n");

        for (int i = 0; i < Colors.Count; i++)
        {
            Rgb24 c = Colors[i];
            string entryName = i < ColorNames.Count && ColorNames[i].Length > 0 ? ColorNames[i] : c.ToString();
            builder.Append(CultureInfo.InvariantCulture, $"{c.R,3} {c.G,3} {c.B,3}\t{entryName}\n");
        }

        return builder.ToString();
    }

    public void Save(string path) => File.WriteAllText(path, ToGpl());

    private static bool TryReadHeader(string line, string prefix, out string value)
    {
        if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = line[prefix.Length..].Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>
    /// Three integers, then an optional name. The separator is whatever whitespace the writer
    /// felt like using, which in practice is a mix of runs of spaces and a tab before the name.
    /// </summary>
    private static bool TryReadEntry(string line, out Rgb24 color, out string name)
    {
        color = default;
        name = string.Empty;

        string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3
            || !TryReadChannel(parts[0], out byte r)
            || !TryReadChannel(parts[1], out byte g)
            || !TryReadChannel(parts[2], out byte b))
        {
            return false;
        }

        color = new Rgb24(r, g, b);
        name = parts.Length > 3 ? string.Join(' ', parts[3..]) : string.Empty;
        return true;
    }

    private static bool TryReadChannel(string text, out byte value)
    {
        value = 0;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            || parsed is < 0 or > 255)
        {
            return false;
        }

        value = (byte)parsed;
        return true;
    }
}
