namespace Pixelfit.Core;

/// <summary>A colour in sRGB, 8 bits per channel. The only pixel type that crosses a Core boundary.</summary>
public readonly record struct Rgb24(byte R, byte G, byte B)
{
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>A colour in OKLab. Cartesian form — use this for distance.</summary>
public readonly record struct OklabColor(float L, float A, float B);

/// <summary>A colour in OKLCh, the polar form of OKLab. <see cref="H"/> is in degrees, 0..360.</summary>
public readonly record struct OklchColor(float L, float C, float H);

/// <summary>
/// sRGB &lt;-&gt; linear sRGB &lt;-&gt; OKLab &lt;-&gt; OKLCh, plus nearest-palette search.
/// OKLab is used purely as a ruler: both sides of every comparison originate in sRGB and
/// output is always a colour copied verbatim, so out-of-gamut values cannot reach a caller.
/// </summary>
public static class Oklab
{
    private static readonly float[] SrgbToLinearLut = BuildSrgbToLinearLut();

    private static float[] BuildSrgbToLinearLut()
    {
        float[] lut = new float[256];
        for (int i = 0; i < 256; i++)
        {
            lut[i] = SrgbToLinear(i / 255f);
        }

        return lut;
    }

    /// <summary>The sRGB transfer function, including the linear segment. Not a 2.2 power curve.</summary>
    public static float SrgbToLinear(float c) =>
        c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    /// <summary>Inverse of <see cref="SrgbToLinear(float)"/>.</summary>
    public static float LinearToSrgb(float c) =>
        c <= 0.0031308f ? c * 12.92f : (1.055f * MathF.Pow(c, 1f / 2.4f)) - 0.055f;

    public static OklabColor FromSrgb(Rgb24 c) => FromSrgb(c.R, c.G, c.B);

    public static OklabColor FromSrgb(byte r, byte g, byte b) =>
        FromLinear(SrgbToLinearLut[r], SrgbToLinearLut[g], SrgbToLinearLut[b]);

    public static OklabColor FromLinear(float r, float g, float b)
    {
        float l = (0.4122214708f * r) + (0.5363325363f * g) + (0.0514459929f * b);
        float m = (0.2119034982f * r) + (0.6806995451f * g) + (0.1073969566f * b);
        float s = (0.0883024619f * r) + (0.2817188376f * g) + (0.6299787005f * b);

        // Cbrt, not Pow(x, 1/3) — the latter returns NaN for the negative values that
        // arise from out-of-gamut intermediates.
        float lc = MathF.Cbrt(l);
        float mc = MathF.Cbrt(m);
        float sc = MathF.Cbrt(s);

        return new OklabColor(
            (0.2104542553f * lc) + (0.7936177850f * mc) - (0.0040720468f * sc),
            (1.9779984951f * lc) - (2.4285922050f * mc) + (0.4505937099f * sc),
            (0.0259040371f * lc) + (0.7827717662f * mc) - (0.8086757660f * sc));
    }

    public static (float R, float G, float B) ToLinear(OklabColor c)
    {
        float lc = c.L + (0.3963377774f * c.A) + (0.2158037573f * c.B);
        float mc = c.L - (0.1055613458f * c.A) - (0.0638541728f * c.B);
        float sc = c.L - (0.0894841775f * c.A) - (1.2914855480f * c.B);

        float l = lc * lc * lc;
        float m = mc * mc * mc;
        float s = sc * sc * sc;

        return (
            (+4.0767416621f * l) - (3.3077115913f * m) + (0.2309699292f * s),
            (-1.2684380046f * l) + (2.6097574011f * m) - (0.3413193965f * s),
            (-0.0041960863f * l) - (0.7034186147f * m) + (1.7076147010f * s));
    }

    /// <summary>
    /// Only used by tests and by the round-trip assertion. No production path converts
    /// OKLab back to sRGB — output is always a palette entry copied verbatim.
    /// </summary>
    public static Rgb24 ToSrgb(OklabColor c)
    {
        (float r, float g, float b) = ToLinear(c);
        return new Rgb24(EncodeChannel(r), EncodeChannel(g), EncodeChannel(b));
    }

    private static byte EncodeChannel(float linear)
    {
        float encoded = LinearToSrgb(linear);
        int v = (int)MathF.Round(encoded * 255f);
        return (byte)Math.Clamp(v, 0, 255);
    }

    public static OklchColor ToOklch(OklabColor c)
    {
        float chroma = MathF.Sqrt((c.A * c.A) + (c.B * c.B));
        float hue = MathF.Atan2(c.B, c.A) * (180f / MathF.PI);
        if (hue < 0f)
        {
            hue += 360f;
        }

        return new OklchColor(c.L, chroma, hue);
    }

    public static OklabColor FromOklch(OklchColor c)
    {
        float radians = c.H * (MathF.PI / 180f);
        return new OklabColor(c.L, c.C * MathF.Cos(radians), c.C * MathF.Sin(radians));
    }

    public static OklchColor OklchFromSrgb(Rgb24 c) => ToOklch(FromSrgb(c));

    /// <summary>Perceptual distance. Cartesian OKLab — going polar would reintroduce hue wraparound.</summary>
    public static float Distance(OklabColor a, OklabColor b) => MathF.Sqrt(DistanceSquared(a, b));

    public static float DistanceSquared(OklabColor a, OklabColor b)
    {
        float dl = a.L - b.L;
        float da = a.A - b.A;
        float db = a.B - b.B;
        return (dl * dl) + (da * da) + (db * db);
    }

    /// <summary>Signed difference between two hue angles in degrees, in -180..180.</summary>
    public static float HueDelta(float fromDegrees, float toDegrees)
    {
        float d = (toDegrees - fromDegrees) % 360f;
        if (d > 180f)
        {
            d -= 360f;
        }
        else if (d < -180f)
        {
            d += 360f;
        }

        return d;
    }

    /// <summary>Index of the palette entry nearest <paramref name="c"/>, or -1 for an empty palette.</summary>
    public static int NearestInPalette(OklabColor c, ReadOnlySpan<OklabColor> palette)
    {
        int best = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < palette.Length; i++)
        {
            float d = DistanceSquared(c, palette[i]);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        return best;
    }

    /// <summary>Converts a palette to OKLab once, so per-pixel search does not re-convert.</summary>
    public static OklabColor[] ToOklab(ReadOnlySpan<Rgb24> palette)
    {
        OklabColor[] result = new OklabColor[palette.Length];
        for (int i = 0; i < palette.Length; i++)
        {
            result[i] = FromSrgb(palette[i]);
        }

        return result;
    }
}
