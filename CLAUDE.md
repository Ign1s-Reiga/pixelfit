# pixelfit

Pixel-art tooling built around a shared algorithm core, delivered primarily as
a Paint.NET plugin.

Two things it does:

1. **Pixelize** — convert AI-generated "pixel-art-styled" images into actual
   pixel art: correct grid alignment, fixed palette, no anti-aliasing.
2. **PaletteLens** — analyse a palette's *structure* and report what is broken:
   non-monotonic shading ramps, uneven lightness steps, colours too close to
   distinguish, entries that collapse in greyscale.

Personal tool. Single user, single machine.

The source is public, at `github.com/Ign1s-Reiga/pixelfit`. That is publication,
not distribution: no releases, no binaries, no support, and no obligation to
anyone else's setup. Nothing below this line changes because the code can be
read. In particular, "it would help other users" is not an argument for
reopening any of the non-goals — a second reader is not a second requirement.

---

## The shape of the project, and why

An earlier draft of this project was a standalone CLI, and separately
considered building a whole image editor. Both were wrong, for the same
reason: **the goal is colour-selection support while drawing**, and neither a
batch CLI nor a from-scratch editor serves that.

Paint.NET already provides the canvas, the pen, the eyedropper, undo/redo, and
layers. Those are solved problems and re-implementing them produces a worse
Aseprite. What Paint.NET does *not* provide is any notion of whether a palette
is structurally sound. That gap is the entire product.

So: the plugin is the primary front-end. The CLI exists for batch work and for
testing the algorithms without launching a GUI.

```
src/
  Pixelfit.Core/          algorithms only — no UI, no file I/O, no ImageSharp
    Oklab.cs              sRGB <-> linear <-> OKLab <-> OKLCh, NearestInPalette
    Grid.cs               EstimateGridSize, EstimatePhase
    Reduce.cs             cell -> representative colour
    Ramp.cs               ramp detection and palette structure diagnostics
    GplPalette.cs         .gpl parse/write
  Pixelfit.PaintNet/      Paint.NET plugin — two effects, one DLL
    PixelizeEffect.cs
    PaletteLensEffect.cs
    PaletteLensDialog.cs
  Pixelfit.Cli/           batch front-end; ImageSharp lives here and only here
tests/Pixelfit.Tests/     tests against Core; no Paint.NET dependency
```

`Pixelfit.Core` must never reference Paint.NET, ImageSharp, or WinForms. It
takes and returns plain arrays. This is what lets the tests run without a GUI
and what keeps the two front-ends from diverging.

## Non-goals — settled, do not re-open

- **No image-editor features.** No pen, selection, layers, undo/redo, canvas,
  zoom, or transforms. Paint.NET supplies all of these. Building any of them
  means the project has lost its way.
- **No standalone GUI application.** The CLI has no window. Interactive use
  happens inside Paint.NET.
- **No bundled or called generative model.** Input is an image that already
  exists. This tool never talks to ComfyUI or any image-generation API.
  The integration point is the filesystem, and it already works:
  `comfy-run prompt.json -o gen.png && pixelfit gen.png ...`
- **No non-standard formats.** PNG (ISO/IEC 15948) and GIMP Palette (`.gpl`).
  Not `.aseprite`.
- **No packaging work.** No installers, no NativeAOT, no signed binaries, and
  no release artifacts. The repository being public does not change this; the
  deployment step is still "build, copy two DLLs, restart Paint.NET".
- **No Python.** Not for the tool, not for scripts, not for test helpers.
- **PaletteLens must never modify pixels.** See below — this is load-bearing.

## Platform

Paint.NET is Windows-only. **Build and run on the Windows side, not inside
WSL2.** `Pixelfit.Core` and `Pixelfit.Cli` are portable and their tests run
anywhere, but the plugin project will not build under WSL.

## Paint.NET plugin

Paint.NET 5.x plugins derive from `BitmapEffect` (CPU) or `GpuEffect` (GPU).
This project uses `BitmapEffect`; the workloads are tiny and GPU adds nothing.

**Get the target framework and `.csproj` layout from the official samples**
(`github.com/paintdotnet/PdnV5EffectSamples`) rather than guessing. Plugin
projects fail in confusing ways when the TFM does not match the host's
runtime, and the version moves between Paint.NET releases.

API reference: `paintdotnet.github.io/apidocs/`

**Parts of this API are abstract in behaviour and virtual in signature.** Some
members are declared `virtual` with a base body that throws
`NotImplementedException`, so the compiler will not tell you that you had to
override them, and nothing fails until a live host reaches the code. On
`EffectConfigForm` this applies at least to `OnCreateInitialToken` and
`OnUpdateDialogFromToken` — leaving either alone takes the whole application
down the moment the dialog is constructed, not just the dialog. Before
deriving from a Paint.NET base class, read its XML docs in the install folder
for "must be implemented"/"must be overridden" and override those members even
when there is nothing to do; a no-op override is the point.

Note also that `OnCreateInitialToken` runs *before* the derived constructor
body and before `Effect` is set, so it must not touch anything the dialog
builds.

Deployment is two DLLs dropped into the `Effects` folder — the plugin and
`Pixelfit.Core`, since dependencies resolve from the folder the plugin loaded
from. The folder lives under `Program Files`, so the copy needs an elevated
shell. Paint.NET scans for plugins only at startup, so a restart is required
after every build, and the host must be closed first or the DLLs are locked —
factor this into how you iterate, and prefer testing algorithm changes through
the CLI and unit tests rather than through the plugin.

What the unit tests cannot reach is exactly this: they never load Paint.NET,
so no test will ever catch a host-contract failure of the kind above. Type
checks over the built assembly do not either — an effect can be perfectly
discoverable and still throw the instant its dialog opens. Anything touching
the plugin's host surface has to be confirmed by opening it.

### PixelizeEffect

An ordinary transform. Source surface in, pixelized surface out. Parameters
(grid size, manual override, dither) fit IndirectUI fine.

### PaletteLensEffect — the no-op effect pattern

Paint.NET's effect model requires every effect to produce an output image.
PaletteLens has nothing to write: it is a diagnostic that must leave the user's
artwork untouched.

So it copies source to destination unchanged and does all its real work in the
config dialog. **Any code path in which PaletteLens writes a pixel that differs
from its source is a bug**, not a feature — the user is running this on work in
progress, and silently altering it would be the worst failure this project
could have.

IndirectUI is not sufficient for the dialog. It is built for sliders and
checkboxes, and reports of it lacking even tab support go back years. The
report needs colour swatches, ramp groupings, and a warning list, so
`PaletteLensDialog` is a custom WinForms dialog.

The palette comes from the image itself — enumerate unique colours of the
current layer. For pixel art this is exactly right and needs no file. Loading
a `.gpl` is a secondary path for checking a palette before using it.

## Algorithms

### OKLab and OKLCh (`Oklab.cs`)

All colour distance is computed in OKLab. **Not RGB, and not HSV** — both are
bugs here, not simplifications:

- RGB Euclidean distance is not perceptually uniform. It collapses shadow
  detail and pulls skin tones toward grey.
- HSV is worse. `V = max(R,G,B)` means pure yellow and pure blue share V=1.0
  despite an enormous difference in apparent brightness; H is a circle, so
  naive distance treats 359° and 1° as far apart; H is undefined noise at low
  saturation, which is exactly where pixel art puts its outline colours.

OKLab's ability to express colours outside the sRGB gamut is **not a problem
here**, and this is worth stating because it looks like one. OKLab is used only
as a ruler. Both sides of every comparison originate in sRGB — image pixels and
palette entries — and output is always a palette entry copied verbatim. There
is no OKLab-to-sRGB conversion in any production path, so an out-of-gamut
colour cannot arise. Gamut mapping would only become necessary if the project
ever *generated* colours (auto-deriving a ramp, hue-shifting), which is not in
scope.

Conversion chain: sRGB (0–255) → linear sRGB (the piecewise transfer function,
including the linear segment below 0.04045 — **not** `MathF.Pow(x, 2.2f)`) →
LMS → cube root (`MathF.Cbrt`, which handles negatives; `MathF.Pow(x, 1f/3f)`
returns NaN) → OKLab.

OKLCh is the polar form and is three lines from OKLab. Use it for ramp
detection and anywhere hue has meaning. **Keep nearest-neighbour search in
cartesian OKLab** — going polar reintroduces the hue wraparound problem for no
benefit.

### Grid estimation (`Grid.cs`)

The only genuinely non-obvious algorithm. Recover the logical pixel size `s` —
a 1024px image that "is" a 128px sprite has `s = 8`.

1. Grayscale, then gradient magnitude (forward difference is enough).
2. Project onto each axis — sum gradient magnitude down columns for x, across
   rows for y.
3. Autocorrelate each 1D signal. First strong peak at lag > 1 is the candidate
   period. **Subtract the signal mean first**, or lag 0 dominates everything.
4. x and y should agree. If they differ by more than 10%, warn and use the
   smaller.
5. Phase refinement: for each offset `0..s-1`, sum intra-cell colour variance.
   Pick the minimum. Wrong phase is more visually destructive than a slightly
   wrong `s`.

No FFT library. The signals are at most a few thousand samples, so naive
O(n²) correlation is a few million operations. Do not add MathNet.Numerics.

Manual override ships from day one. Estimation being right 90% of the time is
fine when the escape hatch exists.

### Cell reduction (`Reduce.cs`)

**Never use the mean.** Averaging a cell that straddles an edge produces a
colour present in neither region — a muddy halo on every outline, and the most
recognizable failure of naive pixelization.

Use the mode: quantize the cell's colours coarsely (5 bits per channel packed
into a `ushort`), histogram, take the winning bin, then average only the pixels
in that bin. Fall back to the median when no bin has a clear plurality.

`Parallel.For` over rows of cells. Do not reach for `Vector<T>` SIMD; the
workload does not justify it.

### Ramp analysis (`Ramp.cs`)

This is what PaletteLens reports. Everything here is a **structural** check —
whether the palette is internally consistent — never an aesthetic judgement.
The tool does not have taste and must not pretend to.

Detect ramps by grouping entries with similar OKLCh hue whose L values form a
progression. Then report:

- **Monotonic L.** A ramp whose lightness is not strictly increasing does not
  read as a shading progression. Very common and nearly invisible in RGB.
- **Even L steps.** Report the standard deviation of successive L deltas. One
  cramped step is a rung that disappears.
- **Pairs too close.** Two entries within a small ΔE waste a palette slot.
- **Greyscale collisions.** Entries differing in chroma but not in L vanish
  when desaturated. Pixel art has little area to work with; without lightness
  contrast the form stops reading.
- **Hue shift, stated as fact only.** Good pixel art shifts hue toward cool in
  shadows and warm in highlights. Report *whether* a ramp shifts hue.
  **Do not tell the user to shift it.** That is the author's call, and this is
  the boundary between structure and taste.

Unused-entry reporting (which palette slots the current image actually uses) is
in scope and useful. Suggesting replacement colours is not.

### Extraction yes, adjustment no

`Extract` reduces an image to the colours it is made of. That is a measurement —
the same category as unused-entry reporting — and every colour it returns is one
the image contains, copied verbatim. Cluster centres are means and are never
emitted: that would invent a colour present nowhere in the source and would put
an OKLab-to-sRGB conversion in a production path, which this project does not
have.

**This is the boundary, and it is easy to step over by accident.** Now that the
tool emits palettes, "space that ramp evenly", "shift those shadows cooler" and
"here is a better midtone" are one small step away, and all three are taste. The
line is that pixelfit may report which colours an image contains and what is
structurally true of them. It may not invent a colour, alter one, or rank one.
Anything that computes a colour the source does not contain is out.

## Testing

Tests target `Pixelfit.Core` and never load Paint.NET.

The load-bearing test is a round-trip against ground truth:

1. Take real, hand-made pixel art from `tests/Pixelfit.Tests/Fixtures/`.
2. Upscale by a known factor with bicubic interpolation, add mild noise. Commit
   the generated inputs so tests are deterministic.
3. Run the pipeline with the sprite's own palette.
4. Assert the result is pixel-identical to the original.

**Test non-power-of-two factors** — 6× and 11× expose phase bugs that 8×
accidentally passes.

Also required: a round-trip test on the colour conversions. sRGB → OKLab →
sRGB must return the input within 1/255 across the full range, including 0 and
255. Write this first; every downstream check is meaningless until it passes.

### CI

`.github/workflows/build.yml` builds `Pixelfit.Core` and `Pixelfit.Cli` and runs
the test suite on `windows-latest`, for pushes to `main` and for every pull
request. `TreatWarningsAsErrors` is on everywhere, so a warning fails the build.

**`Pixelfit.PaintNet` is not built there and cannot be.** It references Paint.NET's
assemblies by `HintPath` out of the install directory, which no runner has and
which are not redistributable — `dotnet build` on the solution fails in CI, which
is why the workflow names projects rather than the solution. Do not try to "fix"
this by adding the plugin; the only way to check it is to open the effect in
Paint.NET. A green build says nothing about the plugin.

## Conventions

- Nullable reference types enabled. `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- File-scoped namespaces.
- Pixel data crosses `Core` boundaries as plain `byte[]` or a simple RGB struct
  with explicit `width`/`height` — never a Paint.NET `Surface` or an ImageSharp
  `Image<T>`. Colour-space intermediates are `float[]`.
- Prefer `Span<T>` / `ReadOnlySpan<T>` in signatures where the method does not
  store the reference.
- `static` methods unless there is real state. There mostly is not.
- Methods under ~40 lines. If a stage does not fit, it is two stages.
- No logging framework, no config file, no DI container.
