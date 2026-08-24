namespace Pixelfit.Core;

/// <summary>A grid of logical pixels recovered from a larger image.</summary>
public sealed class CellGrid
{
    public CellGrid(Rgb24[] cells, int width, int height)
    {
        Cells = cells;
        Width = width;
        Height = height;
    }

    public Rgb24[] Cells { get; }

    public int Width { get; }

    public int Height { get; }

    public Rgb24 this[int x, int y] => Cells[(y * Width) + x];
}

/// <summary>
/// Collapses each grid cell to one deliberate colour.
/// </summary>
public static class Reduce
{
    /// <summary>Colours are binned at 5 bits per channel before counting, so near-identical pixels agree.</summary>
    private const int QuantizeShift = 3;

    /// <summary>
    /// Reduces <paramref name="rgb"/> to one colour per cell of the given grid.
    /// </summary>
    /// <remarks>
    /// Never the mean. Averaging a cell that straddles an edge produces a colour present in
    /// neither region — a muddy halo on every outline, and the most recognisable failure of
    /// naive pixelization. The mode keeps edges clean.
    /// </remarks>
    public static CellGrid ToCells(
        ReadOnlySpan<byte> rgb,
        int width,
        int height,
        int size,
        int phaseX = 0,
        int phaseY = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        if (rgb.Length < width * height * 3)
        {
            throw new ArgumentException("Pixel buffer is smaller than the stated dimensions.", nameof(rgb));
        }

        int originX = CellOrigin(phaseX, size);
        int originY = CellOrigin(phaseY, size);
        int cellsX = CellCount(width, originX, size);
        int cellsY = CellCount(height, originY, size);

        Rgb24[] cells = new Rgb24[cellsX * cellsY];
        byte[] source = rgb.ToArray();

        Parallel.For(
            0,
            cellsY,
            () => new CellBuffer(size),
            (cellY, _, buffer) =>
            {
                for (int cellX = 0; cellX < cellsX; cellX++)
                {
                    buffer.Gather(
                        source,
                        width,
                        height,
                        originX + (cellX * size),
                        originY + (cellY * size),
                        size);

                    cells[(cellY * cellsX) + cellX] = buffer.Representative();
                }

                return buffer;
            },
            _ => { });

        return new CellGrid(cells, cellsX, cellsY);
    }

    /// <summary>
    /// The x (or y) of the leftmost cell boundary at or before zero. A non-zero phase means
    /// the first cell is a partial one.
    /// </summary>
    public static int CellOrigin(int phase, int size)
    {
        int p = ((phase % size) + size) % size;
        return p == 0 ? 0 : p - size;
    }

    public static int CellCount(int extent, int origin, int size) =>
        (int)Math.Ceiling((extent - origin) / (double)size);

    /// <summary>Per-thread scratch for one cell. Cells are small; this avoids allocating per cell.</summary>
    private sealed class CellBuffer
    {
        private readonly byte[] r;
        private readonly byte[] g;
        private readonly byte[] b;
        private readonly ushort[] keys;
        private readonly ushort[] sortedKeys;
        private readonly byte[] scratch;
        private int count;

        public CellBuffer(int size)
        {
            int capacity = size * size;
            r = new byte[capacity];
            g = new byte[capacity];
            b = new byte[capacity];
            keys = new ushort[capacity];
            sortedKeys = new ushort[capacity];
            scratch = new byte[capacity];
        }

        public void Gather(byte[] source, int width, int height, int x0, int y0, int size)
        {
            count = 0;

            // Ignore a margin around the cell. Interpolating an upscale spreads each boundary
            // over roughly one whole cell, so a cell's own colour survives only near its middle
            // and its edges are already halfway to the neighbour's. Reading the inset rather
            // than the whole cell is what lets a one-pixel feature win against the two
            // neighbours bleeding into it from both sides.
            int margin = size / 3;
            int xStart = Math.Max(0, x0 + margin);
            int yStart = Math.Max(0, y0 + margin);
            int xEnd = Math.Min(width, x0 + size - margin);
            int yEnd = Math.Min(height, y0 + size - margin);

            // A cell clipped at the image border can lose its whole inset; fall back to all of it.
            if (xStart >= xEnd || yStart >= yEnd)
            {
                xStart = Math.Max(0, x0);
                yStart = Math.Max(0, y0);
                xEnd = Math.Min(width, x0 + size);
                yEnd = Math.Min(height, y0 + size);
            }

            for (int y = yStart; y < yEnd; y++)
            {
                int row = y * width;
                for (int x = xStart; x < xEnd; x++)
                {
                    int o = (row + x) * 3;
                    r[count] = source[o];
                    g[count] = source[o + 1];
                    b[count] = source[o + 2];
                    keys[count] = Quantize(source[o], source[o + 1], source[o + 2]);
                    count++;
                }
            }
        }

        public Rgb24 Representative()
        {
            if (count == 0)
            {
                return default;
            }

            Array.Copy(keys, sortedKeys, count);
            Array.Sort(sortedKeys, 0, count);

            (ushort winner, int best, int runnerUp) = LongestRun(sortedKeys.AsSpan(0, count));

            // No clear plurality means the cell has no dominant colour; the median is a
            // safer answer than an arbitrary tie-break.
            return best > runnerUp ? AverageOfBin(winner) : Median();
        }

        private static ushort Quantize(byte red, byte green, byte blue) =>
            (ushort)(((red >> QuantizeShift) << 10) | ((green >> QuantizeShift) << 5) | (blue >> QuantizeShift));

        private static (ushort Winner, int Best, int RunnerUp) LongestRun(ReadOnlySpan<ushort> sorted)
        {
            ushort winner = sorted[0];
            int best = 0;
            int runnerUp = 0;

            int i = 0;
            while (i < sorted.Length)
            {
                int j = i;
                while (j < sorted.Length && sorted[j] == sorted[i])
                {
                    j++;
                }

                int run = j - i;
                if (run > best)
                {
                    runnerUp = best;
                    best = run;
                    winner = sorted[i];
                }
                else if (run > runnerUp)
                {
                    runnerUp = run;
                }

                i = j;
            }

            return (winner, best, runnerUp);
        }

        /// <summary>
        /// Averages only the pixels inside the winning bin. They already agree to within
        /// 1/32 per channel, so this cannot invent a colour that straddles an edge.
        /// </summary>
        private Rgb24 AverageOfBin(ushort winner)
        {
            int sumR = 0;
            int sumG = 0;
            int sumB = 0;
            int n = 0;

            for (int i = 0; i < count; i++)
            {
                if (keys[i] == winner)
                {
                    sumR += r[i];
                    sumG += g[i];
                    sumB += b[i];
                    n++;
                }
            }

            return new Rgb24(
                (byte)((sumR + (n / 2)) / n),
                (byte)((sumG + (n / 2)) / n),
                (byte)((sumB + (n / 2)) / n));
        }

        /// <summary>
        /// The per-channel median, snapped to whichever pixel actually present in the cell is
        /// nearest it. Snapping matters: a raw per-channel median is as capable of inventing a
        /// colour as the mean is.
        /// </summary>
        private Rgb24 Median()
        {
            Rgb24 target = new(ChannelMedian(r), ChannelMedian(g), ChannelMedian(b));
            OklabColor targetLab = Oklab.FromSrgb(target);

            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                float d = Oklab.DistanceSquared(targetLab, Oklab.FromSrgb(r[i], g[i], b[i]));
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }

            return new Rgb24(r[best], g[best], b[best]);
        }

        private byte ChannelMedian(byte[] channel)
        {
            Array.Copy(channel, scratch, count);
            Array.Sort(scratch, 0, count);
            return scratch[count / 2];
        }
    }
}
