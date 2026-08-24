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
/// </remarks>
internal sealed class SwatchGrid : Control
{
    private const int SwatchSize = 34;
    private const int Gap = 4;

    private Rgb24[] entries = [];
    private IReadOnlyList<int> highlighted = [];
    private int selected = -1;

    public SwatchGrid()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
    }

    public event EventHandler? SelectionChanged;

    public int SelectedIndex => selected;

    public void SetEntries(Rgb24[] palette)
    {
        entries = palette;
        selected = palette.Length > 0 ? 0 : -1;
        highlighted = [];
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Highlight(IReadOnlyList<int> indices)
    {
        highlighted = indices;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseDown(e);

        int columns = Math.Max(1, (Width - Gap) / (SwatchSize + Gap));
        int column = (e.X - Gap) / (SwatchSize + Gap);
        int row = (e.Y - Gap) / (SwatchSize + Gap);
        int index = (row * columns) + column;

        if (column >= 0 && column < columns && index >= 0 && index < entries.Length)
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

        int columns = Math.Max(1, (Width - Gap) / (SwatchSize + Gap));
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
        using Font font = new(Font.FontFamily, 7f);
        g.DrawString(index.ToString(), font, text, bounds.X + 2, bounds.Y + 1);
    }

    protected override Size DefaultSize => new(320, 240);
}
