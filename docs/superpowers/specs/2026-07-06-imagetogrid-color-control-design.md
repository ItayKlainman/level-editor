# ImageToGrid — Color Control Features (remap + dominant downscale)

**Date:** 2026-07-06
**Status:** Approved (design)
**Scope:** `YAKImageToGrid`, `BusBuddiesImageToGrid`, and shared `Core.Editor` image math.

## Problem

When converting a source image to a level grid, the subject's colors can come out
wrong. Concrete case: a **lime / yellow-green frog** converts to mostly **yellow**.
Two root causes:

1. **Nearest-palette matching with no steering.** A lime pixel (high R, high G, low B)
   sits between the palette's Yellow and Green in RGB and tips toward Yellow. There is
   currently no way to steer the result.
2. **Area-average downscale muddies squares.** Each output square averages all source
   pixels under it, so dark outlines/eyes and highlights bleed into neighbouring squares
   and pull colors off.

## Goals

Add two designer-facing controls to both converters:

- **(#2) Optional color remap** — "treat *this* source color as *that* palette color"
  (e.g. lime → Green), with an adjustable reach.
- **(#4) Dominant-color downscale** — each square takes its majority color instead of the
  blurred average, for truer squares. A toggle, defaulting to Dominant.

An "allowed-colors subset" feature was considered and **explicitly dropped** from scope.

## Non-goals

- Changing the perceptual distance metric itself (redmean stays).
- A hue-space matcher.
- Any change to segmentation, outline, or background-neutral behavior.
- Reproducing/re-converting old levels identically (see Compatibility).

---

## Architecture

The pixel math is currently **duplicated** in `YAKImageToGrid` and `BusBuddiesImageToGrid`
(BB deliberately does not reference the `Hoppa.YAK.Editor` assembly, so the helpers were
copy-mirrored). The new logic needs that same math, so rather than triple the duplication we
**extract the pure image math into the core Editor package**, which both converters already
reference. Future converters get it for free.

### New file: `Packages/com.hoppa.leveleditor.core/Editor/ImageToGrid/ImageToGridMath.cs`

A `static` class of pure functions (no Unity asset state), holding the helpers moved out of
both converters plus the two new ones:

- `struct PaletteColor { string Id; Color C; }` (promoted to public core type)
- `List<PaletteColor> BuildPalette(GameProfile)`
- `double RedmeanDist(Color a, Color b)`
- `int NearestIndex(Color c, IReadOnlyList<PaletteColor> palette)`
- `string NearestId(Color c, IReadOnlyList<PaletteColor> palette)`
- `float Luminance(Color c)`
- `bool[] ComputeOutlineMask(bool[] isBg, int W, int H)`
- `Color[] Downscale(Color[] src, int sw, int sh, int W, int H, SampleMode mode, IReadOnlyList<PaletteColor> palette)`
- `void MergeToCap(string[] ids, IReadOnlyList<PaletteColor> palette, int cap, bool[] isEmpty, string protectedId)`
- `string ResolveRemap(Color cellColor, IReadOnlyList<ColorRemap> remaps, IReadOnlyList<PaletteColor> palette)` — **new**
- Segmentation helpers `BorderRing`, `BySaturation`, `Fraction` (identical in both) move here too.

### New shared types (in core, so both converters can serialize them)

```csharp
public enum SampleMode { AreaAverage, Dominant }

[Serializable]
public sealed class ColorRemap
{
    public Color  Source;              // picked via ColorField swatch/eyedropper
    public string TargetColorId;       // a palette ColorId
    [Range(0f, 1f)] public float Reach = 0.15f;
}
```

### Converter changes (both `YAKImageToGrid` and `BusBuddiesImageToGrid`)

Each keeps its own `Convert()` orchestration, game-specific field defaults, and emit logic
(`YAKWoolCell` vs `BBPixelCell`/`BBEmptyCell`). It stops holding private copies of the math
and instead calls `ImageToGridMath`. Each gains two serialized fields:

```csharp
[Header("Sampling")]
public SampleMode Sampling = SampleMode.Dominant;   // default = Dominant

[Header("Color Remap")]
public List<ColorRemap> ColorRemaps = new List<ColorRemap>();  // empty = no remaps
```

The per-converter `Segment()` wrapper stays (it carries the game-tagged debug log and the
ambiguity fallback) but delegates to the shared `BorderRing`/`BySaturation`.

---

## Feature #4 — Dominant downscale

`ImageToGridMath.Downscale(..., SampleMode mode, palette)`:

- **AreaAverage** — existing behavior, byte-for-byte unchanged (the current area-average).
- **Dominant** — for each output square's source-pixel region:
  1. Bin each source pixel by `NearestIndex(pixel, palette)`.
  2. Pick the **majority** bin (highest count; tie → lowest palette index).
  3. The square's representative color = the **mean of the source pixels in that bin**.
  4. If the region is empty (zero pixels, the existing `n==0` edge), fall back to the plain
     area-average of the region.

Returning the mean of the majority bin (rather than the palette color itself) keeps a *real*
color flowing into segmentation and remap, so no downstream step needs special-casing.

**Default:** `Sampling = Dominant` on both converters.

---

## Feature #2 — Color remap (optional)

### Matching

At the quantize step, every cell resolves its id as:

```
id = ImageToGridMath.ResolveRemap(cellColor, ColorRemaps, palette) ?? NearestId(cellColor, palette)
```

`ResolveRemap`:

1. Consider only remaps whose `TargetColorId` exists in the palette (others ignored).
2. Compute a **normalized color distance** between `cellColor` and each remap's `Source`:
   `dist = sqrt(RedmeanDist(a,b)) / sqrt(RedmeanDist(black, white))`, giving ~`[0,1]`.
3. Keep remaps with `dist <= Reach`. Among them, pick the **smallest dist**; ties → the
   earliest entry in the list.
4. Return its `TargetColorId`, or `null` if none matched.

Remap runs uniformly on every cell at quantize time. Background and outline cells are
overwritten by their own steps afterward (neutral/empty fill, outline color), so a remap
visibly affects the **subject** (and any background cell not later overwritten). `Reach = 0`
matches only near-exact colors; higher `Reach` widens the catch.

### Interaction with #4

Remap operates on the post-downscale cell color, so with Dominant sampling the match is
against the truer square color — the two features compose cleanly.

### UI

The 🖼 Image panel already renders the converter asset's inspector via
`Editor.CreateEditor(converter).OnInspectorGUI()` (see `ImageToGridModePanel`).

- **Source color** — a standard `ColorField`. Unity's ColorField ships with a swatch **and**
  a screen-space eyedropper, so "pick from the image" and "set by hand" both work with no
  custom preview-clicking. The designer opens the picker and eyedrops the frog directly off
  the source preview (or the source asset).
- **TargetColorId** — a **dropdown of the profile palette's ColorIds** (not free text).
- **Reach** — a 0–1 slider.

To render the palette dropdown, add **one shared custom inspector**
`ImageToGridAssetEditor : UnityEditor.Editor` (in `Core.Editor`, `[CustomEditor(typeof(ImageToGridAsset), true)]`,
`editorForChildClasses: true`) reused by every converter. It reads the active palette from a
static context the panel publishes each frame before drawing:

```csharp
// In ImageToGridModePanel.DrawParams, before _configEditor.OnInspectorGUI():
ImageToGridAssetEditor.ActivePalette = profile.ColorPalette;
```

If `ActivePalette` is null (the SO is inspected outside the panel), TargetColorId falls back
to a plain text field. The custom inspector draws the default fields for everything else, then
the `ColorRemaps` list with the three controls above.

**Layout note:** `ImageToGridModePanel` currently gives the embedded inspector a fixed 320px
area inside a fixed 720px scroll region. A variable-length remap list can overflow this; the
implementation must make those heights dynamic (or grow them) so the list and buttons remain
reachable.

---

## Testing

### Core math tests (new, in the core Editor test assembly)

- **Dominant sampling:** a square whose region is 60% green + 40% yellow resolves to green
  (area-average would land between them); a mostly-solid square with a thin minority
  anti-alias edge ignores the edge.
- **AreaAverage unchanged:** same input → same output as before the refactor (regression guard).
- **ResolveRemap:** in-reach → target; out-of-reach → null; closest of several wins; tie →
  first entry; `TargetColorId` not in palette → ignored; `Reach = 0` → only near-exact matches.
- **Normalized distance** is 0 for identical colors and ~1 for black↔white.

### Converter tests (YAK + BB)

- A synthetic lime-block image with remap `lime → Green` produces Green subject squares
  (reproduces and fixes the frog case).
- Dominant default yields a cleaner two-tone square than AreaAverage on the same input.
- Empty `ColorRemaps` + `Sampling = AreaAverage` reproduces current output (opt-out path).

### Compatibility

Flipping the default to **Dominant** changes conversion output, so existing YAK/BB image
tests that assert area-average results will need their expected values re-baselined. This is
expected and in-scope. Converting an old source image will now produce a (better) different
grid; already-saved level JSON is unaffected (conversions are one-shot, then hand-edited).

---

## Files

**New**
- `Packages/com.hoppa.leveleditor.core/Editor/ImageToGrid/ImageToGridMath.cs`
- `Packages/com.hoppa.leveleditor.core/Editor/ImageToGrid/ImageToGridAssetEditor.cs`
- Core Editor tests for the math (+ remap/dominant).

**Modified**
- `Assets/YAK/Editor/ImageToGrid/YAKImageToGrid.cs` — call shared math; add `Sampling` + `ColorRemaps`.
- `Assets/BusBuddies/Editor/ImageToGrid/BusBuddiesImageToGrid.cs` — same.
- `Packages/com.hoppa.leveleditor.core/Editor/ImageToGrid/ImageToGridModePanel.cs` — publish
  `ActivePalette`; make the embedded-inspector area height dynamic.
- Existing YAK/BB image-to-grid tests — re-baseline for Dominant default; add remap/dominant cases.
