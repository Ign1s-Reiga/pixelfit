using System.Drawing;
using System.Windows.Forms;
using Pixelfit.Core;

namespace Pixelfit.PaintNet;

/// <summary>
/// The palette, drawn as swatches with their indices.
/// </summary>
/// <remarks>
/// This is the control IndirectUI cannot give us, and the reason the dialog is hand-built:
/// a structural report about colour that you cannot see the colours of is most of the way to
/// useless. Entries belonging to the selected ramp are outlined so a ramp reads as a group.
/// <para>
/// Scrollable, because the palette is not always small. Reading the image's colours can hand
/// this control hundreds of entries, and a fixed grid draws everything past the first few rows
/// outside itself, where it can be neither seen nor clicked.
/// </para>
/// </remarks>
internal sealed class SwatchGrid : ScrollableControl
{
    private const int SwatchSize = 34;
    private const int Gap = 4;

    private readonly Font indexFont;

    private Rgb24[] entries = [];
    private IReadOnlyList<int> highlighted = [];
    private int selected = -1;

    public SwatchGrid()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoScroll = true;
        TabStop = false;

        // One font for every index label. Built per swatch it would be several hundred GDI
        // objects per repaint, and repaints now happen on every scroll tick.
        indexFont = new Font(Font.FontFamily, 7f);
    }

    public event EventHandler? SelectionChanged;

    public int SelectedIndex => selected;

    /// <summary>
    /// Columns always reserve room for the scrollbar, whether or not one is showing. Measuring
    /// against the live client width instead lets the layout oscillate: one fewer column makes
    /// the content taller, which summons the scrollbar, which takes away the column.
    /// </summary>
    private int Columns =>
        Math.Max(1, (Width - SystemInformation.VerticalScrollBarWidth - Gap) / (SwatchSize + Gap));

    public void SetEntries(Rgb24[] palette)
    {
        entries = palette;
        selected = palette.Length > 0 ? 0 : -1;
        highlighted = [];

        AutoScrollPosition = Point.Empty;
        UpdateScrollBounds();
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Highlight(IReadOnlyList<int> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);

        highlighted = indices;
        if (indices.Count > 0)
        {
            // Selecting a ramp is how the report points at its members. Pointing at something
            // outside the viewport is not pointing at it.
            ScrollIntoView(indices[0]);
        }

        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollBounds();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseDown(e);

        // The mouse arrives in viewport coordinates and the grid is laid out in content
        // coordinates. AutoScrollPosition reads back negative once scrolled, hence subtraction.
        int x = e.X - AutoScrollPosition.X;
        int y = e.Y - AutoScrollPosition.Y;
        if (x < Gap || y < Gap)
        {
            return;
        }

        int columns = Columns;
        int column = (x - Gap) / (SwatchSize + Gap);
        int index = (((y - Gap) / (SwatchSize + Gap)) * columns) + column;

        if (column < columns && index >= 0 && index < entries.Length)
        {
            selected = index;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnPaint(e);

        Graphics g = e.Graphics;
        g.Clear(BackColor);
        g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);

        int columns = Columns;
        HashSet<int> inRamp = [.. highlighted];

        for (int i = 0; i < entries.Length; i++)
        {
            int x = Gap + ((i % columns) * (SwatchSize + Gap));
            int y = Gap + ((i / columns) * (SwatchSize + Gap));
            Rectangle bounds = new(x, y, SwatchSize, SwatchSize);

            using (SolidBrush brush = new(Color.FromArgb(entries[i].R, entries[i].G, entries[i].B)))
            {
                g.FillRectangle(brush, bounds);
            }

            DrawBorder(g, bounds, i, inRamp);
            DrawIndex(g, bounds, i);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            indexFont.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Tells <see cref="ScrollableControl"/> how tall the palette actually is, which is what
    /// gives it a scrollbar to offer.
    /// </summary>
    private void UpdateScrollBounds()
    {
        int rows = entries.Length == 0 ? 0 : (int)Math.Ceiling(entries.Length / (double)Columns);
        AutoScrollMinSize = new Size(0, Gap + (rows * (SwatchSize + Gap)));
    }

    /// <summary>Brings one entry into the viewport, scrolling the shortest distance that does it.</summary>
    private void ScrollIntoView(int index)
    {
        if (index < 0 || index >= entries.Length)
        {
            return;
        }

        int top = Gap + ((index / Columns) * (SwatchSize + Gap));
        int viewTop = -AutoScrollPosition.Y;

        // The setter takes positive coordinates where the getter returns negative ones.
        if (top < viewTop)
        {
            AutoScrollPosition = new Point(0, top);
        }
        else if (top + SwatchSize > viewTop + ClientSize.Height)
        {
            AutoScrollPosition = new Point(0, top + SwatchSize - ClientSize.Height);
        }
    }

    private void DrawBorder(Graphics g, Rectangle bounds, int index, HashSet<int> inRamp)
    {
        if (index == selected)
        {
            using Pen pen = new(Color.White, 3f);
            g.DrawRectangle(pen, bounds);
            using Pen inner = new(Color.Black, 1f);
            g.DrawRectangle(inner, Rectangle.Inflate(bounds, -2, -2));
        }
        else if (inRamp.Contains(index))
        {
            using Pen pen = new(Color.White, 2f);
            g.DrawRectangle(pen, bounds);
        }
        else
        {
            using Pen pen = new(Color.FromArgb(90, 90, 90), 1f);
            g.DrawRectangle(pen, bounds);
        }
    }

    /// <summary>The index is written in whichever of black or white the swatch will not swallow.</summary>
    private void DrawIndex(Graphics g, Rectangle bounds, int index)
    {
        float lightness = Oklab.FromSrgb(entries[index]).L;
        using SolidBrush text = new(lightness > 0.6f ? Color.Black : Color.White);
        g.DrawString(index.ToString(), indexFont, text, bounds.X + 2, bounds.Y + 1);
    }

    protected override Size DefaultSize => new(320, 240);
}
