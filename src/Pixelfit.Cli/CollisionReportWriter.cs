using System.Globalization;
using System.Text;
using Pixelfit.Core;

namespace Pixelfit.Cli;

/// <summary>Renders a <see cref="CollisionReport"/> in the same shape as the palette report.</summary>
internal static class CollisionReportWriter
{
    public static string Render(CollisionReport report, IReadOnlyList<Rgb24> palette)
    {
        StringBuilder builder = new();

        if (report.NearestIndex < 0)
        {
            builder.AppendLine("  nothing to compare against");
            return builder.ToString();
        }

        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"  {"nearest",-12} [{report.NearestIndex}] {palette[report.NearestIndex]} at dE {report.NearestDistance:F3}");

        foreach (Collision collision in report.Collisions.OrderBy(c => c.Kind).ThenBy(c => c.Value))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"  {Describe(collision, palette)}");
        }

        foreach (RampPlacement placement in report.Placements)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"  {Describe(placement)}");
        }

        if (report.Collisions.Count == 0 && report.Placements.Count == 0)
        {
            builder.AppendLine("  no collisions, and no ramp shares its hue");
        }

        return builder.ToString();
    }

    private static string Describe(Collision collision, IReadOnlyList<Rgb24> palette) =>
        collision.Kind switch
        {
            PaletteWarningKind.TooClose =>
                $"{"too close",-12} [{collision.Index}] {palette[collision.Index]} at dE {collision.Value:F3}",
            PaletteWarningKind.GreyscaleCollision =>
                $"{"greyscale",-12} [{collision.Index}] {palette[collision.Index]} differs by L {collision.Value:F3}"
                + " and will merge when desaturated",
            _ => $"{collision.Kind,-12} [{collision.Index}] {palette[collision.Index]}",
        };

    /// <summary>
    /// Where it would sit, and whether that carries the progression on. Both are statements
    /// about the ramp's structure; neither says whether to use the colour.
    /// </summary>
    private static string Describe(RampPlacement placement)
    {
        string position =
            $"would sit {Ordinal(placement.PositionByLightness + 1)} of {placement.MemberCount + 1} by lightness";
        string progression = placement.ExtendsProgression
            ? "extends the progression"
            : "does not extend the progression";

        return $"{$"ramp {placement.RampIndex + 1}",-12} {position}; {progression}";
    }

    private static string Ordinal(int n) => n switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{n.ToString(CultureInfo.InvariantCulture)}th",
    };
}
