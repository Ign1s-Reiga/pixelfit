# Using pixelfit

The complete reference for both front-ends. The [README](../README.md) covers
what the tool is and why it works the way it does; this covers how to drive it.

Every transcript below is real output, produced by running the command against a
fixture committed in this repository. Copy any of them and you will get the same
thing back.

- [Getting the binaries](#getting-the-binaries)
- [Paint.NET: Pixelize](#paintnet-pixelize)
- [Paint.NET: PaletteLens](#paintnet-palettelens)
- [CLI](#cli)
- [Reading the palette report](#reading-the-palette-report)
- [Extracting a palette](#extracting-a-palette-from-an-image)
- [Checking one colour](#checking-one-colour-before-you-use-it)
- [Palette files](#palette-files)
- [When something looks wrong](#when-something-looks-wrong)

---

## Getting the binaries

Windows only. Paint.NET is Windows-only, and although `Pixelfit.Core` and
`Pixelfit.Cli` are portable, the plugin project will not build under WSL.

### CLI

```bash
dotnet build src/Pixelfit.Cli -c Release
```

The executable lands at `src/Pixelfit.Cli/bin/Release/net10.0/pixelfit.exe`.
There is no installer and nothing is put on `PATH` — invoke it by path, or alias
it yourself.

### Plugin

Two assemblies go into Paint.NET's `Effects` folder: `Pixelfit.dll` and
`Pixelfit.Core.dll`. Paint.NET resolves a plugin's dependencies from the folder
it was loaded from, and `Pixelfit.Core.dll` is where the algorithms live.

From an **elevated** shell, because `Effects` sits under `Program Files`:

```bash
dotnet build src/Pixelfit.PaintNet -c Release -t:Deploy
```

Close Paint.NET first or the DLLs are locked, and restart it afterwards — it
scans for plugins only at startup. Both effects then appear under
`Effects > Pixelfit`.

If Paint.NET is installed somewhere other than `C:\Program Files\paint.net`,
pass the location:

```bash
dotnet build src/Pixelfit.PaintNet -c Release -t:Deploy -p:PdnRoot="D:\paint.net"
```

---

## Paint.NET: Pixelize

`Effects > Pixelfit > Pixelize`. Recovers the logical pixel grid of an image
that only looks like pixel art, and collapses each cell to one deliberate
colour.

| Control | Default | Meaning |
|---|---|---|
| Grid size | `0` | Logical pixel size. `0` estimates it from the image. |
| Grid offset X | `-1` | Where the first cell boundary falls. `-1` estimates it. |
| Grid offset Y | `-1` | As above, vertically. |
| Ordered dither | off | 4×4 Bayer, perturbing lightness only. Usually looks wrong at sprite sizes. |
| Palette (.gpl) | empty | Snaps every cell to its nearest entry. Empty keeps the reduced colours as they are. |

Grid size accepts up to 64; the offsets accept `-1` to 63 and are taken modulo
the grid size, so an offset of 63 with a grid of 8 means 7.

### The resize step is not optional

An effect cannot resize the canvas, so Pixelize writes its result back **at the
original resolution**: every cell becomes a block of flat colour. A 1024px image
of a 128px sprite stays 1024px, made of 8×8 blocks.

To get the actual sprite, follow the effect with `Image > Resize`, scaling by
1/grid with **nearest neighbour** interpolation. Any other interpolation
reintroduces exactly the anti-aliasing the effect just removed.

### Transparency

Alpha is sampled from the middle of each cell rather than averaged. Averaging it
would soften every hard edge of a cut-out back into the fringe this effect
exists to remove.

---

## Paint.NET: PaletteLens

`Effects > Pixelfit > PaletteLens`. Reports what is structurally wrong with a
palette. It is a diagnostic and **never modifies your image** — the effect
renders an exact copy of its input, and all the work happens in the dialog.

Closing the dialog cancels, deliberately. Accepting would have Paint.NET apply
the effect, and even though what it applies is an identical copy, that still
lands an entry in the history. A diagnostic should leave no trace of having run.

| Button | What it does |
|---|---|
| Use image colours | Reads the palette from the current layer, ignoring fully transparent pixels. This is what runs on open. |
| Load .gpl… | Reads a palette from a file and checks it against the current layer. |
| Close | Dismisses without touching the image or the history. |

Click a swatch for its index, hex, and OKLCh values. Select a ramp in the list
and its members are outlined in the grid; if they are below the fold the grid
scrolls to the first one.

### The 512-colour limit

The layer's colours are collected up to 512 entries. Past that, two things
change and the dialog says so rather than pretending otherwise:

- The palette shown is the first 512 colours in scan order, which is a slice of
  the image, not a palette.
- **Unused entries are not reported at all.** "Unused" is a claim about the
  whole layer, and against a truncated list it would be a claim about the top of
  one — every entry the artwork uses further down would be reported absent.

The true colour count is always shown, so you can tell a 512-colour layer from a
truncated one:

> This layer has 18819 distinct colours, which is not a palette. Reading the
> first 512 in scan order.

A layer with more colours than that is one Pixelize should be run on first.

---

## CLI

For batch work, and for exercising the algorithms without restarting Paint.NET.

```
pixelfit <input.png> [--palette p.gpl] [--grid N] [--phase X,Y] [--dither]
         [--scale N] -o <out.png>
pixelfit <input.png> --probe
pixelfit check <palette.gpl | image.png>
pixelfit collide <palette.gpl | image.png> <#RRGGBB>
pixelfit palette <image.png> -n <count> -o <out.gpl>
```

| Flag | Meaning |
|---|---|
| `-o`, `--output PATH` | Where to write the result. Required unless `--probe`. Parent directories are created. |
| `--palette PATH` | GIMP palette (`.gpl`). Without one, cell colours are left as reduced. |
| `--grid N` | Logical pixel size. Omit to estimate. |
| `--phase X,Y` | Grid offset. Both components required. Omit to estimate. |
| `--dither` | Ordered 4×4 Bayer. Only has an effect alongside `--palette`. |
| `--scale N` | Also write `out@Nx.png`, magnified N× by nearest neighbour. Ignored for N ≤ 1. |
| `--probe` | Print the grid estimate and exit without writing anything. |
| `-h`, `--help` | Print usage. |

`check`, `collide` and `palette` are separate verbs; see
[the report](#reading-the-palette-report),
[extracting a palette](#extracting-a-palette-from-an-image) and
[checking one colour](#checking-one-colour-before-you-use-it).

Unlike the plugin, `--grid 0` is an error rather than a request to estimate.
Omit the flag instead.

### Output resolution

The CLI writes **at cell resolution** — one output pixel per logical pixel —
because it is not bound by a canvas. This is the opposite of the plugin, and
means no follow-up resize:

```
$ pixelfit tests/Pixelfit.Tests/Fixtures/slime@6x.png --grid 6 --scale 8 -o out.png
16x16 at grid 6, phase 0,0 -> out.png
128x128 -> out@8x.png
```

A 96×96 input at grid 6 gives a 16×16 sprite. `--scale 8` additionally writes a
128×128 copy for looking at, since a 16×16 PNG is hard to inspect.

### Exit codes and streams

`0` on success, `1` on any error and when invoked with no arguments at all.
`--help` is a success.

The report and the result line go to **stdout**; warnings go to **stderr**. That
split is what makes the output pipeable:

```
$ pixelfit tests/Pixelfit.Tests/Fixtures/slime@6x.png --probe
image      96x96
grid       6
  from x   6
  from y   1
phase      0,0
cells      16x16
warning: the axes disagree (6 across, 1 down); using the smaller.
```

Errors are prefixed and terse:

```
$ pixelfit slime@6x.png
pixelfit: No output given; use -o out.png.

$ pixelfit slime@6x.png --nope
pixelfit: Unknown option --nope.

$ pixelfit slime@6x.png --phase 3 -o out.png
pixelfit: --phase needs X,Y, got "3".

$ pixelfit slime@6x.png --grid 0 -o out.png
pixelfit: size ('0') must be greater than or equal to '1'. (Parameter 'size')
```

Arguments are parsed before the image is loaded, so a malformed flag is reported
even when the input does not exist. A missing file reports itself by full path.

### Typical run

Probe first, then convert with what the probe told you:

```bash
pixelfit gen.png --probe
pixelfit gen.png --grid 8 --palette sweetie16.gpl -o sprite.png
```

The filesystem is the integration point with anything upstream. Nothing in this
project talks to an image generator:

```bash
comfy-run prompt.json -o gen.png && pixelfit gen.png --grid 8 -o sprite.png
```

---

## Reading the palette report

`pixelfit check` takes a `.gpl` or a `.png`. Given an image it reads the palette
from the image itself.

```
$ pixelfit check tests/Pixelfit.Tests/Fixtures/shield.png
tests/Pixelfit.Tests/Fixtures/shield.png — 7 distinct colours

ramp 1 (hue ~255°, 5 entries)
  L      0.36  0.51  0.56  0.73  0.40     NOT monotonic — reverses at 5
  hue     269   268   248   236   252     hue shifts -33° from dark to light

warnings
  ramp order   [0], [1], [3], [4], [6] lightness reverses at position 5 of 5
  uneven       [0], [1], [3], [4], [6] lightness steps vary by sigma 0.051
```

A **ramp** is a group of entries sharing a hue whose lightness forms a
progression; entries too grey to have a meaningful hue form one neutral ramp of
their own. Fewer than three entries is not a progression and is not reported as
a ramp. Members are listed in palette order, because that is the order they were
written in and the order a reversal is a reversal with respect to.

| Line | What it tells you |
|---|---|
| `L` | Each member's OKLab lightness, in palette order. |
| `monotonic, step sigma N` | Lightness climbs or falls throughout. Sigma is the spread of the steps — one cramped step is a rung that disappears at small sizes. |
| `NOT monotonic — reverses at N` | Lightness turns around at that position. The ramp does not read as a shading progression. Nearly invisible if you check in RGB. |
| `hue` | Each member's hue in degrees. |
| `hue shifts ±N°` | How far hue moves from the darkest entry to the lightest. **Stated as a fact, never as a correction** — whether yours should shift is your call. |

### Warning kinds

| Label | Meaning |
|---|---|
| `ramp order` | A ramp whose lightness reverses partway through. |
| `uneven` | Step spread above the threshold: the rungs are not evenly spaced. |
| `too close` | Two entries within a small ΔE. You cannot tell them apart, so one is a wasted slot. |
| `greyscale` | Two entries differing in chroma but not lightness. They merge when desaturated, and pixel art has too little area to carry form without lightness contrast. |
| `unused` | A palette entry the image does not use. |

A pair flagged `too close` is never also flagged `greyscale` — that is one
problem, not two.

### Capping

Both pair checks compare every pair of entries. That is right for a palette and
meaningless for a set of colours read out of an image, where it matches by the
hundred thousand. Each kind lists 40 and reports the rest:

```
$ pixelfit check tests/Pixelfit.Tests/Fixtures/shield@8x.png
tests/Pixelfit.Tests/Fixtures/shield@8x.png — 18819 distinct colours; analysing the first 4096 in scan order
...
  too close    [0], [1] #363E50 and #303950 differ by dE 0.020
  too close     and 508498 more pairs are this close; listing stopped at 40
  greyscale     and 209961 more pairs collide too; listing stopped at 40
```

Seeing those two lines means you pointed the report at an image rather than a
palette. That is not a useful thing to check; run Pixelize on it first.

## Extracting a palette from an image

`pixelfit palette` reduces an image to the colours it is actually made of. Every
entry is a colour the image contains, copied verbatim — cluster centres are means
and are never emitted, so nothing here invents a colour.

```
$ pixelfit palette 02-styled-448px.png -n 16 -o extracted.gpl
101596 distinct colours -> 16 entries -> extracted.gpl
```

| Flag | Meaning |
|---|---|
| `-n`, `--count` | How many colours to extract. Default 16. |
| `-o`, `--output` | Where to write the `.gpl`. Required. |

Entries come out grouped by ramp and ascending in lightness, so the palette's own
report reads as a progression rather than as scan order.

### Extract from the crispest source you have

Extraction weights by pixel count, so whatever covers the most area wins — and in
a heavily interpolated image, edge blends cover a lot of area. They are not
colours the artwork uses, but the extractor cannot tell the difference.

Measured against the sample set, reconstructing the same 64×64 sprite:

| Palette | Mean dE | Pixels visibly off |
|---|---|---|
| Hand-made Sweetie 16 | 0.0020 | 1.6% |
| Extracted from the crisp 7× image | 0.0104 | 1.9% |
| Extracted from the bicubic 7× image | 0.0266 | 12.5% |
| Extracted from the bicubic image **after pixelizing** | 0.0098 | 6.4% |

On crisp input, extraction is as good as a hand-made palette. On a blurred one it
is noticeably worse, and the fix is to pixelize first and extract from the
result — cell reduction takes the mode, so the blends are gone before the
extractor ever sees them:

```bash
pixelfit blurry.png --grid 7 -o reduced.png
pixelfit palette reduced.png -n 16 -o out.gpl
pixelfit blurry.png --grid 7 --palette out.gpl -o sprite.png
```

That halves the error on the bicubic sample.

### In Paint.NET

PaletteLens has an **Extract** button with a count beside it. This is what makes
the dialog useful on a generated image: reading the colours of a layer with a
hundred thousand of them gives a scan-order slice rather than a palette, and
every check in the report is then measuring noise.

## Checking one colour before you use it

`pixelfit collide` measures a single candidate against a palette: what it is
indistinguishable from, what it merges with in greyscale, and where it would sit
in any ramp sharing its hue.

```
$ pixelfit collide sweetie16.gpl "#566C86"
#566C86 against Sweetie 16 (16 entries)

  nearest      [14] #566C86 at dE 0.000
  too close    [14] #566C86 at dE 0.000
  greyscale    [2] #B13E53 differs by L 0.007 and will merge when desaturated
  greyscale    [9] #3B5DC9 differs by L 0.009 and will merge when desaturated
  ramp 1       would sit 5th of 8 by lightness; does not extend the progression
```

A colour that clears everything says so, and still reports what the closest
thing you have is:

```
$ pixelfit collide sweetie16.gpl "#7A3B1F"
#7A3B1F against Sweetie 16 (16 entries)

  nearest      [2] #B13E53 at dE 0.131
  no collisions, and no ramp shares its hue
```

`nearest` is reported whether or not anything collided — "the closest thing you
have is this far away" is the useful answer when the answer is *no conflict*.

Thresholds are the same ones the palette report uses, so a pair called too close
here is a pair called too close there.

**`extends the progression`** means the ramp climbs or falls throughout and
putting this colour after its last entry would carry on in the same direction.
It is a statement about the ramp's structure. It is not advice: a colour that
lands mid-ramp is not wrong, it is simply not an extension of that ramp, and
what to do about that is your call.

The same measurement is in PaletteLens — select a swatch and the detail panel
reports what that entry collides with, excluding itself.

### `unused` never appears in the CLI

Worth stating plainly, because it looks like it should. `check image.png` takes
the palette *from* the image, so by construction every entry is used. `check
palette.gpl` has no image to compare against.

Unused reporting exists only in PaletteLens, via *Load .gpl…*, checked against
the open layer — and only when that layer has 512 or fewer distinct colours.

---

## Palette files

Palettes are not authored here. Make them in Aseprite, or take an established
one from [Lospec](https://lospec.com/palette-list), and export as `.gpl`.

The format is plain text: a `GIMP Palette` header, optional `Name:` and
`Columns:` lines, `#` comments, then three space-separated 0–255 integers per
entry with an optional name after them.

```
GIMP Palette
Name: Sweetie 16
Columns: 8
#
 26  28  44	black
 93  39  93	purple
177  62  83	red
```

Lines that do not parse as three channel values are skipped rather than
rejected, so a stray comment or a trailing blank line is harmless. A file whose
first line is not `GIMP Palette` is rejected outright:

```
pixelfit: Not a GIMP palette: the first line must be "GIMP Palette".
```

Inside Paint.NET a file is only needed to check a palette you have not used yet.
For a sprite in progress, *Use image colours* is the common case and needs no
file at all.

---

## When something looks wrong

### The grid came back as 1

There was no periodic structure to find. The grid leaves a trace only at cell
boundaries where the colour actually changes, so art built from large flat
blocks gives the estimator nothing to measure — if the finest feature is two
logical pixels across, the shortest distance between two edges is two cells.

This is not a rare corner: several of this repository's own fixtures probe as
`grid 1`, deliberately, because they were built without single-pixel detail so
that they survive upscaling. Pass `--grid` with the size you know it to be. The
override exists for exactly this and ships from day one.

### The axes disagree

```
warning: the axes disagree (6 across, 1 down); using the smaller.
```

One axis found a period the other did not, or they found different ones. The
smaller is used. Check it with `--probe` and override if it looks wrong — this
warning is common on art whose detail runs mostly one way.

### The output has muddy halos along outlines

That is the signature of averaging cells rather than taking their dominant
colour, which this tool does not do. If you are seeing it, the grid or phase is
wrong, so each cell is straddling a boundary. Probe, then override.

### Dithering looks bad

It usually does at sprite sizes, which is why it is off by default. It exists
for large flat gradients against a small palette, and almost nowhere else.

### The report shows `?` where symbols should be

Fixed — the CLI sets its output encoding to UTF-8 at startup. If you still see
it, something is re-encoding the stream downstream; redirect to a file and read
that instead.

---

## What this tool will not do

Settled, and listed here so you do not go looking:

- No image-editor features. Paint.NET supplies the canvas, pen, eyedropper,
  layers, and undo.
- No standalone GUI. The CLI has no window; interactive use is inside Paint.NET.
- No talking to an image generator. Input is a file that already exists.
- No formats beyond PNG and `.gpl`.
- No installers, releases, or signed binaries. Deployment is "build, copy two
  DLLs, restart Paint.NET".
- **No suggested replacement colours.** PaletteLens reports structure, never
  taste. It will tell you a ramp reverses; it will not tell you what to make it.
