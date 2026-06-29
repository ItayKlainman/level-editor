# Bus Buddies — Design Spec

**Date:** 2026-06-29
**Status:** Approved (design) — pending implementation plan
**Initiative:** Bus Buddies (see `docs/BUS-BUDDIES-BACKLOG.md`)
**Authoritative mechanic source:** the Bus Buddies GDD (lead-provided), summarized below.

---

## 1. Overview

**Bus Buddies** is a new game authored through this level editor — a re-theme of the live game
**Food Hunt**, intended ~100% mechanically identical (buses + passengers theme). It is a **sibling
of YAK**: same Layer-1 framework, a new set of Layer-2 assets wired into a new `GameProfile.asset`.

In this framework a "game" is **not a subclass** — `GameProfile` is `sealed`. A game = a configured
`BusBuddiesProfile.asset` plus Layer-2 asset implementations (cells, simulator, analyzer, autofiller,
generator, image→grid, queue panel, exporter, palette, validation rule) referenced by its slots,
mirroring the structure of `Assets/YAK/`.

**Goal:** mass-produce **30 polished, solvable, difficulty-graded levels** for a **D1 40%+** retention
validation campaign. Therefore the editor must drive the full **generate → autofill → analyze →
export** pipeline, exactly as it does for YAK.

**Companion game:** a Bus Buddies game is being built **now**, heavily based on the YAK codebase.
Consequence for us (see §8): keep the exporter **as close to YAK's as possible** so the YAK-based
game reads our levels with minimal change, behind a thin seam we can adapt when the game firms up.

---

## 2. Core mechanic (authoritative)

The new rules, contrasted with YAK. **This is the contract the simulator and the future game both
implement.**

| Piece | YAK | **Bus Buddies** |
|---|---|---|
| Gravity | blocks fall after a hit | **none — a removed block just becomes empty; nothing else moves.** The GDD's "grid collapses" = progressive removal, NOT physics. |
| Accessibility | bottom row + gravity-exposed | a Pixel Block is **accessible** iff a passenger can reach it from the grid border **through empty cells, moving 4-way (orthogonal only)**. Formally: the region **outside the grid counts as open**, and empty cells are flood-filled 4-connected from the border; a block is accessible iff it sits on the **literal grid edge** (touching the outside) OR at least one of its 4 orthogonal neighbours is a border-connected flooded empty cell. **Diagonal pinches do NOT pass** (a block orthogonally enclosed by 4 blocks is locked until a neighbour clears). This "outside == open" rule is what makes edge blocks always reachable (passengers attack from each frame edge), and the canonical rule the future game must implement. |
| Buses ≡ spools | wool spools | colored **Buses** with a fixed passenger count; passengers share the bus colour. |
| Queue | spool columns | **1–5 vertical bus columns; only the top bus in each column is tappable.** Tapping moves it into the Active Bus Row; the column shifts up. |
| Active region | conveyor belt (moving) | **Active Bus Row — stationary, max 5 buses.** Active buses auto-release passengers. |
| Release | — | while an active bus has an accessible matching block: one passenger exits, removes that block, carries it to the **central Hole**; bus count −1. Continues until the bus is empty or no accessible match remains. **Target rule (canonical, editor + game must match):** when several matching blocks are reachable, the passenger takes the one **nearest to the central Hole** (hole at bottom-center, just below the grid; squared-Euclidean distance; tie-break by lowest index). Because there is no gravity, target choice changes which neighbours unlock, so this must be deterministic and identical in both — it also makes the solver exact/sound (only bus-pull order branches). |
| Bus exit | — | a bus at **0 passengers** leaves the Active Row, freeing a slot. |
| Overflow / holding | belt capacity + jam | **none.** No belt. |
| **Win** | — | all blocks collected **and** all buses emptied (queues exhausted + active row empty). |
| **Lose** | belt jam | **deadlock — the Active Row is full (5 buses) and none of them can release** (no active colour has an accessible matching block). |

**Grid sizes:** 20×20, 30×30, 40×40 (square). Each cell = one colored Pixel Block or empty.

---

## 3. Scope & phasing

**Phase-1 = the full generation pipeline** (lead decision). To de-risk an ambitious bite, it is split
into three internal sub-phases, each run through the /daily build → review → verify loop:

| Sub-phase | Delivers | Risk |
|---|---|---|
| **1a — Engine** | Cells + the simulator stack (`BusLevelModel / BusSimState / BusSolver / BusAveragePlayer`) + headless EditMode unit tests. Pure C#, no UnityEngine. | The genuinely-new core. Proven in isolation first. |
| **1b — Author & analyze** | `BusBuddiesAnalyzer` (+config), `BusBuddiesAutofiller` (+config), `BusBuddiesProfile.asset`, palette, queue panel, balance rule. Hand-author a level → get a difficulty read. | Medium — wiring + analyzer integration. |
| **1c — Generate & export** | `BusBuddiesImageToGrid` (reuses Task-4 helpers), exporter (+colour source), generator (+config), batch/curve harness. Full pipeline → sample levels. | Medium — mostly retargeting proven YAK code. |

**In scope (phase-1):** core colour gameplay end-to-end; all three mechanics **authorable + exportable**
(data/editor/export only — see §7); image-driven and procedural generation; batch generation.

**Out of scope (phase-1, deferred):** accurate difficulty **modeling** of the three mechanics
(imperfect-info for hidden, coupled solve for connected); APS calibration against real player data;
a YAK-style "complexity / click-pattern" axis (YAK-specific); boosters, lives, monetization, meta,
live-ops (per GDD).

---

## 4. Architecture — extension points (mirrors YAK)

New folder `Assets/BusBuddies/` mirroring `Assets/YAK/`. Layer-1 (`Packages/com.hoppa.leveleditor.core`)
is reused unchanged — no Layer-1 churn expected. Map of what to create and its YAK analog:

| New Bus Buddies asset/class | Layer-1 type it satisfies | YAK analog |
|---|---|---|
| `BBPixelCell` / `BBEmptyCell` (+ definitions) | `IColoredCell` / `ICellData` (+ `CellTypeDefinition`) | `YAKWoolCell` / `YAKEmptyCell` |
| `BusLevelModel / BusSimState / BusSolver / BusAveragePlayer` | (pure C#, no Layer-1 type) | `Yak*` sim stack |
| `BusBuddiesAnalyzer` + `BusBuddiesAnalyzerConfig` | `LevelAnalyzerAsset : ILevelAnalyzer` | `YAKLevelAnalyzer` |
| `BusBuddiesAutofiller` + `BusBuddiesAutofillConfig` | `LevelCompleterAsset : ILevelCompleter` | `YAKSpoolAutofiller` |
| `BusBuddiesLevelGenerator` + `BusBuddiesGeneratorConfig` | `LevelGeneratorAsset : ILevelGenerator` | `YAKLevelGenerator` |
| `BusBuddiesImageToGrid` | `ImageToGridAsset : IImageToGrid` | `YAKImageToGrid` |
| `BusBuddiesQueuePanel` | `TopSectionPanel` | `YAKSpoolSectionPanel` |
| `BusBuddiesExporter` + `BusBuddiesColorSource` | `LevelExporterAsset : ILevelExporter` | `YAKLevelExporter` + `YAKStaticManagerColorSource` |
| `BBColorBalanceRule` | `ValidationRuleBase` | `YAKColorBalanceRule` |
| `BusBuddiesPalette.asset` | `ColorPaletteAsset` | `YAKPalette.asset` |
| `BusBuddiesProfile.asset` | `GameProfile` (asset, not subclass) | `YAKProfile.asset` |
| batch/curve harness | (static editor harness) | `YAKBatchHarness` / `YAKCurveBatchHarness` |

**Reused unchanged from Layer-1:** `GameProfile`, `GridData<ICellData>`, `LevelDocument`, all
`I*`/`*Asset` interfaces & result types (`LevelAnalysisResult`, `AnalysisStatus`,
`LevelCompletionResult`, `LevelGeneratorRequest/Result`), `JsonLevelSerializer`, `LevelEditorWindow`
+ shared panels (Summary/Autofill/Validation/Grid), `CellTypeRegistry`,
`BatchReviewWindow`/`BatchStaging`/`LevelThumbnail`, and the Task-4 image→grid static helpers.

---

## 5. The simulator (sub-phase 1a) — the core

Pure C# under `Assets/BusBuddies/Runtime/Sim/`, headless-testable. Unlike YAK, **gravity is absent**,
so YAK's "collapse each column to a fixed bottom→top sequence" optimization does **not** apply — the
board is modeled as a full mutable 2D occupancy.

### `BusLevelModel` — immutable interned snapshot
- **Grid:** `int[W*H]` colour indices, `-1` = empty (row-major; pick one origin convention and document
  it — match YAK's `y*W+x`, bottom-up, for exporter parity).
- **Queue:** per column, an ordered list (head→back) of bus entries `{ colorIdx, capacity, hidden,
  connectedId }`.
- **Scalars:** `W, H, ActiveSlots` (default 5), `Columns`, `TotalBlocks`, `TotalPassengers`,
  `NumColors`, `ColorNames`, connected-bus pairing table.
- **Build:** `static Build(GridData<ICellData> grid, BusQueueData queue, int activeSlots)` reading cells
  via `IColoredCell.ColorId`; plus a `FromArrays(...)` test factory.

### `BusSimState` — mutable per-playthrough state
- **State:** `Removed` occupancy (e.g. a `bool[]`/bitset over grid cells) + the live colour lookup,
  `QHead[]` per column, Active Row = up to `ActiveSlots` slots each `{ colorIdx, remaining }`
  (−1/0 = empty), `BlocksLeft`, an **accessibility cache** (the border-connected empty flood).
- **Accessibility:** `RecomputeAccess()` floods empty cells 4-connected from the border (outside the
  grid counts as open); a block is accessible iff it is on the grid edge or an orthogonal neighbour is
  flooded. **Sub-phase 1a uses a full reflood per removal (correctness-first);** an incremental flood
  (re-expanding only from the freed cell) is a deferred performance optimization, not required for 1a.
- **`ApplyMove(int col)`:** pull the top bus of `col` into the first free Active slot (requires a free
  slot and `QHead[col]` in range), advance `QHead[col]`, then `ResolveReleases()`.
- **`ResolveReleases()`:** loop to quiescence — while some active bus has ≥1 accessible block of its
  colour, remove one such block, decrement that bus, free the slot if it hits 0, update accessibility;
  repeat until no active bus can release. **Which block is removed is the canonical target rule:
  `FindAccessibleBlock` picks the accessible matching block NEAREST the Hole** (bottom-center,
  `hx=(W-1)/2`, `hy=-1`; squared-Euclidean; tie-break lowest index). This is deterministic and must
  match the game; with a single deterministic targeting, only bus-pull order branches, so the solver
  is sound (no false `Unsolvable` from target-choice ambiguity).
- **`IsWin()`** = `BlocksLeft == 0 && all QHead exhausted && Active Row empty`.
- **`IsDeadlock()`** = Active Row full (`ActiveSlots` occupied) **and** no active colour has an
  accessible matching block.
- **`HasLegalMove()`** = a free Active slot **and** some column with a pullable bus.
- **`Key()`** — canonical memo key for the small-grid solver: `(Removed bitset, QHeads, sorted active
  multiset of {color,remaining})`. All components are monotonic (blocks only removed, heads only
  advance, buses only shrink) → the state space is a DAG.

### `BusSolver` — budgeted exact win-path finder (small grids)
- `enum Outcome { Solvable, Unsolvable, BudgetExceeded }`; `Result { Outcome, int[] WinPath, Nodes,
  ElapsedMs }`. DFS over move orders (which column to pull next) with a `_dead` visited-set keyed by
  `Key()`. **Honesty rule:** a budget cut returns `BudgetExceeded`, never `Unsolvable`. Constructor
  `(maxNodes, timeoutMs)`. Used only when `W*H ≤ threshold` (config) to produce a real solution path.

### `BusAveragePlayer` — Monte-Carlo difficulty estimator (all sizes)
- N seeded playouts of a myopic greedy bot (lookahead L, carelessness ε): mostly pull the bus that
  releases the most blocks while avoiding self-deadlock; with prob ε pull a random legal column.
- `Estimate(model, Config{Epsilon, Lookahead, Runs, Seed}) → Result{ WinRate, Runs, Aps = 1/WinRate
  (capped), ... }`. **APS = Attempts-Per-Solve ≈ 1/win-rate.** Deadlock-proneness lowers win-rate
  naturally, so it folds into APS. Hidden/connected modeled best-effort in 1a (treated as known /
  independent); imperfect-info deferred.

---

## 6. Analyzer & difficulty (sub-phase 1b)

`BusBuddiesAnalyzer : LevelAnalyzerAsset` + `BusBuddiesAnalyzerConfig` (a `ScriptableObject` referenced
by the analyzer, like YAK). `Analyze(LevelDocument, GameProfile, AnalysisRequest) → LevelAnalysisResult`:

1. Resolve `ActiveSlots` (`doc.GameData["conveyorCount"]` — YAK's key, repurposed; → else config default 5).
2. `BusLevelModel.Build`.
3. **Balance precheck** — per-colour block count must equal per-colour total bus capacity; else proven
   `Unsolvable` (sound, cheap).
4. If `W*H ≤ smallGridThreshold`: `BusSolver.Solve` → set `Status` + `WinPath`.
5. Run `BusAveragePlayer.Estimate` → `WinRate`, `ApsEstimate`; **rollout-rescue** upgrades a TimedOut/
   BudgetExceeded solver result to `Solvable` if any playout won.
6. `Band` from `cfg.BandFor(aps)` (APS thresholds). Fill `LevelAnalysisResult` (Status, Solvable, APS,
   Band, WinPath, etc.).

Difficulty levers (all fold into APS / band): grid size (dominant — more blocks = more taps), colour
count, queue ordering / column count, and deadlock-proneness.

---

## 7. Autofiller & the three mechanics (sub-phase 1b)

`BusBuddiesAutofiller : LevelCompleterAsset` + `BusBuddiesAutofillConfig`, same shape as
`YAKSpoolAutofiller`:

- Inventory per-colour blocks from the grid via `IColoredCell`.
- Per attempt: choose column count in `ColumnRange` (1–5); `Partition` each colour's block total into
  bus capacities in `[Min,Max]` jittered around `Avg` **summing exactly** (solvable by construction);
  assign buses across the queue columns.
- Gate each candidate on `profile.LevelAnalyzer.Analyze`: keep `Solvable`; accept first within
  `ApsTolerance`; else closest-solvable best-effort or honest "no solvable arrangement".

**Three mechanics — authorable + exportable, modeling deferred (per lead):**
- **Hidden Bus** — a queued bus whose colour is unknown until it reaches the Active Row. Stored as a
  `hidden` flag on the bus entry (≡ YAK hidden spool). Analyzer treats colour as known in 1a.
- **Hidden cube** — a grid block whose colour is concealed until revealed. Stored on the cell/queue
  data; treated as its real colour by the analyzer in 1a.
- **Connected Bus** — two buses linked so emptying interacts. Stored as a `connectedId` pairing; the
  autofiller may emit simple pairs; analyzer treats them as independent in 1a.

Accurate modeling (imperfect-info Monte-Carlo for hidden, coupled solve for connected) is a deferred
calibration phase, exactly as YAK shipped hidden spools as data before modeling them.

---

## 8. Data model & export

### Cells & queue data
- `BBPixelCell : IColoredCell` (`CellTypeId = "bb.pixel"`, `string ColorId`) — the colored block.
- `BBEmptyCell : ICellData` (`CellTypeId = "bb.empty"`) — "no block" everywhere; emitted by the
  subject-only image→grid path.
- `BusQueueData` (the `LevelDocument.TopSection` payload): `Columns` → ordered `Buses`
  `{ ColorId, Capacity, Hidden, ConnectedId }`. Active-row capacity is carried in GameData under
  YAK's existing **`conveyorCount`** key (repurposed; default 5), surfaced to designers as
  "Active Bus Slots" — reusing the key keeps the cloned YAK analyzer/autofiller/generator/exporter
  code working unchanged.

### Export — stay YAK-shaped (revised per lead)
The companion game is being built on the YAK codebase **now**, so the exporter is a **near-clone of
`YAKLevelExporter`** rather than a fresh schema:

- Same master-config writer pattern: upsert a single shared `level_config.json`, int key parsed from
  the filename (preserving Apply-Order keys).
- Same top-level shape: `{ "LevelConfigs": { "<intKey>": { ... } } }`.
- **Buses serialize exactly like YAK spools** — reuse YAK's `SpoolColumnConfigs` structure
  (`{ ColorType:int, Capacity, IsHidden }`); connected pairing carried as an additive field only where
  YAK has no equivalent.
- **Active-row slot count reuses YAK's `ConveyorCount`** field on export (and `conveyorCount` as the
  GameData key, per §8 above), Bus Buddies meaning = Active Row capacity.
- `PixelColors` stays the flat `int[W*H]`, row-major bottom-up, `0` = empty, mapped via
  `BusBuddiesColorSource.GetInt(colorId)`.

Keep the exporter behind a **thin seam** (a single mapping method) so fields can be renamed/added when
the game project finalizes. Document the format for the game developer. Avoid absolute paths in the
output-path field (project-relative, per project rule).

---

## 9. Profile wiring, image→grid, generation (sub-phase 1c)

- **`BusBuddiesProfile.asset`** wires every slot from §4; `schemaId = "busbuddies"`; default grid
  30×30; **`_spoolsBelowGrid = true`** (grid on top, bus queue below — reuses the CWS layout flip and
  matches the reference art).
- **`BusBuddiesImageToGrid`** reuses the Task-4 `ComputeOutlineMask` + empty-emit helpers; **defaults to
  `BackgroundFill.Empty` + `OutlineSubject = true`** (subject-only with a black outline) — load-bearing,
  because the flood-accessibility model needs empty background to reach concave parts of the silhouette.
  Emits `BBPixelCell` (+ `BBEmptyCell` for background).
- **`BusBuddiesLevelGenerator`** chains image source → `ImageToGrid.Convert` (procedural fallback) →
  `Autofiller.Complete` → `Analyzer.Analyze` gate (Solvable + APS in tolerance), like `YAKLevelGenerator`.
- **Batch / curve harness** — clone YAK's static harnesses, parameterized by the Bus Buddies profile
  path, to mass-generate + dedup + stage numbered `level_N.json` (+ png + stats) for `BatchReviewWindow`.
  Reuse the difficulty-curve tier mechanism if useful for grading the 30 levels.

---

## 10. Testing strategy

**Headless EditMode unit tests (sub-phase 1a, the priority):**
- Accessibility flood correctness, including the **locked-diagonal** case (a block orthogonally enclosed
  by 4 blocks is NOT accessible even with diagonal empties) and interior-pocket / donut shapes.
- No-gravity removal (removing a block leaves all others in place).
- `ResolveReleases` to quiescence (multi-bus, same-colour competition, partial-empty bus left in row).
- Win detection and **deadlock** detection (5 full + none release).
- Balance precheck rejects mismatched colour totals.
- `BusSolver` win-path on small hand-built levels; replay the path → `IsWin`.
- `BusAveragePlayer` determinism with a fixed seed.

**Integration smoke (1b/1c):** image → grid (subject-only + outline) → autofill → analyze → export
round-trip on a real profile; assert Solvable + a sane APS + a well-formed `level_config.json`.

---

## 11. Open questions / future work
- **APS calibration** — APS is measured but uncalibrated until real Bus Buddies player data exists
  (shared with the YAK Gap-B calibration problem).
- **Mechanic modeling** — imperfect-info Monte-Carlo for hidden buses/cubes; coupled solve for connected
  buses. Deferred.
- **Export schema sync** — revisit field names/shape once the companion game project finalizes; the thin
  seam exists for this.
- **Solver scaling** — confirm the `smallGridThreshold` empirically; the Monte-Carlo path carries 30×30
  and 40×40.

---

## 12. Success criteria
- Sub-phase 1a: the simulator + tests are green and model the §2 rules exactly (headless).
- Sub-phase 1b: a hand-authored Bus Buddies level analyzes to a believable Solvable + APS + Band in-editor.
- Sub-phase 1c: the batch harness produces solvable, difficulty-graded sample levels and a
  YAK-shaped `level_config.json`; the pipeline is ready to generate the 30-level set.
