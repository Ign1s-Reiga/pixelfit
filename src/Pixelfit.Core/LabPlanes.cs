namespace Pixelfit.Core;

/// <summary>
/// An image converted once into three planar OKLab channels. Grid estimation reads it
/// several times, and converting per read would dominate the cost.
/// </summary>
internal readonly struct LabPlanes
{
    private LabPlanes(float[] l, float[] a, float[] b, int width, int height)
    {
        L = l;
        A = a;
        B = b;
        Width = width;
        Height = height;
    }

    public float[] L { get; }

    public float[] A { get; }

    public float[] B { get; }

    public int Width { get; }

    public int Height { get; }

    public static LabPlanes From(ReadOnlySpan<byte> rgb, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Image dimensions must be positive.");
        }

        int pixels = width * height;
        if (rgb.Length < pixels * 3)
        {
            throw new ArgumentException(
                $"Expected at least {pixels * 3} bytes for {width}x{height} RGB, got {rgb.Length}.",
                nameof(rgb));
        }

        float[] l = new float[pixels];
        float[] a = new float[pixels];
        float[] b = new float[pixels];

        for (int i = 0; i < pixels; i++)
        {
            int o = i * 3;
            OklabColor lab = Oklab.FromSrgb(rgb[o], rgb[o + 1], rgb[o + 2]);
            l[i] = lab.L;
            a[i] = lab.A;
            b[i] = lab.B;
        }

        return new LabPlanes(l, a, b, width, height);
    }

    /// <summary>Swaps the axes, so row-oriented code answers the vertical question unchanged.</summary>
    public LabPlanes Transposed()
    {
        float[] l = new float[L.Length];
        float[] a = new float[A.Length];
        float[] b = new float[B.Length];

        for (int y = 0; y < Height; y++)
        {
            int src = y * Width;
            for (int x = 0; x < Width; x++)
            {
                int dst = (x * Height) + y;
                l[dst] = L[src + x];
                a[dst] = A[src + x];
                b[dst] = B[src + x];
            }
        }

        return new LabPlanes(l, a, b, Height, Width);
    }
}

/// <summary>
/// Per-thread state for phase estimation: one row of prefix sums, plus running totals
/// of intra-cell variance for every candidate offset.
/// </summary>
internal sealed class PhaseAccumulator
{
    private readonly double[] totals;
    private readonly long[] counts;
    private readonly double[] sumL;
    private readonly double[] sumA;
    private readonly double[] sumB;
    private readonly double[] sumL2;
    private readonly double[] sumA2;
    private readonly double[] sumB2;

    public PhaseAccumulator(int size, int width)
    {
        totals = new double[size];
        counts = new long[size];
        sumL = new double[width + 1];
        sumA = new double[width + 1];
        sumB = new double[width + 1];
        sumL2 = new double[width + 1];
        sumA2 = new double[width + 1];
        sumB2 = new double[width + 1];
    }

    public void AddRow(in LabPlanes planes, int y, int size)
    {
        int width = planes.Width;
        BuildPrefix(planes, y, width);

        for (int offset = 0; offset < size; offset++)
        {
            for (int start = offset; start + size <= width; start += size)
            {
                totals[offset] += RunVariance(start, size);
                counts[offset]++;
            }
        }
    }

    public void MergeInto(double[] destinationTotals, long[] destinationCounts)
    {
        for (int i = 0; i < totals.Length; i++)
        {
            destinationTotals[i] += totals[i];
            destinationCounts[i] += counts[i];
        }
    }

    private void BuildPrefix(in LabPlanes planes, int y, int width)
    {
        int row = y * width;
        sumL[0] = sumA[0] = sumB[0] = 0;
        sumL2[0] = sumA2[0] = sumB2[0] = 0;

        for (int x = 0; x < width; x++)
        {
            double l = planes.L[row + x];
            double a = planes.A[row + x];
            double b = planes.B[row + x];

            sumL[x + 1] = sumL[x] + l;
            sumA[x + 1] = sumA[x] + a;
            sumB[x + 1] = sumB[x] + b;
            sumL2[x + 1] = sumL2[x] + (l * l);
            sumA2[x + 1] = sumA2[x] + (a * a);
            sumB2[x + 1] = sumB2[x] + (b * b);
        }
    }

    private double RunVariance(int start, int length) =>
        ChannelVariance(sumL, sumL2, start, length)
        + ChannelVariance(sumA, sumA2, start, length)
        + ChannelVariance(sumB, sumB2, start, length);

    private static double ChannelVariance(double[] sum, double[] sumOfSquares, int start, int length)
    {
        int end = start + length;
        double mean = (sum[end] - sum[start]) / length;
        double meanOfSquares = (sumOfSquares[end] - sumOfSquares[start]) / length;
        double variance = meanOfSquares - (mean * mean);
        return variance > 0 ? variance : 0;
    }
}
