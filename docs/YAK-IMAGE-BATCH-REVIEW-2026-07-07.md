# YAK Image Batch — Thorough Accuracy Review (2026-07-07)

> **✅ RESOLVED (same day).** All root causes below were fixed and the batch re-converted for free.
> **Converter fixes:** removed the two stale color-remaps, raised `ColorCap` 3→6, turned the black
> outline off, and added three new cleanup steps to `ImageToGridMath` (with unit tests):
> `ErodeSubject` (peels the glow-halo ring), `KeepLargestSubjectComponent`, and `Despeckle`.
> Also fixed the empty `SourceImageFolder` (the generator had been falling back to procedural blocks).
> **Result:** avg accuracy ~3.3 → **~4.5/5**; every former failure (dog, watermelon, pretzel, apple,
> umbrella, bee, all the yellow→green ones) now reads clearly; the dolphin's scattered specks are gone.
> Final batch = **31/31 solvable, 22 in the APS band, 4–6 colors each**. 333/333 EditMode tests green.
> The per-level notes below are the *original* (pre-fix) diagnosis, kept as the record of what was wrong.

---


A per-level audit of how clearly each generated **level grid** represents its **source object**,
across four lenses: **object clarity · color correctness · background artifacts · detail ambiguity**.

## The headline (read this first)

**The AI source images are excellent. Almost every problem is introduced by the *converter*
(`YAKImageToGrid`), not by OpenAI.** Side-by-side proof:

| Object | Source image (OpenAI) | Converted grid (YAK) | Verdict |
|--------|-----------------------|----------------------|---------|
| 🍉 watermelon | red + yellow seeds + green rind, crisp | solid **dark blob** | converter destroyed it |
| 🐕 dog | clean brown dog, orange muzzle, outlined | featureless **dark blob** | converter destroyed it |
| 🍎 apple | bright **red** apple + green leaf | **near-black** silhouette | color lost |
| 🦆 duck | iconic **yellow** duck, red beak | **green** duck, ragged edge | color forced wrong |
| 🦉 owl | 7 colors (pink/cyan/yellow/brown) | **brown/grey** blob | colors flattened |
| 🍌 banana | **yellow** banana | **green** banana | color forced wrong |

So the fix is **not** better prompts or new images — it's **converter settings**.

## Root causes (precise, config-level — all on `Assets/YAK/Data/Config/ImageToGrid/YAKImageToGrid.asset`)

1. **Two stale color-remaps** — `Source→Green` and `Source→Red`, both `Reach 0.2`. These were tuned
   on 2026-07-06 to fix ONE image (the frog). The `→Green` remap now grabs **yellow** pixels on every
   image → banana, rubber-duck, light-bulb, mug, table-lamp all turn green.
   **Per-image remaps must not live in a shared/batch config.** → *Remove both (or bypass remaps in batch).*
2. **`ColorCap = 3`** — every level is hard-limited to 3 colors. Multi-color subjects can't survive
   (owl 7→3, watermelon 3→1). → *Raise to ~5–6.*
3. **`OutlineSubject = Black` + cap 3** — a thick black outline eats one of only 3 slots; for solid
   subjects the fill then merges into the black → the dark blobs (dog, apple, watermelon).
   → *Turn the outline off for the library, or thin it, or raise the cap so it doesn't crowd out fills.*
4. **Background handling is inconsistent + noisy** — the source stickers have a soft white halo on a
   solid background; the converter's background-neutral pick varies (white / grey / light-blue across
   images) and the anti-aliased halo leaves stray edge pixels (dolphin noise, owl grey, duck/watermelon
   "corners", turtle speck). → *Deterministic background fill + a cleanup pass for isolated pixels.*
5. **30×30 is too coarse for fine internal detail** — the pretzel knot, owl chevrons, alarm-clock
   hands can't survive at grid resolution. Inherent; partly mitigated by #2 (more colors) but a bigger
   grid would help the worst cases.

> Note: my first-pass guess ("palette has no yellow") was **wrong** — the palette has 36 colors
> including Yellow/Gold/Orange/Browns. The yellow→green is the stale **remap**, not a missing color.

---

## Per-level audit (all 31)

Rating: **5** recognizable + right color · **4** clear, minor issue · **3** recognizable but a real
problem · **2** barely · **1** unrecognizable.

### ✗ 1 — unrecognizable (converter destroyed a clear source)
| Level | What's wrong |
|-------|--------------|
| **watermelon** | Source is a perfect red/green/yellow slice. Output = dark teardrop. Red→dark, seeds+rind lost to cap 3, black outline dominates. |
| **dog** | Source is a clean outlined brown dog. Output = solid dark blob — thick black outline + cap 3 merged the whole body to black. No face, no legs. |
| **pretzel** | Source is a classic brown knotted pretzel. Output reads as a **skull/face** — the two lower loops became "eyes"; the knot can't survive 30×30 + 3 colors. |

### ✖ 2 — barely recognizable
| Level | What's wrong |
|-------|--------------|
| **red-apple** | Clear apple *shape* (round + stem + dimple) but rendered **near-black**, not red. Outline+cap collapsed the red fill. Also low contrast on blue. |
| **umbrella** | Dark dome blob; canopy maybe implied, no clear handle, low contrast against the blue background. |
| **bee** | Red body + faint antennae reads more like a beetle/egg. Head/wings ambiguous; missing the yellow-black cue. |
| **pizza** | Triangle hints at a slice, but colors are **green + purple** (moldy-looking); crust/cheese/topping indistinguishable. |

### ⚠ 3 — recognizable but a real problem
| Level | What's wrong |
|-------|--------------|
| **banana** | Great crescent shape — **yellow→green** (stale remap). |
| **light-bulb** | Bulb shape clear — **yellow→green**. |
| **mug-of-coffee** | Mug+handle clear — **→green** (should be white/ceramic + brown). |
| **rubber-duck** | Duck silhouette is great — but **yellow→green** (loses the whole point) + ragged corner artifacts. |
| **table-lamp** | Shade+base reads as a lamp — **→green**. |
| **potted-plant** | Plant+pot readable, but leaves are **dark/muddy** (not green) with a 2-tone "shading" that looks like noise. |
| **owl** | Recognizable as an owl (eyes survive), but a gorgeous 7-color source flattened to brown/grey; grey background is noisy. |
| **fox** | Animal face with ears, but reads as **cat-or-fox** — ambiguous; dark/flat. |
| **house-key** | Key (bow + shaft) is there but **very low contrast** — teal key on blue, almost blends in. |
| **panda** | Reads as a panda but muddy black/brown; face not crisp. |

### ★ 4 — clear, minor issue
| Level | What's wrong (minor) |
|-------|----------------------|
| **cat** | Excellent silhouette; body is **red** (odd) and the face has no internal features (flat). |
| **cupcake** | Reads well; frosting top a little busy/ambiguous. |
| **donut** | Clear ring + hole + sprinkles; color a touch red. |
| **dolphin** | Great blue shape — but **background noise** (stray pixels) from yellow-bg→white + halo. |
| **strawberry** | Red berry + seeds read well; leaves came out **dark**, not green. |
| **pair-of-scissors** | Clear scissors; **pink** instead of metal. |
| **snail** | Spiral shell + body clear; **purple** (odd); head slightly blobby. |
| **teapot** | Spout+handle+lid clear; red acceptable. |
| **turtle** | Clear turtle, **green (correct!)**; one stray pixel, legs a bit blobby. |
| **alarm-clock** | Round face + bells + feet read as a clock; no hands (detail lost). |
| **ice-cream-cone** | Scoop on a cone; pink scoop reads; cone waffle sparse. |
| **panda / grapes** | grapes: cluster reads, correct purple, a bit blobby (also APS 6.0 = very hard). |

### ⭐ 5 — recognizable + right color
| Level | Note |
|-------|------|
| **ladybug** | Red body + dark spots + antennae. Textbook. |
| **penguin** | Upright, dark body + white belly. Correct. |

---

## Score summary
- **Average:** ~3.3 / 5
- **Spread:** 5★×2 · 4★×13 · 3★×10 · 2★×4 · 1★×3
- **Usable as-is (no rework):** ladybug, penguin, cat, donut, dolphin, strawberry, scissors, snail,
  teapot, turtle, alarm-clock, ice-cream-cone, cupcake, grapes — ~14 levels.

## Fix plan (in priority order — all cheap config experiments on the converter)
1. **Remove the two stale color-remaps** → instantly fixes banana, rubber-duck, light-bulb, mug,
   table-lamp (and helps bee/pizza). *Biggest single win, near-zero risk.*
2. **Raise `ColorCap` 3 → 5–6** → recovers owl, watermelon, pizza, potted-plant, strawberry leaves.
3. **Turn off / thin `OutlineSubject`** (or re-test with the higher cap) → fixes dog, apple,
   watermelon dark-blobs.
4. **Improve background handling** — deterministic neutral + strip isolated stray pixels → cleans
   dolphin, owl, duck, turtle, watermelon edges.
5. **Re-convert the whole batch** with the new settings (free — no new OpenAI calls, images are on
   disk) and re-run this review to confirm.

Everything above is **converter-side** — no new image generation needed. Steps 1–3 are a few field
edits we can A/B on 4–5 representative images (banana, watermelon, dog, owl, apple) before re-running all 31.
