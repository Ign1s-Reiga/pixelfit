using PaintDotNet.Effects;
using PaintDotNet.Imaging;

namespace Pixelfit.PaintNet;

/// <summary>
/// A token with no settings in it. PaletteLens has nothing to configure, and a token that
/// cannot carry a value cannot carry one into the render pass either.
/// </summary>
public sealed class PaletteLensToken : EffectConfigToken
{
    public PaletteLensToken()
    {
    }

    private PaletteLensToken(PaletteLensToken copyMe)
        : base(copyMe)
    {
    }

    public override object Clone() => new PaletteLensToken(this);
}

/// <summary>
/// Reports what is structurally wrong with a palette. It writes nothing.
/// </summary>
/// <remarks>
/// Paint.NET's effect model requires every effect to produce an output image, and this one
/// has nothing to produce: it is a diagnostic, and the user is running it on work in progress.
/// So it copies source to destination unchanged and does all its real work in the dialog.
/// <para>
/// Any code path in which this effect writes a pixel that differs from its source is a bug,
/// not a feature. That is why <see cref="OnRender"/> contains a single bulk copy and no
/// per-pixel arithmetic at all — there is nowhere for a stray write to hide.
/// </para>
/// </remarks>
public sealed class PaletteLensEffect : BitmapEffect<PaletteLensToken>
{
    private IEffectInputBitmap<ColorBgra32>? source;

    public PaletteLensEffect()
        : base(
            "PaletteLens",
            "Pixelfit",
            BitmapEffectOptions.Create() with { IsConfigurable = true })
    {
    }

    /// <summary>
    /// A hand-built dialog rather than IndirectUI, which is made for sliders and checkboxes
    /// and cannot show swatches, ramp groupings or a warning list.
    /// </summary>
    protected override IEffectConfigForm OnCreateConfigForm() => new PaletteLensDialog();

    protected override void OnInitializeRenderInfo(IBitmapEffectRenderInfo renderInfo)
    {
        ArgumentNullException.ThrowIfNull(renderInfo);

        // The output format must match the source exactly, or the copy below would be a
        // conversion and the round-trip through a float format could shift a low bit.
        renderInfo.OutputPixelFormat = PixelFormats.Bgra32;
        source = Environment.GetSourceBitmapBgra32();
        base.OnInitializeRenderInfo(renderInfo);
    }

    protected override void OnRender(IBitmapEffectOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        using IBitmapLock<ColorBgra32> outputLock = output.LockBgra32();
        source!.CopyPixels(outputLock, output.Bounds.Location);
    }
}
