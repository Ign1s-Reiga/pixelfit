using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.Imaging;
using PaintDotNet.IndirectUI;
using PaintDotNet.PropertySystem;
using PaintDotNet.Rendering;
using Pixelfit.Core;

namespace Pixelfit.PaintNet;

/// <summary>
/// Recovers the logical pixel grid of an image that only looks like pixel art, and collapses
/// each cell to one deliberate colour.
/// </summary>
/// <remarks>
/// The result is written back at the original resolution, because an effect cannot resize the
/// canvas. Every cell becomes a block of flat colour, so a following Image &gt; Resize by
/// nearest neighbour at 1/grid produces the actual sprite.
/// </remarks>
public sealed class PixelizeEffect : PropertyBasedBitmapEffect
{
    private readonly Lock gate = new();

    private SourceImage? source;
    private GridEstimate? estimate;
    private CellGrid? cells;
    private byte[]? cellAlpha;

    private int gridSizeSetting;
    private int phaseXSetting;
    private int phaseYSetting;
    private string palettePath = string.Empty;
    private bool dither;

    private Rgb24[]? palette;
    private string loadedPalettePath = string.Empty;

    public PixelizeEffect()
        : base(
            "Pixelize",
            "Pixelfit",
            BitmapEffectOptions.Create() with { IsConfigurable = true })
    {
    }

    private enum PropertyNames
    {
        GridSize,
        PhaseX,
        PhaseY,
        PalettePath,
        Dither,
    }

    protected override PropertyCollection OnCreatePropertyCollection() =>
        new(
        [
            // Zero means "estimate it". The override ships from day one because estimation
            // being right most of the time is fine only when an escape hatch exists.
            new Int32Property(PropertyNames.GridSize, 0, 0, Grid.MaxSize),
            new Int32Property(PropertyNames.PhaseX, -1, -1, Grid.MaxSize - 1),
            new Int32Property(PropertyNames.PhaseY, -1, -1, Grid.MaxSize - 1),
            new StringProperty(PropertyNames.PalettePath, string.Empty, 1024),
            new BooleanProperty(PropertyNames.Dither, false),
        ]);

    protected override ControlInfo OnCreateConfigUI(PropertyCollection props)
    {
        ControlInfo config = CreateDefaultConfigUI(props);

        Describe(config, PropertyNames.GridSize, "Grid size", "0 estimates the grid from the image.");
        Describe(config, PropertyNames.PhaseX, "Grid offset X", "-1 estimates the offset.");
        Describe(config, PropertyNames.PhaseY, "Grid offset Y", "-1 estimates the offset.");
        Describe(config, PropertyNames.Dither, "Ordered dither", "4x4 Bayer. Usually looks wrong at sprite sizes.");
        Describe(config, PropertyNames.PalettePath, "Palette (.gpl)", "Leave empty to keep the reduced colours as they are.");

        config.SetPropertyControlType(PropertyNames.PalettePath, PropertyControlType.FileChooser);
        config.SetPropertyControlValue(PropertyNames.PalettePath, ControlInfoPropertyNames.AllowAllFiles, true);

        return config;
    }

    private static void Describe(ControlInfo config, PropertyNames name, string displayName, string description)
    {
        config.SetPropertyControlValue(name, ControlInfoPropertyNames.DisplayName, displayName);
        config.SetPropertyControlValue(name, ControlInfoPropertyNames.Description, description);
    }

    protected override void OnSetToken(PropertyBasedEffectConfigToken? newToken)
    {
        ArgumentNullException.ThrowIfNull(newToken);

        gridSizeSetting = newToken.GetProperty<Int32Property>(PropertyNames.GridSize)!.Value;
        phaseXSetting = newToken.GetProperty<Int32Property>(PropertyNames.PhaseX)!.Value;
        phaseYSetting = newToken.GetProperty<Int32Property>(PropertyNames.PhaseY)!.Value;
        palettePath = newToken.GetProperty<StringProperty>(PropertyNames.PalettePath)!.Value ?? string.Empty;
        dither = newToken.GetProperty<BooleanProperty>(PropertyNames.Dither)!.Value;

        lock (gate)
        {
            // The reduction is cheap to redo; the grid estimate and the source read are not,
            // and neither depends on the token, so both survive a slider move.
            cells = null;
            cellAlpha = null;
        }

        base.OnSetToken(newToken);
    }

    protected override void OnInitializeRenderInfo(IBitmapEffectRenderInfo renderInfo)
    {
        ArgumentNullException.ThrowIfNull(renderInfo);

        // Bgra32 and not a float format: output is a palette entry copied verbatim, and eight
        // bits per channel is exactly what a palette entry has.
        renderInfo.OutputPixelFormat = PixelFormats.Bgra32;
        base.OnInitializeRenderInfo(renderInfo);
    }

    protected override void OnRender(IBitmapEffectOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        (CellGrid grid, byte[] alpha, int size, int originX, int originY) = EnsureCells();
        if (IsCancelRequested)
        {
            return;
        }

        using IBitmapLock<ColorBgra32> outputLock = output.LockBgra32();
        RegionPtr<ColorBgra32> region = outputLock.AsRegionPtr();
        RectInt32 bounds = output.Bounds;

        for (int dy = 0; dy < region.Height; dy++)
        {
            if (IsCancelRequested)
            {
                return;
            }

            int cellY = CellIndex(bounds.Y + dy, originY, size, grid.Height);
            for (int dx = 0; dx < region.Width; dx++)
            {
                int cellX = CellIndex(bounds.X + dx, originX, size, grid.Width);
                int i = (cellY * grid.Width) + cellX;
                Rgb24 c = grid.Cells[i];
                region[dx, dy] = ColorBgra32.FromBgra(c.B, c.G, c.R, alpha[i]);
            }
        }
    }

    private static int CellIndex(int coordinate, int origin, int size, int count) =>
        Math.Clamp((coordinate - origin) / size, 0, count - 1);

    private (CellGrid Grid, byte[] Alpha, int Size, int OriginX, int OriginY) EnsureCells()
    {
        lock (gate)
        {
            source ??= SourceImage.Read(Environment);

            int size = gridSizeSetting > 0 ? gridSizeSetting : EstimatedGrid().Size;
            size = Math.Max(1, size);

            int phaseX = phaseXSetting >= 0 ? phaseXSetting % size : EstimatedPhase(size).X;
            int phaseY = phaseYSetting >= 0 ? phaseYSetting % size : EstimatedPhase(size).Y;

            int originX = Reduce.CellOrigin(phaseX, size);
            int originY = Reduce.CellOrigin(phaseY, size);

            if (cells is not null && cellAlpha is not null)
            {
                return (cells, cellAlpha, size, originX, originY);
            }

            CellGrid grid = Reduce.ToCells(source.Rgb, source.Width, source.Height, size, phaseX, phaseY);

            Rgb24[]? entries = LoadPalette();
            if (entries is { Length: > 0 })
            {
                grid = Pixelize.SnapToPalette(grid, entries, dither);
            }

            cells = grid;
            cellAlpha = ReduceAlpha(source, grid, size, originX, originY);
            return (cells, cellAlpha, size, originX, originY);
        }
    }

    private GridEstimate EstimatedGrid() =>
        estimate ??= Grid.Estimate(source!.Rgb, source.Width, source.Height);

    private (int X, int Y) EstimatedPhase(int size)
    {
        GridEstimate estimated = EstimatedGrid();
        return estimated.Size == size
            ? (estimated.PhaseX, estimated.PhaseY)
            : Grid.EstimatePhase(source!.Rgb, source.Width, source.Height, size);
    }

    /// <summary>
    /// Alpha is taken from the middle of each cell. Averaging it would soften every hard edge
    /// of a cut-out back into the anti-aliased fringe this effect exists to remove.
    /// </summary>
    private static byte[] ReduceAlpha(SourceImage source, CellGrid grid, int size, int originX, int originY)
    {
        byte[] alpha = new byte[grid.Cells.Length];

        for (int cellY = 0; cellY < grid.Height; cellY++)
        {
            int y = Math.Clamp(originY + (cellY * size) + (size / 2), 0, source.Height - 1);
            for (int cellX = 0; cellX < grid.Width; cellX++)
            {
                int x = Math.Clamp(originX + (cellX * size) + (size / 2), 0, source.Width - 1);
                alpha[(cellY * grid.Width) + cellX] = source.Alpha[(y * source.Width) + x];
            }
        }

        return alpha;
    }

    private Rgb24[]? LoadPalette()
    {
        if (string.IsNullOrWhiteSpace(palettePath))
        {
            palette = null;
            loadedPalettePath = string.Empty;
            return null;
        }

        if (palettePath == loadedPalettePath)
        {
            return palette;
        }

        loadedPalettePath = palettePath;
        try
        {
            palette = [.. GplPalette.Load(palettePath).Colors];
        }
        catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException or ArgumentException)
        {
            // A path that is half-typed or not a palette is an ordinary thing to encounter
            // while the dialog is open. Fall back to no palette rather than tearing down.
            palette = null;
        }

        return palette;
    }
}
