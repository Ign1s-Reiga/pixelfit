using System.Globalization;
using System.Text;
using Pixelfit.Core;

namespace Pixelfit.Cli;

/// <summary>Renders a <see cref="PaletteReport"/> as the text form shown in the README.</summary>
internal static class PaletteReportWriter
{
    public static string Render(PaletteReport report)
    {
        StringBuilder builder = new();

        if (report.Ramps.Count == 0)
        {
            builder.AppendLine("no ramps found");
        }

        for (int i = 0; i < report.Ramps.Count; i++)
        {
            AppendRamp(builder, report, i);
        }

        AppendWarnings(builder, report);
        return builder.ToString();
    }

    private static void AppendRamp(StringBuilder builder, PaletteReport report, int index)
    {
        RampReport ramp = report.Ramps[index];
        string hue = ramp.IsNeutral
            ? "neutral"
            : $"hue ~{ramp.HueDegrees!.Value.ToString("F0", CultureInfo.InvariantCulture)}°";

        builder.AppendLine(
            CultureInfo.InvariantCulture,
            $"ramp {index + 1} ({hue}, {ramp.Indices.Count} entries)");

        builder.Append("  L    ");
        foreach (float l in ramp.Lightness)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{l,6:F2}");
        }

        builder.AppendLine(
            ramp.IsMonotonicLightness
                ? $"     monotonic, step sigma {ramp.LightnessStepDeviation.ToString("F3", CultureInfo.InvariantCulture)}"
                : $"     NOT monotonic — reverses at {string.Join(", ", ramp.OutOfOrderPositions)}");

        if (ramp.IsNeutral)
        {
            builder.AppendLine();
            return;
        }

        builder.Append("  hue  ");
        foreach (int i in ramp.Indices)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{report.Entries[i].Lch.H,6:F0}");
        }

        // A fact, not an instruction. Whether a ramp should shift hue is the author's call.
        builder.AppendLine(
            MathF.Abs(ramp.HueShiftDegrees) < 1f
                ? "     no hue shift"
                : $"     hue shifts {ramp.HueShiftDegrees.ToString("+0;-0", CultureInfo.InvariantCulture)}° from dark to light");

        builder.AppendLine();
    }

    private static void AppendWarnings(StringBuilder builder, PaletteReport report)
    {
        builder.AppendLine("warnings");

        if (report.Warnings.Count == 0)
        {
            builder.AppendLine("  none");
            return;
        }

        foreach (PaletteWarning warning in report.Warnings.OrderBy(w => w.Kind))
        {
            string indices = string.Join(", ", warning.Indices.Select(i => $"[{i}]"));
            builder.AppendLine(CultureInfo.InvariantCulture, $"  {Label(warning.Kind),-12} {indices} {warning.Message}");
        }
    }

    private static string Label(PaletteWarningKind kind) => kind switch
    {
        PaletteWarningKind.TooClose => "too close",
        PaletteWarningKind.GreyscaleCollision => "greyscale",
        PaletteWarningKind.NonMonotonicRamp => "ramp order",
        PaletteWarningKind.UnevenLightnessSteps => "uneven",
        PaletteWarningKind.Unused => "unused",
        _ => kind.ToString(),
    };
}
