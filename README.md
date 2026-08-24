# pixelfit

Pixel-art tooling for Paint.NET, plus a CLI for batch work.

Two things:

**Pixelize** turns AI-generated "pixel-art-styled" images into actual pixel art.
Diffusion models work in a continuous latent space, so what comes out is an
illustration that resembles pixel art rather than pixel art itself — the grid
does not line up, the colour count runs into the thousands, and anti-aliasing
sits on every edge. Nearest-neighbour downscaling does not fix this, because
there is no consistent grid to downscale onto. Pixelize recovers the grid,
reduces each cell to one deliberate colour, and maps onto a palette.

**PaletteLens** tells you whether a palette is structurally sound. Not whether
it is pretty — that is your call. Whether the shading ramps actually work as
ramps.

## PaletteLens

Run it on a sprite and it reports:

```
ramp 1 (hue ~25°, 4 entries)
  L    0.31  0.44  0.58  0.71     monotonic, step sigma 0.008
  hue    25    25    25    25     no hue shift
ramp 2 (hue ~210°, 3 entries)
  L    0.28  0.35  0.34           NOT monotonic — entries 2 and 3 out of order

warnings
  too close    [5] #8A5940 / [9] #8B5A3C    dE 0.011
  greyscale    [3] and [12] differ by L 0.004 — will merge when desaturated
  unused       [14] #2E1F3D not present in this image
```

What each of these means:

- **Non-monotonic L** — the ramp does not read as a shading progression. Common,
  and nearly invisible when you check in RGB.
- **Uneven L steps** — one cramped step is a rung that disappears at small sizes.
- **Too close** — two entries you cannot tell apart are one entry and a wasted
  palette slot.
- **Greyscale collision** — colours differing in chroma but not lightness vanish
  when desaturated. Pixel art has very little area to work with; without
  lightness contrast the form stops reading.
- **No hue shift** — stated as an observation, not a correction. Shifting hue
  toward cool in shadows and warm in highlights is what most good pixel art
  does, but whether yours should is a decision the tool does not make for you.

It never modifies your image.

## Install

Two assemblies go into Paint.NET's `Effects` folder: `Pixelfit.dll` and
`Pixelfit.Core.dll`. Paint.NET resolves a plugin's dependencies from the folder it
was loaded from, and `Pixelfit.Core.dll` is where all the algorithms live.

From an **elevated** shell, since the `Effects` folder lives under `Program Files`:

```bash
dotnet build src/Pixelfit.PaintNet -t:Deploy
```

Then restart Paint.NET — it only scans for plugins at startup. Both effects appear
under `Effects > Pixelfit`.

Windows only, since Paint.NET is.

### Pixelize inside Paint.NET

An effect cannot resize the canvas, so Pixelize writes its result back at the
original resolution: every cell becomes a block of flat colour. To get the actual
sprite, follow it with `Image > Resize` at 1/grid using **nearest neighbour**.

Set `Grid size` to 0 to have it estimated, or to the logical pixel size if you know
it. `Grid offset X`/`Y` work the same way, with -1 meaning "estimate". Leave the
palette empty to keep the reduced colours as they are.

## CLI

For batch work and for checking algorithm changes without restarting Paint.NET.

```bash
pixelfit input.png --palette mypal.gpl -o out.png
pixelfit input.png --palette mypal.gpl --grid 8 -o out.png   # manual override
pixelfit input.png --probe                                   # estimate only
pixelfit check mypal.gpl                                     # ramp report
```

| Flag | Description |
|---|---|
| `--palette PATH` | GIMP palette (`.gpl`). |
| `--grid N` | Override the estimated logical pixel size. |
| `--phase X,Y` | Override the estimated grid offset. |
| `--probe` | Print the grid estimate and exit. |
| `--scale N` | Also write `out@Nx.png` at nearest-neighbour N×. |
| `--dither` | Ordered 4×4 Bayer. Usually looks wrong at sprite sizes. |

`check` takes a `.gpl` or a `.png`. Given an image it reads the palette from the
image itself and reports unused entries against it.

## When grid estimation will not find the grid

The grid leaves a trace only at cell boundaries where the colour actually changes.
Art that varies at nearly every logical pixel — which is what diffusion models
produce, and what most detailed pixel art looks like — gives the estimator plenty to
work with. Art built from large flat blocks does not: if the finest feature is two
logical pixels across, then the shortest distance between two edges is two cells, and
that is the period the estimator will report.

Interpolation pulls the other way. The single-pixel detail that makes a grid
recoverable is exactly the detail bicubic upscaling destroys, so an image can be easy
to measure and hard to reconstruct, or the reverse, but rarely neither.

This is why `--grid` ships from day one rather than as a later escape hatch. Run
`--probe` first; if the answer looks wrong, pass the right one. Estimation being right
most of the time is fine when saying otherwise costs one flag.

## Palettes

Not authored here. Make them in Aseprite, or pull an established one from
[Lospec](https://lospec.com/palette-list), and export as `.gpl` — plain text,
three space-separated 0–255 integers per line:

```
GIMP Palette
Name: Sweetie 16
#
 26  28  44	black
 93  39  93	purple
177  62  83	red
```

Inside Paint.NET, PaletteLens reads the palette from the image itself by
enumerating unique colours, so no file is needed for the common case.

## Why the output looks better than naive pixelization

**Cell reduction uses the mode, not the mean.** Averaging a cell that straddles
an edge produces a colour present in neither region, which shows up as a muddy
halo along every outline. Taking the dominant colour keeps edges clean.

**Everything happens in OKLab, not RGB or HSV.** RGB distance is not
perceptually uniform. HSV is worse — `V = max(R,G,B)` puts pure yellow and pure
blue at the same "brightness", and hue is undefined noise at low saturation,
which is exactly where outline colours live.

## Build

.NET SDK on Windows. Match the target framework to the official
[Paint.NET v5 effect samples](https://github.com/paintdotnet/PdnV5EffectSamples)
rather than guessing — it moves between Paint.NET releases.

```
dotnet build
dotnet test          # Core only; no Paint.NET dependency
```

## License

[MIT](LICENSE). Published, not distributed — there are no releases and no
support, but do what you like with the code.
