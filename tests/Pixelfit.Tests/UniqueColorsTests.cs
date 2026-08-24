using Pixelfit.Core;
using Xunit;

namespace Pixelfit.Tests;

/// <summary>
/// The colour limit is a cap on what is collected. Reaching it says nothing about how many
/// colours the image has, and every caller that prints a count depends on knowing the
/// difference.
/// </summary>
public sealed class UniqueColorsTests
{
    [Fact]
    public void AnImageUnderTheLimitReportsItsColoursAndTheirExactCount()
    {
        byte[] image = Strip([new(10, 20, 30), new(40, 50, 60), new(10, 20, 30), new(70, 80, 90)]);

        Rgb24[] colors = Pixelize.UniqueColors(image, 4, 1, limit: 16, out int distinctTotal);

        Assert.Equal(3, distinctTotal);
        Assert.Equal([new(10, 20, 30), new(40, 50, 60), new(70, 80, 90)], colors);
    }

    [Fact]
    public void AnImageOverTheLimitStillReportsHowManyColoursItReallyHas()
    {
        byte[] image = Strip([.. Enumerable.Range(0, 10).Select(i => new Rgb24((byte)i, 0, 0))]);

        Rgb24[] colors = Pixelize.UniqueColors(image, 10, 1, limit: 4, out int distinctTotal);

        // The returned length is what was collected; the total is what was there.
        Assert.Equal(4, colors.Length);
        Assert.Equal(10, distinctTotal);
    }

    /// <summary>
    /// Exactly at the limit is the case the old count could not tell from overflowing it,
    /// and the one that made "4096 distinct colours" a wrong answer for an 18,819-colour image.
    /// </summary>
    [Fact]
    public void AnImageOfExactlyTheLimitIsNotReportedAsTruncated()
    {
        byte[] image = Strip([.. Enumerable.Range(0, 8).Select(i => new Rgb24((byte)i, 0, 0))]);

        Rgb24[] colors = Pixelize.UniqueColors(image, 8, 1, limit: 8, out int distinctTotal);

        Assert.Equal(8, colors.Length);
        Assert.Equal(distinctTotal, colors.Length);
    }

    [Fact]
    public void CollectedColoursAreInFirstSeenOrder()
    {
        byte[] image = Strip([new(9, 9, 9), new(1, 1, 1), new(9, 9, 9), new(5, 5, 5)]);

        Rgb24[] colors = Pixelize.UniqueColors(image, 4, 1, limit: 16, out _);

        Assert.Equal([new(9, 9, 9), new(1, 1, 1), new(5, 5, 5)], colors);
    }

    [Fact]
    public void TheOverloadWithoutATotalStillWorks()
    {
        byte[] image = Strip([new(1, 2, 3), new(4, 5, 6)]);

        Assert.Equal(2, Pixelize.UniqueColors(image, 2, 1).Length);
    }

    /// <summary>One row of pixels, packed the way Core takes them.</summary>
    private static byte[] Strip(Rgb24[] colors)
    {
        byte[] rgb = new byte[colors.Length * 3];
        for (int i = 0; i < colors.Length; i++)
        {
            rgb[i * 3] = colors[i].R;
            rgb[(i * 3) + 1] = colors[i].G;
            rgb[(i * 3) + 2] = colors[i].B;
        }

        return rgb;
    }
}
