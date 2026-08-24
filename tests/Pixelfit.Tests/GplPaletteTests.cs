using Pixelfit.Core;
using Xunit;

namespace Pixelfit.Tests;

public sealed class GplPaletteTests
{
    private const string Sweetie16 =
        """
        GIMP Palette
        Name: Sweetie 16
        Columns: 8
        #
         26  28  44	black
         93  39  93	purple
        177  62  83	red
        239 125  87	orange
        """;

    [Fact]
    public void ParsesNameColumnsAndEntries()
    {
        GplPalette palette = GplPalette.Parse(Sweetie16);

        Assert.Equal("Sweetie 16", palette.Name);
        Assert.Equal(8, palette.Columns);
        Assert.Equal(4, palette.Colors.Count);
        Assert.Equal(new Rgb24(26, 28, 44), palette.Colors[0]);
        Assert.Equal(new Rgb24(239, 125, 87), palette.Colors[3]);
        Assert.Equal("black", palette.ColorNames[0]);
        Assert.Equal("orange", palette.ColorNames[3]);
    }

    [Fact]
    public void RoundTripsThroughItsOwnWriter()
    {
        GplPalette original = GplPalette.Parse(Sweetie16);
        GplPalette reparsed = GplPalette.Parse(original.ToGpl());

        Assert.Equal(original.Name, reparsed.Name);
        Assert.Equal(original.Columns, reparsed.Columns);
        Assert.Equal(original.Colors, reparsed.Colors);
        Assert.Equal(original.ColorNames, reparsed.ColorNames);
    }

    [Fact]
    public void AcceptsCommentsBlankLinesAndMissingEntryNames()
    {
        const string text =
            """
            GIMP Palette
            Name: Terse
            #
            # a comment
              0   0   0

            255 255 255
            """;

        GplPalette palette = GplPalette.Parse(text);

        Assert.Equal(2, palette.Colors.Count);
        Assert.Equal(new Rgb24(0, 0, 0), palette.Colors[0]);
        Assert.Equal(new Rgb24(255, 255, 255), palette.Colors[1]);
        Assert.Equal(string.Empty, palette.ColorNames[0]);
    }

    [Fact]
    public void AcceptsTabsAndMultiWordEntryNames()
    {
        GplPalette palette = GplPalette.Parse("GIMP Palette\nName: T\n#\n10\t20\t30\tdusty rose\n");

        Assert.Equal(new Rgb24(10, 20, 30), palette.Colors[0]);
        Assert.Equal("dusty rose", palette.ColorNames[0]);
    }

    [Fact]
    public void RejectsAFileThatIsNotAGimpPalette()
    {
        Assert.Throws<FormatException>(() => GplPalette.Parse("# just a text file\n1 2 3\n"));
    }

    [Fact]
    public void SkipsMalformedAndOutOfRangeEntries()
    {
        const string text =
            """
            GIMP Palette
            Name: Mixed
            #
            10 20 30	fine
            300 20 30	out of range
            10 20	too few
            not a colour
            40 50 60	also fine
            """;

        GplPalette palette = GplPalette.Parse(text);

        Assert.Equal(2, palette.Colors.Count);
        Assert.Equal(new Rgb24(10, 20, 30), palette.Colors[0]);
        Assert.Equal(new Rgb24(40, 50, 60), palette.Colors[1]);
    }

    [Fact]
    public void WrittenOutputStartsWithTheRequiredHeader()
    {
        string text = new GplPalette("Test", [new Rgb24(1, 2, 3)], ["one"]).ToGpl();

        Assert.StartsWith("GIMP Palette\n", text);
        Assert.Contains("Name: Test\n", text);
        Assert.Contains("  1   2   3\tone\n", text);
    }

    [Fact]
    public void AnEntryWithNoNameIsWrittenWithItsHexValue()
    {
        string text = new GplPalette("Test", [new Rgb24(255, 0, 128)], []).ToGpl();
        Assert.Contains("#FF0080", text);
    }

    [Fact]
    public void SurvivesAByteOrderMarkOnTheFirstLine()
    {
        GplPalette palette = GplPalette.Parse("﻿GIMP Palette\nName: Bom\n#\n1 2 3\n");
        Assert.Equal("Bom", palette.Name);
    }
}
