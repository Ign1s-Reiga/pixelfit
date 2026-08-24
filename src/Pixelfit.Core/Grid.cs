namespace Pixelfit.Core;

/// <summary>The recovered logical pixel grid: cell size and the offset of the first cell boundary.</summary>
/// <param name="Size">The logical pixel size actually used. 1 means no grid was detected.</param>
/// <param name="SizeX">Period recovered from the horizontal axis alone.</param>
/// <param name="SizeY">Period recovered from the vertical axis alone.</param>
/// <param name="PhaseX">Offset of the first vertical cell boundary, in 0..Size-1.</param>
/// <param name="PhaseY">Offset of the first horizontal cell boundary, in 0..Size-1.</param>
/// <param name="AxesDisagree">The axes differed by more than 10%; Size is the smaller of the two.</param>
public readonly record struct GridEstimate(
    int Size,
    int SizeX,
    int SizeY,
    int PhaseX,
    int PhaseY,
    bool AxesDisagree);

/// <summary>
/// Recovers the logical pixel size of an image that "is" pixel art at some larger scale —
/// a 1024px image of a 128px sprite has a grid size of 8.
/// </summary>
public static class Grid
{
    public const int MaxSize = 64;

    /// <summary>Below this, the autocorrelation peak is indistinguishable from noise and there is no grid.</summary>
    private const double MinPeak = 0.20;

    /// <summary>Estimates both the grid size and its phase.</summary>
    public static GridEstimate Estimate(ReadOnlySpan<byte> rgb, int width, int height, int maxSize = MaxSize)
    {
        LabPlanes planes = LabPlanes.From(rgb, width, height);
        LabPlanes transposed = planes.Transposed();

        int sizeX = EstimatePeriodAlongRows(planes, maxSize);
        int sizeY = EstimatePeriodAlongRows(transposed, maxSize);
        (int size, bool disagree) = Reconcile(sizeX, sizeY);

        if (size <= 1)
        {
            return new GridEstimate(1, sizeX, sizeY, 0, 0, disagree);
        }

        int phaseX = FindPhaseAlongRows(planes, size);
        int phaseY = FindPhaseAlongRows(transposed, size);
        return new GridEstimate(size, sizeX, sizeY, phaseX, phaseY, disagree);
    }

    /// <summary>The logical pixel size, or 1 if no periodic structure was found.</summary>
    public static int EstimateGridSize(ReadOnlySpan<byte> rgb, int width, int height, int maxSize = MaxSize) =>
        Estimate(rgb, width, height, maxSize).Size;

    /// <summary>
    /// The offset of the first cell boundary on each axis, in 0..<paramref name="size"/>-1.
    /// A wrong phase is more visually destructive than a slightly wrong size.
    /// </summary>
    public static (int X, int Y) EstimatePhase(ReadOnlySpan<byte> rgb, int width, int height, int size)
    {
        if (size <= 1)
        {
            return (0, 0);
        }

        LabPlanes planes = LabPlanes.From(rgb, width, height);
        return (FindPhaseAlongRows(planes, size), FindPhaseAlongRows(planes.Transposed(), size));
    }

    private static (int Size, bool Disagree) Reconcile(int sizeX, int sizeY)
    {
        // A single undetected axis is a failure to measure, not a measurement of 1.
        if (sizeX <= 1)
        {
            return (sizeY, sizeY > 1);
        }

        if (sizeY <= 1)
        {
            return (sizeX, true);
        }

        int smaller = Math.Min(sizeX, sizeY);
        int larger = Math.Max(sizeX, sizeY);
        return (smaller, larger - smaller > 0.10 * smaller);
    }

    /// <summary>
    /// Projects horizontal gradient magnitude onto the x axis, then autocorrelates it.
    /// Transposed planes give the vertical answer from the same code.
    /// </summary>
    private static int EstimatePeriodAlongRows(in LabPlanes planes, int maxSize)
    {
        int width = planes.Width;
        int height = planes.Height;
        if (width < 4)
        {
            return 1;
        }

        // Forward difference on the lightness plane is enough to expose cell boundaries.
        float[] signal = new float[width - 1];
        float[] l = planes.L;
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width - 1; x++)
            {
                signal[x] += MathF.Abs(l[row + x + 1] - l[row + x]);
            }
        }

        return FindPeriod(signal, maxSize);
    }

    /// <summary>
    /// Tests each candidate period in turn and returns the first that autocorrelates like a
    /// real one. Taking the earliest rather than the tallest peak is what picks the
    /// fundamental out of its own harmonics: 2s and 3s correlate just as hard as s.
    /// </summary>
    private static int FindPeriod(ReadOnlySpan<float> signal, int maxSize)
    {
        int n = signal.Length;

        // A period needs its second harmonic in range to be confirmed, and that harmonic
        // needs enough overlap to mean anything. Both together cap candidates at n/6.
        int maxCandidate = Math.Min(maxSize, n / 6);
        if (maxCandidate < 2)
        {
            return 1;
        }

        for (int p = 2; p <= maxCandidate; p++)
        {
            if (IsPeriod(signal, p))
            {
                return p;
            }
        }

        return 1;
    }

    /// <summary>
    /// Detrending has to happen per candidate. Subtracting the signal mean removes DC but not
    /// the envelope — a sprite in an empty backdrop projects far more gradient in the middle
    /// than at its edges, and that swell buries the comb. A window fixed in advance cannot
    /// work either: wide enough to preserve a long period is too wide to flatten the envelope.
    /// Tying the window to the period under test removes everything slower than that period
    /// while leaving the period itself intact.
    /// </summary>
    private static bool IsPeriod(ReadOnlySpan<float> signal, int p)
    {
        double[] centred = Detrend(signal, (2 * p) + 1);

        double energy = 0;
        for (int i = 0; i < centred.Length; i++)
        {
            energy += centred[i] * centred[i];
        }

        if (energy <= 0)
        {
            return false;
        }

        double scale = energy / centred.Length;
        double atPeriod = Correlate(centred, p) / scale;
        double atHarmonic = Correlate(centred, 2 * p) / scale;

        // A real period repeats: both the peak and its second harmonic have to be there.
        // Requiring the harmonic is what rejects a lone spurious peak thrown up by the
        // artwork itself, and scoring the pair together lets a modest but genuine peak
        // through where a bare threshold on the fundamental alone would miss it.
        if (atPeriod <= 0 || atHarmonic <= 0 || (atPeriod + atHarmonic) / 2 < MinPeak)
        {
            return false;
        }

        // A trough on either side, or the answer is a slope rather than a peak.
        return atPeriod >= Correlate(centred, p - 1) / scale
            && atPeriod >= Correlate(centred, p + 1) / scale;
    }

    /// <summary>
    /// High-pass by subtracting a centred moving average. The window is wide relative to any
    /// period being searched, so periodic structure survives and only the envelope is removed.
    /// </summary>
    private static double[] Detrend(ReadOnlySpan<float> signal, int window)
    {
        int n = signal.Length;
        double[] prefix = new double[n + 1];
        for (int i = 0; i < n; i++)
        {
            prefix[i + 1] = prefix[i] + signal[i];
        }

        int half = window / 2;
        double[] result = new double[n];
        for (int i = 0; i < n; i++)
        {
            int lo = Math.Max(0, i - half);
            int hi = Math.Min(n, i + half + 1);
            result[i] = signal[i] - ((prefix[hi] - prefix[lo]) / (hi - lo));
        }

        return result;
    }

    /// <summary>
    /// Unnormalised correlation at one lag. Naive and O(n) per lag — the signals are at most
    /// a few thousand samples, which does not justify an FFT dependency.
    /// </summary>
    private static double Correlate(ReadOnlySpan<double> centred, int lag)
    {
        int overlap = centred.Length - lag;
        if (overlap <= 0)
        {
            return 0;
        }

        double sum = 0;
        for (int i = 0; i < overlap; i++)
        {
            sum += centred[i] * centred[i + lag];
        }

        // Divide by the overlap so long lags are not penalised by the shrinking window.
        return sum / overlap;
    }

    /// <summary>
    /// Picks the offset whose cell runs have the least internal colour variance. Row prefix
    /// sums make each candidate run O(1), so all offsets together cost one pass per row.
    /// </summary>
    private static int FindPhaseAlongRows(in LabPlanes planes, int size)
    {
        int width = planes.Width;
        int height = planes.Height;
        if (size <= 1 || width < size * 2)
        {
            return 0;
        }

        double[] totals = new double[size];
        long[] counts = new long[size];
        LabPlanes local = planes;
        object gate = new();

        Parallel.For(
            0,
            height,
            () => new PhaseAccumulator(size, width),
            (y, _, accumulator) =>
            {
                accumulator.AddRow(local, y, size);
                return accumulator;
            },
            accumulator =>
            {
                lock (gate)
                {
                    accumulator.MergeInto(totals, counts);
                }
            });

        int bestOffset = 0;
        double bestMeanVariance = double.MaxValue;
        for (int offset = 0; offset < size; offset++)
        {
            if (counts[offset] == 0)
            {
                continue;
            }

            double meanVariance = totals[offset] / counts[offset];
            if (meanVariance < bestMeanVariance)
            {
                bestMeanVariance = meanVariance;
                bestOffset = offset;
            }
        }

        return bestOffset;
    }
}
