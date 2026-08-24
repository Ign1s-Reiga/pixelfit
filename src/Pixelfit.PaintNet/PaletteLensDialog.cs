using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using PaintDotNet.Effects;
using Pixelfit.Core;

namespace Pixelfit.PaintNet;

/// <summary>
/// The PaletteLens report.
/// </summary>
/// <remarks>
/// Hand-built rather than IndirectUI, which is made for sliders and checkboxes and has never
/// grown the swatches, groupings and warning list this report needs. Nothing in here writes
/// to the token, because there is nothing to write: the effect renders an identity copy
/// whatever the dialog does.
/// </remarks>
public sealed class PaletteLensDialog : EffectConfigForm<PaletteLensEffect, PaletteLensToken>
{
    private readonly SwatchGrid swatches = new();
    private readonly ListView rampList = new();
    private readonly ListView warningList = new();
    private readonly Label sourceLabel = new();
    private readonly Label detailLabel = new();

    private Rgb24[] palette = [];
    private Rgb24[] imageColors = [];
    private int imageColorTotal;
    private PaletteReport? report;

    public PaletteLensDialog()
    {
        Text = "PaletteLens";
        ClientSize = new Size(820, 560);
        MinimumSize = new Size(680, 460);
        FormBorderStyle = FormBorderStyle.Sizable;
        BuildLayout();
    }

    protected override void OnLoaded()
    {
        base.OnLoaded();

        try
        {
            imageColors = SourceImage.Read(Environment).UniqueColors(out imageColorTotal);
        }
        catch (Exception e) when (e is InvalidOperationException or NullReferenceException or ObjectDisposedException)
        {
            // A report we cannot produce is worth saying so about. It is never worth taking
            // the host down over, and least of all for an effect whose whole promise is that
            // it leaves the user's work alone.
            sourceLabel.Text = $"Could not read the layer: {e.Message}";
            return;
        }

        UseImageColors();
    }

    /// <summary>
    /// Creates the token the host hands back to the effect.
    /// </summary>
    /// <remarks>
    /// Not optional. <c>EffectConfigForm.OnCreateInitialToken</c> is declared virtual but its
    /// base implementation throws, so it is abstract in everything but signature, and the
    /// generic <c>EffectConfigForm&lt;TEffect, TToken&gt;</c> supplies typed overrides for
    /// updating a token but none for creating one. Leaving it alone takes the host down the
    /// moment the dialog is constructed.
    /// <para>
    /// It also runs before this class's constructor and before <c>Effect</c> is set, so it
    /// must not touch anything the dialog builds. Returning an empty token is all it can
    /// safely do, and all PaletteLens needs it to do.
    /// </para>
    /// </remarks>
    protected override EffectConfigToken OnCreateInitialToken() => new PaletteLensToken();

    /// <summary>
    /// The effect has no settings, so there is nothing to copy out of the dialog. Overriding
    /// this to do nothing is what guarantees the token cannot pick up a stray value.
    /// </summary>
    protected override void OnUpdateTokenFromDialog(PaletteLensToken dstToken)
    {
        // Intentionally empty. See the class remarks.
    }

    /// <summary>
    /// Nothing to read back out of the token either — it carries no settings. Overridden
    /// because the base declares this one "must be overridden" as well, on the same
    /// throw-by-default footing as <see cref="OnCreateInitialToken"/>.
    /// </summary>
    protected override void OnUpdateDialogFromToken(PaletteLensToken token)
    {
        // Intentionally empty. See the class remarks.
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(10),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

        sourceLabel.AutoSize = true;
        sourceLabel.Margin = new Padding(3, 3, 3, 8);
        root.Controls.Add(sourceLabel, 0, 0);
        root.SetColumnSpan(sourceLabel, 2);

        Control palettePane = BuildPalettePane();
        root.Controls.Add(palettePane, 0, 1);
        root.SetRowSpan(palettePane, 2);

        ConfigureList(
            rampList,
            ["Ramp", "Hue", "Entries", "Lightness", "Steps", "Hue shift"],
            [70, 55, 60, 190, 70, 90]);
        rampList.SelectedIndexChanged += (_, _) => HighlightSelectedRamp();
        root.Controls.Add(WithHeader("Ramps", rampList), 1, 1);

        ConfigureList(warningList, ["Warning", "Entries", "Detail"], [150, 80, 420]);
        root.Controls.Add(WithHeader("Warnings", warningList), 1, 2);

        Controls.Add(root);
    }

    private Control BuildPalettePane()
    {
        TableLayoutPanel pane = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        pane.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        pane.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pane.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label heading = new() { Text = "Palette", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        pane.Controls.Add(heading, 0, 0);

        swatches.Dock = DockStyle.Fill;
        swatches.BackColor = Color.FromArgb(40, 40, 40);
        swatches.SelectionChanged += (_, _) => ShowSelectedEntry();
        pane.Controls.Add(swatches, 0, 1);

        detailLabel.AutoSize = false;
        detailLabel.Dock = DockStyle.Fill;
        detailLabel.Height = 40;
        pane.Controls.Add(detailLabel, 0, 2);

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        buttons.Controls.Add(MakeButton("Use image colours", UseImageColors));
        buttons.Controls.Add(MakeButton("Load .gpl…", LoadGpl));
        buttons.Controls.Add(MakeCloseButton());
        pane.Controls.Add(buttons, 0, 3);

        return pane;
    }

    /// <summary>
    /// Closing cancels, on purpose. Accepting would have Paint.NET apply the effect, and even
    /// though what it applies is an exact copy, that still lands an entry in the history that
    /// undoes to the same image. A diagnostic should leave no trace of having been run.
    /// </summary>
    private Button MakeCloseButton()
    {
        Button close = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        CancelButton = close;
        return close;
    }

    private static Button MakeButton(string text, Action onClick)
    {
        Button button = new() { Text = text, AutoSize = true };
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Control WithHeader(string title, Control content)
    {
        TableLayoutPanel panel = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = title, AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) }, 0, 0);
        content.Dock = DockStyle.Fill;
        panel.Controls.Add(content, 0, 1);
        return panel;
    }

    private static void ConfigureList(ListView list, string[] columns, int[] widths)
    {
        list.View = View.Details;
        list.FullRowSelect = true;
        list.MultiSelect = false;
        list.HideSelection = false;
        list.UseCompatibleStateImageBehavior = false;
        for (int i = 0; i < columns.Length; i++)
        {
            list.Columns.Add(columns[i], widths[i]);
        }
    }

    /// <summary>
    /// Whether the colour list covers the whole layer, or stopped at the collection limit.
    /// Everything the report says about the layer as a whole depends on this.
    /// </summary>
    private bool ImageColorsAreComplete => imageColors.Length == imageColorTotal;

    private void UseImageColors()
    {
        palette = imageColors;
        sourceLabel.Text = palette.Length switch
        {
            0 => "This layer has no opaque pixels.",
            _ when !ImageColorsAreComplete =>
                $"This layer has {imageColorTotal} distinct colours, which is not a palette. "
                + $"Reading the first {palette.Length} in scan order. PaletteLens never modifies your image.",
            _ => $"Palette read from the image: {palette.Length} distinct colours. "
                + "PaletteLens never modifies your image.",
        };

        Analyze(withImage: true);
    }

    private void LoadGpl()
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "GIMP palette (*.gpl)|*.gpl|All files (*.*)|*.*",
            Title = "Load palette",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            GplPalette loaded = GplPalette.Load(dialog.FileName);
            palette = [.. loaded.Colors];
            sourceLabel.Text = $"Palette \"{loaded.Name}\": {palette.Length} entries. "
                + (ImageColorsAreComplete
                    ? "Unused entries are reported against the current layer. "
                    : $"Unused entries are not reported: this layer has {imageColorTotal} distinct "
                        + $"colours, more than the {imageColors.Length} read. ")
                + "PaletteLens never modifies your image.";
            Analyze(withImage: true);
        }
        catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, e.Message, "Could not read palette", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Analyze(bool withImage)
    {
        // "Unused" is a claim about the whole layer. Made against a colour list that stopped
        // at the limit it is only a claim about the top of one, and every palette entry the
        // artwork uses further down would be reported as absent from artwork that is using it.
        // Withholding the verdict is the only honest answer; a wrong one gets slots deleted.
        bool canReportUnused = withImage && ImageColorsAreComplete;

        report = Ramp.Analyze(
            palette,
            imageColors: canReportUnused ? imageColors : ReadOnlySpan<Rgb24>.Empty);

        swatches.SetEntries(palette);
        FillRamps(report);
        FillWarnings(report);
        ShowSelectedEntry();
    }

    private void FillRamps(PaletteReport current)
    {
        rampList.BeginUpdate();
        rampList.Items.Clear();

        for (int i = 0; i < current.Ramps.Count; i++)
        {
            RampReport ramp = current.Ramps[i];
            ListViewItem item = new((i + 1).ToString(CultureInfo.InvariantCulture))
            {
                Tag = ramp,
            };

            item.SubItems.Add(ramp.IsNeutral ? "neutral" : $"{ramp.HueDegrees:F0}°");
            item.SubItems.Add(ramp.Indices.Count.ToString(CultureInfo.InvariantCulture));
            item.SubItems.Add(string.Join("  ", ramp.Lightness.Select(l => l.ToString("F2", CultureInfo.InvariantCulture))));
            item.SubItems.Add(
                ramp.IsMonotonicLightness
                    ? $"σ {ramp.LightnessStepDeviation:F3}"
                    : "not monotonic");

            // Stated as a fact. Whether a ramp should shift hue is the author's call, and
            // nothing here is allowed to suggest an answer.
            item.SubItems.Add(
                MathF.Abs(ramp.HueShiftDegrees) < 1f
                    ? "none"
                    : $"{ramp.HueShiftDegrees:+0;-0}°");

            rampList.Items.Add(item);
        }

        rampList.EndUpdate();
    }

    private void FillWarnings(PaletteReport current)
    {
        warningList.BeginUpdate();
        warningList.Items.Clear();

        foreach (PaletteWarning warning in current.Warnings.OrderBy(w => w.Kind))
        {
            ListViewItem item = new(Describe(warning.Kind));
            item.SubItems.Add(string.Join(", ", warning.Indices));
            item.SubItems.Add(warning.Message);
            warningList.Items.Add(item);
        }

        if (warningList.Items.Count == 0)
        {
            warningList.Items.Add(new ListViewItem("none") { ForeColor = Color.Gray });
        }

        warningList.EndUpdate();
    }

    private static string Describe(PaletteWarningKind kind) => kind switch
    {
        PaletteWarningKind.TooClose => "too close",
        PaletteWarningKind.GreyscaleCollision => "greyscale collision",
        PaletteWarningKind.NonMonotonicRamp => "ramp not monotonic",
        PaletteWarningKind.UnevenLightnessSteps => "uneven lightness steps",
        PaletteWarningKind.Unused => "unused",
        _ => kind.ToString(),
    };

    private void HighlightSelectedRamp()
    {
        if (rampList.SelectedItems.Count == 1 && rampList.SelectedItems[0].Tag is RampReport ramp)
        {
            swatches.Highlight(ramp.Indices);
        }
        else
        {
            swatches.Highlight([]);
        }
    }

    private void ShowSelectedEntry()
    {
        int index = swatches.SelectedIndex;
        if (index < 0 || index >= palette.Length)
        {
            detailLabel.Text = string.Empty;
            return;
        }

        Rgb24 c = palette[index];
        OklchColor lch = Oklab.OklchFromSrgb(c);
        detailLabel.Text =
            $"[{index}] {c}   rgb({c.R}, {c.G}, {c.B})\n"
            + $"L {lch.L:F3}   C {lch.C:F3}   h {lch.H:F0}°";
    }
}
