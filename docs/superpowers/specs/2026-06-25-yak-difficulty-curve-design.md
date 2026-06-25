# YAK Difficulty Curve — Design Spec

**Date:** 2026-06-25
**Status:** Approved (brainstorming), ready for implementation plan
**Project:** hoppa-level-editor-core (YAK pipeline)

## Problem

The YAK auto-generation pipeline (image → grid → autofill → analyze → batch) produces
levels of **uniform difficulty**. Every batch level uses the *same* config and the *same*
`TargetAPS`, keyed by a random seed — there is no notion of level number, progression, or
a difficulty ramp.

Consequence observed in-game (Gap C, the `apple_level` proof): a 30×30 level yields
**~45 spools / ~45 taps**, far too complex for an early level. New players would hit a
wall on level 1.

The core insight: **number of taps ≈ total wool ÷ spool capacity**, and total wool is driven
by **grid size**. So "easy" is primarily *small grid + few colors + generous buffer*, and
"hard" is *big grid + more colors + tight buffer*. Fewer colors mainly reduces *visual/sorting*
complexity, not tap count — grid size is the dominant lever.

## Goal

Let a designer define a difficulty **ramp once**, then mass-produce a numbered level set that
follows it. Difficulty is expressed as **tier presets + ordered ranges**, with easy preset
CRUD (create, delete, **duplicate-and-edit-as-new**).

## Scope

- **In scope (this build):** the curve engine — tier presets, the ordered curve, per-level
  knob overrides into the existing generator/autofiller/analyzer, the batch runner, the editor
  window, and routing output through the existing review/export path.
- **Fast-follow (separate task):** wiring the per-tier "max colors" value into the **image
  generation** flow (OpenAI prompt requesting ~N colors + per-tier image regeneration). This
  build only **threads the value through** so it's ready; the grid color-cap already simplifies
  whatever image is fed in.
- **Deferred (YAGNI):** color-fragmentation control; generalizing the curve into Layer 1
  (kept YAK-scoped with clean boundaries so it *can* be lifted later); multi-mechanic toggles
  (YAK has none).

## Architecture (Approach 1: one config asset + a manager window)

### §1 Data model — `YAKDifficultyCurveConfig : ScriptableObject`

One asset holds two lists.

```csharp
[Serializable]
public class TierPreset {
    public string Name;            // "Tutorial", "Easy", ...
    public int    GridWidth;
    public int    GridHeight;
    public int    MaxColors;       // drives grid color-cap now; image-gen later
    public int    AvgCapacity;     // higher = fewer/fatter spools = fewer taps
    public float  CapacitySlack;   // spare buffer fraction; 0 = tight/unforgiving
    public int    ConveyorSlots;
    public Vector2Int ColumnRange; // spool-column count [min,max]
    [Range(0,1)] public float HiddenRatio; // fraction of spools marked IsHidden
    public float  TargetAps;
    public float  ApsTolerance;
}

[Serializable]
public class CurveSegment {
    public string TierName;   // references a TierPreset by Name
    public int    LevelCount; // how many levels of this tier
}

// on the SO:
public List<TierPreset>  Presets;  // CRUD = list rows; duplicate = copy a row
public List<CurveSegment> Curve;   // ordered
```

Helper: `TierPreset TierForLevel(int levelIndex)` walks `Curve` accumulating `LevelCount`
to find the active tier for a 1-based level index. Out-of-range clamps to the last tier.

CRUD is trivial because presets are list entries; duplicate copies a struct/row.

### §2 Per-level override path (the one new wiring)

Today `YAKImageToGrid`, `YAKSpoolAutofiller`, and `YAKLevelAnalyzer` read only their static
config assets. Add an **overrides carrier** the batch fills per level. Each stage reads the
override **if present, else falls back to its static config**, so manual single-level editing
is byte-for-byte unchanged.

Mapping (override → consumer):

| Knob | Consumer (today's static source) |
|---|---|
| GridWidth / GridHeight | `YAKImageToGrid.Convert` (today `profile.GridWidth/Height`) |
| MaxColors | `YAKImageToGrid.ColorCap` |
| AvgCapacity / CapacitySlack / ColumnRange / HiddenRatio | `YAKSpoolAutofiller` (today `YAKSpoolAutofillConfig`) |
| TargetAps / ApsTolerance | autofiller accept-gate + analyzer |
| ConveyorSlots | stamped into `doc.GameData["conveyorCount"]` |

Carrier shape: a small `YAKGenerationOverrides` struct (nullable/optional per field, or a
"hasOverride" flag) passed through the existing generation request. Reuse / extend the
existing `LevelGeneratorRequest` path rather than inventing a parallel channel.

### §3 Batch runner — `YAKCurveBatchHarness`

The "Generate" engine. Walks the curve; for each 1-based level index:

1. `tier = config.TierForLevel(index)`
2. Build `YAKGenerationOverrides` from `tier`.
3. Generate → autofill → analyze, **retrying fresh seeds** until measured APS lands in the
   tier's band (`|aps - TargetAps| <= ApsTolerance`) or attempts are exhausted.
4. Write a **numbered** `level_{index}.json` (+ thumbnail PNG + `.stats`) to a timestamped
   staging folder.
5. If no in-band candidate was found, keep the **best-effort** attempt and **flag it
   off-target** (no silent wrong-difficulty levels — this is also where Gap B APS calibration
   pays off).

Hidden-spool ratio: after autofill, mark `HiddenRatio` fraction of the generated spools
`IsHidden` (deterministic by seed).

This **replaces** today's seed-named uniform batch for curve runs; the existing single-config
batch may remain for ad-hoc use but is not the curve path.

### §4 Editor window — `Window ▸ Hoppa ▸ YAK ▸ Difficulty Curve`

- **Presets panel:** list of tiers with **New / Duplicate / Delete / rename**; selecting one
  edits its knob fields.
- **Curve panel:** ordered rows of `[tier ▾] × [count]` with add / remove / reorder.
- **Generate button:** runs `YAKCurveBatchHarness` → staging folder → opens the existing
  `BatchReviewWindow`.
- **Live readout:** e.g. "30 levels → Tutorial ×5, Easy ×10, Medium ×15" so the ramp is
  visible before committing.

### §5 Output & review

- Produces **numbered** `level_1.json … level_N.json`. The YAK exporter assigns sequential
  game keys 1..N — the exact export path proven in Gap C (2026-06-25).
- Everything routes through the **existing `BatchReviewWindow`** for QA: thumbnail + colors +
  measured APS/band per level, plus the new **off-target flag**. Designer approves / rejects /
  regenerates, then imports/exports.
- Nothing auto-overwrites the live game; the designer controls what is exported.

## Error handling / edge cases

- Empty curve or no presets → friendly no-op with a message.
- A `CurveSegment.TierName` referencing a deleted preset → validation error in the window;
  block generation until resolved.
- Grid too small to hold its `MaxColors` → warn / clamp.
- APS unreachable for a tier's knobs → best-effort + off-target flag (honest; no silent cap).
- `CapacitySlack` adds spare empty capacity only; it never breaks the autofiller's
  exact-wool partition.

## Testing (pure-logic EditMode, matching the existing YAK suite)

- **Curve resolution:** level index → correct tier, incl. segment boundaries and out-of-range
  clamping.
- **Preset CRUD:** duplicate yields an *independent* copy; delete / rename behave.
- **Override fallback:** override present → used; absent → static config (proves manual editing
  is untouched).
- **Hidden-spool ratio:** correct count marked hidden, deterministic per seed.
- **Monotonicity sanity:** smaller grid / fewer colors / bigger capacity ⇒ fewer spools &
  lower measured APS (coarse property check that knobs move difficulty the right way).

## Affected files (from recon, editor-core)

- New: `Assets/YAK/Editor/Generator/YAKDifficultyCurveConfig.cs`,
  `YAKCurveBatchHarness.cs`, `YAKDifficultyCurveWindow.cs`, `YAKGenerationOverrides.cs`
  (+ a config `.asset`).
- Touched (add optional override read, keep static fallback):
  `Assets/YAK/Editor/ImageToGrid/YAKImageToGrid.cs`,
  `Assets/YAK/Editor/Analysis/YAKSpoolAutofiller.cs`,
  `Assets/YAK/Editor/Analysis/YAKLevelAnalyzer.cs`,
  generation request path (`LevelGeneratorRequest` / `YAKLevelGenerator`).
- Reused unchanged: `BatchReviewWindow`, the YAK exporter, the Gap C numbered-file → key path.

## Open follow-ups (not this build)

- Image-gen color tie-in (per-tier `MaxColors` → OpenAI prompt + regeneration).
- Gap B: APS calibration to real-game pacing, which makes the per-tier band trustworthy.
