# Bus Buddies — Designer-Controlled Difficulty Model

**Date:** 2026-07-09
**Status:** Approved (brainstorm), pending implementation plan
**Supersedes (partially):** APS-target difficulty in `2026-06-25-yak-difficulty-curve-design.md` as applied to Bus Buddies

## 1. Motivation

Today Bus Buddies difficulty is set by an **APS target** (`TargetAps`) that the autofiller
tries to *hit* by random search, with a Monte-Carlo "average player" measuring APS and the
solver gating on solvability. Two problems:

- **APS plateaus (~5.0 on 30×30)** so high tiers are unreachable and burn all their attempts.
- The lever is **opaque** — designers can't directly say "make this level harder"; they tune a
  number and hope.

The boss's preferred model (captured in an Excel mock-up) is **explicit and designer-controlled**:
difficulty is a small set of knobs derived from the *actual image pixel counts*, and the new
"dig" axis directly shapes the play experience. This spec makes that model the **primary**
difficulty control for Bus Buddies. APS becomes a **measured read-out**, and the solver stays as
a **solvability guardrail** — not a difficulty target.

## 2. The model (designer's mental model)

Given an authored/converted grid, we know the **pixel count per color** (e.g. Purple 500,
Green 200, Black 100, Brown 30, White 20 = 850 total). Six knobs shape the bus queue:

| Knob | Range | Meaning |
|------|-------|---------|
| **Buses Chunks** | 1–5 | Sets **avg pixels per bus**. Higher = fatter buses = fewer buses overall. |
| **Deviation %** | 0–100% | Capacity spread around the avg (± window). |
| **Number of Columns** | 1–5 | How many queue columns the buses fill. |
| **Difficulty** | 1–5 | How deeply the **main colors** are buried in the queues (the "dig" axis). |
| **☐ No 1-bus color** | bool | If on, no color may occupy a single bus — split into ≥2. |
| **☐ Round to 5** | bool | Prefer bus capacities that are multiples of 5 (…,10,15,20,25,…). Best-effort. |

### 2.1 Buses Chunks → capacity

`Buses Chunks` maps to **average pixels per bus** via a configurable base + step
(default matches the Excel):

| Chunks | 1 | 2 | 3 | 4 | 5 |
|--------|---|---|---|---|---|
| Avg px/bus | 10 | 15 | 20 | 25 | 30 |

- `avg = ChunksBase + (chunks - 1) * ChunksStep` (default `ChunksBase=10`, `ChunksStep=5`).
- **Deviation %** sets the capacity window: `half = round(avg * deviation)`, `min = max(1, avg-half)`,
  `max = avg+half`. (Excel: avg 30, dev 50% → min 15, max 45.)
- Each **color** is partitioned into buses of capacity in `[min,max]`, **summing exactly** to that
  color's pixel count (existing `Partition` behaviour — every pixel is carried). Bus count per color
  ≈ `round(colorPixels / avg)`; total buses ≈ `round(totalPixels / avg)`.

### 2.2 The two rules

- **No 1-bus color** (when enabled): any color whose partition yields a single bus is re-split into
  **≥2 buses** (e.g. White 20 → 10/10 or 15/5). Applied per color, after the base partition.
- **Round to 5** (when enabled): the partition prefers capacities that are multiples of 5 while still
  summing exactly to the color total. Best-effort — when an exact all-multiples-of-5 partition is
  impossible, minimize the number of non-multiples. Never violates the exact-sum invariant.

### 2.3 Difficulty → the "dig" axis

**Main vs background colors (automatic, Approach A):** a color is **main** if its pixel share is
`>= MainColorShareThreshold` (default `0.10` = 10% of total board pixels); the rest are **background**.
(The level's designated **outline color** is excluded from "main" by default — burying the black
silhouette outline is not the intent. Configurable.)

**Bus queue orientation:** in `BusQueueData`, `Buses[0]` is the **HEAD** (tapped first / most
accessible). Higher indices are **buried** (tapped later). "Dig" = place main-color buses at higher
indices so the player must clear/park other buses to reach them.

**Gradient (1→5):**
- **Difficulty 1** — main colors spread evenly / interleaved (round-robin). Always something to play.
- **Difficulty 3** — main colors moderately clustered toward the buried end.
- **Difficulty 5** — *most* main-color buses stacked at the buried end, but a **few of each main
  color left near the head** so the level is solvable from move one and rewards active-slot parking.

**Solvability is the hard ceiling.** We do not bury by a fixed fraction. We bury *as much as the
difficulty asks*, then **relax** (pull main-color buses toward the head / interleave) **until the
existing `BusBuddiesAnalyzer` solver confirms Solvable**. Target burying depth is
`f = (difficulty - 1) / 4` ∈ [0,1] of the main-color buses; the search reduces `f` (or interleaves)
as needed to reach a solvable arrangement, never below the difficulty-1 baseline.

## 3. Architecture (Approach 1 — rework the autofiller + per-level settings)

### 3.1 New: `BusBuddiesDifficultySettings`

A small `[Serializable]` value type holding the six knobs:

```
BusesChunks      : int   (1–5)
DeviationPercent : float (0–1)         // 0.5 = 50%
Columns          : int   (1–5)
Difficulty       : int   (1–5)
NoSingleBusColor : bool
RoundToFive      : bool
```

**Persistence (per level):** written into `LevelDocument.GameData` under stable keys
(`bb.busesChunks`, `bb.deviation`, `bb.columns`, `bb.difficulty`, `bb.noSingleBus`, `bb.roundToFive`).
So **Open a level → settings load → edit → Save** round-trips. A helper reads/writes these
(`BusBuddiesDifficultySettings.ReadFrom(doc)` / `WriteTo(doc)`), defaulting missing keys to the
config's defaults.

Config-level defaults + the fixed mapping constants (`ChunksBase`, `ChunksStep`,
`MainColorShareThreshold`, `ExcludeOutlineFromMain`) live on `BusBuddiesAutofillConfig`
(extend it; keep APS fields for the measured read-out).

### 3.2 Reworked `BusBuddiesAutofiller`

`Complete()` changes from "random-search-until-APS-in-band" to **deterministic build + solvability
relaxation**:

1. Inventory per-color pixel counts (unchanged).
2. Read `BusBuddiesDifficultySettings` (from `req` override if present, else from `doc` GameData,
   else config defaults).
3. Compute `avg`, `[min,max]` from Chunks + Deviation.
4. Partition each color → buses (exact sum), applying **No 1-bus** and **Round to 5** rules.
5. Classify main vs background colors by share.
6. **Arrange** buses across `Columns` with the **dig ordering** for target depth `f(difficulty)`.
7. **Validate** with `BusBuddiesAnalyzer`. If not Solvable, **relax** dig depth (step `f` down /
   interleave more) and re-validate, until Solvable or baseline reached.
8. Attach the **measured APS** from the final analysis as an informational read-out (not a gate).
   Report honest failure only if no solvable arrangement exists at all (unchanged failure surface).

Determinism: seeded RNG only where needed (partition jitter, tie-breaks). Same settings + seed →
same level.

### 3.3 UI — per-level "Difficulty" panel

A new right-panel section for Bus Buddies (sits with **Spool Analysis** in the level editor's right
column — see attached editor screenshot), bound to the open level's settings:

- **Buses Chunks** slider (1–5) — shows resolved *avg px/bus* + *estimated total buses* live.
- **Deviation %** slider.
- **Columns** slider (1–5).
- **Difficulty** slider (1–5).
- **☐ No 1-bus color**, **☐ Round to 5** checkboxes.
- **Apply / Auto-fill** button — runs the reworked autofiller with these settings and repopulates
  the queue; **APS shown as a read-only measured number** afterwards.

On **Open**, the panel populates from the level's GameData. Edits write back on Apply/Save.
(This panel is Bus Buddies-specific; it does not affect YAK/YarnTwist right-panel content.)

### 3.4 Batches / difficulty curve

`TierPreset` (in `BusBuddiesDifficultyCurveConfig`) swaps its difficulty-defining fields to carry a
`BusBuddiesDifficultySettings` block (Buses Chunks / Deviation / Columns / Difficulty / two rules)
**in place of** `AvgCapacity` / `CapacitySlack` / `ColumnRange` / `TargetAps` / `ApsTolerance`.
`GridWidth/Height` and `MaxColors` stay (they drive the board + image color cap). The tier builder
(`BusBuddiesTierProfileBuilder`) writes the tier's settings into the generated level (config +
GameData) so each batch level is built with its tier's difficulty. Batch review still shows the
measured APS as an informational column.

## 4. What stays / what changes

**Reuse unchanged:** `BusBuddiesAnalyzer` + the 1a sim/solver (now a guardrail, not a target),
`BusQueueData`/`BusColumn`/`BusEntry`, the exporter (it serializes the resulting buses — order &
capacity — as before), `Partition` (extended for the two rules), the batch/curve + review stack.

**Changes:** `BusBuddiesAutofiller.Complete` (new fill/arrange logic), `BusBuddiesAutofillConfig`
(new default/mapping fields; APS fields demoted to read-out), new `BusBuddiesDifficultySettings`,
new right-panel Difficulty section, `TierPreset` field swap + `BusBuddiesTierProfileBuilder`.

**Explicitly out of scope (YAGNI):** changing YAK/YarnTwist difficulty; modeling hidden buses in
APS (still a fast-follow); grid-size auto-ramping (separate lever, not part of this model).

## 5. Testing

- **Capacity math:** Chunks→avg mapping; Deviation→[min,max]; per-color partition sums exactly;
  total-bus count ≈ pixels/avg on the Excel example (Purple 500 @ chunks5 → ~17 buses, etc.).
- **Rules:** No-1-bus splits a below-avg color into ≥2; Round-to-5 maximizes multiples of 5 while
  preserving exact sum; both compose.
- **Main/background classification:** share threshold picks Purple/Green(/Black) as main on the
  example; outline color excluded when configured.
- **Dig ordering:** higher Difficulty → main-color buses at higher (buried) indices; a few main
  buses remain near head at Difficulty 5; Difficulty 1 ≈ round-robin.
- **Solvability relaxation:** an over-buried arrangement that the solver reports Unsolvable is
  relaxed to a Solvable one; result is always Solvable when any solvable arrangement exists.
- **Persistence:** settings round-trip through GameData (write → read == identity).
- **No regressions:** full EditMode suite green.

## 6. Open defaults (decided, tunable)

- `ChunksBase=10`, `ChunksStep=5` (Excel mapping).
- `MainColorShareThreshold=0.10`.
- `ExcludeOutlineFromMain=true`.
- Difficulty depth `f=(d-1)/4`.
- Default settings for a fresh level: Chunks 3, Deviation 50%, Columns 3, Difficulty 3, both rules off.
