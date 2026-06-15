# Build Plan: Automated Level-Generation Tooling — Hoppa Level Editor (YAK)

> Paste this whole document into Claude Code, **in the repo**. It assumes the architecture described in `ARCHITECTURE.md` (Hoppa Level Editor: a Unity-Editor-only, game-agnostic UPM framework with Layer 1 = `Packages/com.hoppa.leveleditor.core/`, Layer 2 = `Assets/<Game>/`).
> **Target game = YAK** (`Assets/YAK/`). **Reference implementation = YarnTwist** (`Assets/YarnTwist/`), which already implements the analyzer/autofiller/generator contracts. **Do not start coding** — follow Section 0 first.

---

## 0. How to use this prompt (read first)

1. **Explore before building.** Read and report back on: `GameProfile` and its slots; the `ILevelGenerator`/`LevelGeneratorAsset`, `ILevelAnalyzer`/`LevelAnalyzerAsset`, `ILevelCompleter`/`LevelCompleterAsset` contracts; YarnTwist's implementations of all three (`YarnTwistLevelGenerator`, `YarnTwistLevelAnalyzer`, `YarnTwistSpoolAutofiller`); YAK's current Layer 2 (`YAKWoolCell`, `YAKSpoolSectionPanel`, `YAKColorBalanceRule`, `YAKLevelExporter`, `YAKStaticManagerColorSource`); `LevelDocument`/`GridData<TCell>`/`LevelMetadata`; `IColorPalette`/`ColorEntry`; and the `DemoColorGridGame` sample.
2. **Propose a plan, then wait.** Map the work to the phases in Section 4, state exactly which Layer 1 contracts you'll extend vs. add, flag anything below that conflicts with the real code, and list the Section 8 questions. Get confirmation before implementing.
3. **Extend, don't fork — but mirror structure, not algorithms.** Two of these systems already have generic Layer 1 contracts — extend them. Mirror **YarnTwist's Layer 2 for its *wiring*** (how it implements the contracts, registers assets, plugs into `GameProfile`) when building YAK's. But **YarnTwist's generator / autofill / difficulty *logic* is known-imperfect** — treat it as a pattern reference, not a correctness oracle: build YAK's behavior to the Section 5 spec and improve where YarnTwist falls short. Copy the `DemoColorGridGame` sample patterns for anything new.
4. **Respect the layer boundary and package immutability.** Generic logic → Layer 1 package; YAK specifics → `Assets/YAK/`. Layer 1 (package) changes ship via git tag + `manifest.json` pin bump — never hand-edit package assets in place; propose the version bump.
5. **Generic by default.** Anything not intrinsically YAK-specific goes in Layer 1 behind a contract, so the next game inherits it. Treat `Difficulty`/`APS` as game-defined metrics, not a fixed scale (they leaked into Layer 1 already — see Section 6).
6. **Match the game exactly.** The simulator must reproduce YAK's real mechanics (Section 2). Difficulty numbers are worthless if the sim drifts from the game.

---

## 1. Context & goal

The studio ships a new casual puzzle every few weeks on this shared editor framework. The current target, **YAK**, is a Sand-Loop-style game: a 30×30 grid of colored "wool" cleared by colored "spools" on a looping conveyor. Level authoring is the bottleneck. **Goal: tooling that auto-generates, scores, and batch-produces levels so a designer reviews and tweaks instead of hand-building** — built generically in Layer 1 so future games reuse it. Current phase is a polished prototype for CPI validation (10 levels); prioritize a trustworthy generation + difficulty pipeline over meta features.

---

## 2. The YAK game mechanic (ground truth — the simulator must match this exactly)

**Grid (upper):** 30×30. Every cell is a wool tile of one color (no empty cells in generated levels). Forms a pre-designed image.

**Spools (lower):** in **2–5 vertical column-queues**. Each spool has a color and a fixed wool **capacity** (e.g. 20). Only the **top** spool of each column is tappable; when it leaves, that column shifts up.

**Conveyor:** max **5 active spools** at once. Tapping sends a spool onto the belt; if full, the tap is blocked.

**Movement & consumption:** an active spool moves right, enters a cave/tunnel, reappears from the left — it **loops**. While moving it touches **only the bottom row** of the grid. Wherever it passes a bottom-row tile **matching its color**, that tile unwinds into it (capacity −1) and disappears. Consumption is bottom-row-only, across all 30 columns.

**Gravity:** removing a tile drops the tiles above it in **that column** by one. The 30 columns collapse independently, exposing new bottom-row tiles.

**Spool completion:** a spool reaching exactly full capacity leaves the belt and frees a slot. (A spool with more capacity than remaining matching wool can never complete — hence balance must be exact.)

**Win:** all wool cleared **and** all spools completed.

**Lose (deadlock):** all 5 belt slots occupied **and** none of those spools matches any currently-exposed bottom-row tile.

**Balance invariant:** per color, `total wool == total spool capacity`. (This is what `YAKColorBalanceRule` already checks.)

**Mechanic set (current):** YAK has only **wool + empty** cells — no walls, arrow-boxes, tunnels, or hidden/connected spools. The simulator models exactly this and nothing more. Mechanics may be added if the game expands later; see 5.1 on staying extensible without building them now.

---

## 3. Mapping the systems onto the existing architecture

Each system below names: the Layer 1 contract to **extend or add**, the YAK Layer 2 to **implement**, and the YarnTwist piece to **mirror**.

| System | Layer 1 (generic) | Layer 2 (YAK) | Mirror |
|---|---|---|---|
| **Difficulty scorer / simulator** | Extend `ILevelAnalyzer` / `LevelAnalysisResult` / `AnalysisRequest` (generalize for a real average-player APS estimate) | `YAKLevelAnalyzer : LevelAnalyzerAsset` (simulates §2) | `YarnTwistLevelAnalyzer` |
| **System 2 — auto column/shooter setup** | Reuse `ILevelCompleter` / `LevelCompleterAsset` | `YAKSpoolAutofiller : LevelCompleterAsset` + config SO | `YarnTwistSpoolAutofiller` + `YarnTwistSpoolAutofillConfig` |
| **System 1 — image → grid** | **New** contract `IImageToGrid` + `ImageToGridAsset : ScriptableObject` on a new `GameProfile` slot, + a toolbar panel mirroring `GeneratorModePanel`, reusing the `OnUseLevel` document-load handoff | `YAKImageToGrid : ImageToGridAsset` + config (neutrals, cap) | the generator pattern (greenfield) |
| **System 3 — solution viewer** | Surface the analyzer's win-path as an exportable ordered-action solution | YAK in-game replay tool (game runtime) consuming `solution.json` | `YarnTwistLevelAnalyzer` win-path |
| **System 4 — overnight batch + review** | **New** generic **Batch Review** window (multi-candidate generalization of the generator handoff) + a **batch-run harness** runnable in Unity batch mode | `YAKLevelGenerator : LevelGeneratorAsset` + config | `YarnTwistLevelGenerator` + `YarnTwistGeneratorConfig` |

---

## 4. Build phases

- **Phase A — Difficulty scorer (foundation).** Implement `YAKLevelAnalyzer` with the §2 simulator + average-player policy. Generalize `LevelAnalysisResult`/`AnalysisRequest` in Layer 1 only as needed (carry an APS estimate + calibration knob). Unit-test the simulator against the 10 hand-made YAK levels.
- **Phase B — System 2 auto-setup.** `YAKSpoolAutofiller : LevelCompleterAsset` + config, mirroring YarnTwist; uses the analyzer to confirm solvable + on-target.
- **Phase C — System 1 image→grid.** New Layer 1 contract + panel; `YAKImageToGrid` Layer 2.
- **Phase D — System 3 solution export + in-game viewer.**
- **Phase E — System 4 generator + batch harness + Batch Review window.**

---

## 5. System specifications

> Format per subsystem: **Approach** (how it works) · **Key shapes** (data/signatures to define) · **Edge cases** (what breaks) · **Acceptance** (done when). Names are suggestions — reconcile with the real contracts during Phase 0 exploration. YarnTwist's analyzer/autofiller/generator are a **structural** reference only — their generation and difficulty logic don't work reliably yet, so implement to the spec below rather than inheriting their calculations.

### 5.1 Difficulty scorer / simulator (Phase A)

The foundation everything else leans on. `YAKLevelAnalyzer : LevelAnalyzerAsset`, mirroring `YarnTwistLevelAnalyzer`. Three pieces: a deterministic **simulator**, a **smart solver** on top of it, and an **average-player estimator** that produces APS.

**Approach.**
- *Simulator* — a headless, deterministic model of §2. Keep the core logic engine-agnostic (plain C#, Runtime-side, no `UnityEngine`) so it's fast and unit-testable; the asset just wraps it. Model state as: 30 column-stacks of `ColorId`s (the grid), the bottom-row exposure (each column's current bottom tile), the belt (≤5 active spools = `{ColorId, remainingCapacity}`), and the per-column spool queues. A **move** = send the top spool of a chosen column onto the belt (only if a slot is free). Between moves, advance the belt deterministically: each active spool sweeps and consumes all matching exposed bottom-row tiles of its color (apply gravity per-column after each removal, re-exposing new bottom tiles), until no active spool can make further progress — i.e. resolve to a steady state, completing/removing any spool that hits exactly full and freeing its slot. Detect **win** (grid empty + all spools done) and **deadlock** (5 slots full, none matches any exposed tile) after each resolution.
- *Smart solver* — search over move sequences (DFS/BFS with memoization on a canonical state hash, plus a greedy heuristic to order branches) to find one winning sequence or prove none exists within a node/time budget. Returns a **win-path** (ordered column taps) or `unsolvable`.
- *Average-player estimator* — the APS number. At each free slot, score candidate sends with a **rule of thumb** (prefer a spool whose color is currently exposed; break ties by most tiles cleared this turn), pick the best, but with probability **ε** make a **careless** send (random or non-matching). **Lookahead = 1** (sees board + each column's next spool; no deep planning). Use a seeded RNG; run **N ≈ 300–500** playthroughs; **APS ≈ attempts-before-first-win ≈ 1 / win-rate**. ε is exposed and tunable.
- *Calibration* — a method that takes a set of `(LevelDocument, observedAps)` pairs and fits ε (and optionally lookahead) by simple search to minimize error vs. the simulator's APS. The 10 CPI levels + their real-player data are the calibration set.

**Key shapes.** A pure-C# `YakSimulator` (state struct + `ApplyMove` + `ResolveBelt` + `IsWin`/`IsDeadlock`); a `SolveResult { bool Solvable; IReadOnlyList<int> WinPath; }`; an extension to `LevelAnalysisResult` carrying `{ float ApsEstimate; float WinRate; int Band; IReadOnlyList<int> WinPath; }`; an `ImitationConfig { float Epsilon; int Lookahead; int Runs; int Seed; }`.

**Edge cases.** Distinguish *unsolvable by balance* (caught earlier by `YAKColorBalanceRule`) from *unsolvable by sequence* (no safe order). Levels solvable only by near-perfect play → very high/⊥ APS; cap and flag rather than hang. Honor search node/time budgets and report when a budget is hit instead of asserting unsolvable. RNG must be seeded so runs are reproducible.

**Acceptance.** Reproduces the win/lose outcome of all 10 hand-made levels; deterministic given a seed; APS is monotonic in obvious cases (adding slack lowers it); a full analysis of one level returns well under a second so System 2/4 can call it in a loop.

**Extensibility (without over-building).** YAK is wool-only today, so implement exactly the §2 model — do **not** build hypothetical mechanics now (YAGNI). But read cells through the existing marker interfaces (`IColoredCell`, etc.) rather than a hardcoded wool type, and keep the consumption/exposure rule in one isolated place, so a future cell type or mechanic slots in as a new cell type + a small hook rather than a simulator rewrite.

### 5.2 System 2 — auto column/shooter setup (Phase B)

`YAKSpoolAutofiller : LevelCompleterAsset` + a config SO, mirroring `YarnTwistSpoolAutofiller`. Runs after a grid exists; reads its real per-color counts and vertical layering; emits the spool/top-section config. The designer never hand-balances.

**Approach.**
1. Tally per-color wool totals from the grid.
2. **Partition** each color's total into spools with capacity in `[min,max]` (averaging ≈ `avg`), summing **exactly** to the color total (balance invariant).
3. **Assign** the resulting spools into 2–5 column-queues and **order** them so the sequence is feasible against bottom-up exposure (a color's spools shouldn't all sit behind colors that only get exposed late).
4. **Verify** with `YAKLevelAnalyzer`: solvable + APS near `apsTarget`. If off, **perturb** (re-partition, re-order, adjust slack) and retry within a budget; keep the best-fitting candidate.
- *Difficulty tuning* — fewer safe orderings = harder. Knobs nudge tightness: interleaving colors across columns and minimizing slack raises APS; front-loading easily-exposed colors and adding redundant safe sends lowers it.

**Key shapes.** `YAKSpoolAutofillConfig { int MinCapacity; int MaxCapacity; int AvgCapacity; Vector2Int ColumnRange; float ApsTarget; float ApsTolerance; int MaxAttempts; }`; output written into `LevelDocument.TopSection` as YAK's spool payload (same shape `YAKSpoolSectionPanel` reads).

**Edge cases.** A color total not cleanly divisible by capacities → allow a final undersized spool, or merge, per a documented rule. **Too many distinct colors for the column/slot budget** → fail fast with a clear diagnostic (this is the partner to System 1's color cap; surface the conflict, don't silently produce something unsolvable). Target APS unreachable for this grid → return the closest achievable with a warning rather than looping forever.

**Acceptance.** Every emitted level passes `YAKColorBalanceRule` and is analyzer-solvable; APS lands within `ApsTolerance` of target or returns an explicit "closest, target unreachable" result; capacities all within `[min,max]` (with the documented exception).

### 5.3 System 1 — image → grid converter (Phase C)

New Layer 1 contract `IImageToGrid` + `ImageToGridAsset` on a new `GameProfile` slot, with a toolbar panel mirroring `GeneratorModePanel` and reusing the `OnUseLevel` handoff. YAK implements `YAKImageToGrid`.

**Approach (ordered pipeline).**
1. **Load** the source image; **downscale** to 30×30 using area/box averaging (not point sampling) so each cell is the average of its source region.
2. **Quantize** each cell to the nearest `ColorEntry.Color` via `IColorPalette`/`TryGetColor`, in a perceptual space (CIELAB ΔE if practical, else luminance-weighted RGB). Emit cells whose `IColoredCell.ColorId` = the matched id. Clean blocks, **no dithering**. **Never touch `YAKColorType`** — enum/int conversion stays in `YAKLevelExporter`/`YAKStaticManagerColorSource`.
3. **Segment** subject vs. background (default heuristic: use alpha if present, else the dominant color of the outer border ring / largest edge-connected region; make the heuristic overridable in config).
4. **Background fill** — choose the **neutral** of maximum luminance distance from the subject's dominant color, *excluding the subject's own id*, from `{Grey, GreyLight, GreyDark, DarkGrey, White, Black}`. Fill the background region with it. **No empty cells**: override the framework's `CellTypes[0]` (empty) convention so background is a real colored cell.
5. **Color-cap merge** — while `distinctColors > cap`, merge the least-used color into its nearest perceptual palette neighbor and re-label those cells; repeat until ≤ cap. The background neutral counts toward the cap.
6. **Emit** a `LevelDocument` (30×30 from `GameProfile.GridWidth/Height`), handed to the editor via the normal load path.

**Key shapes.** `IImageToGrid.Convert(Texture2D source, ImageToGridConfig) → LevelDocument`; `YAKImageToGridConfig { int ColorCap = 6; ColorId[] BackgroundNeutrals; SegmentationMode Segmentation; }`.

**Edge cases.** Subject's dominant color *is* a neutral (egg = white) → exclusion rule must still leave a valid contrasting neutral. Image already under the cap → skip merging. Gradient-heavy images → blocky merges are expected and fine (recognizability > fidelity). Ambiguous segmentation (subject touches all borders) → fall back to "most-saturated region = subject" and log it.

**Acceptance.** Output has **distinct colors ≤ cap**, **zero empty cells**, subject visually distinct from background, deterministic for a given image+config, and loads cleanly in the editor as a normal `LevelDocument`.

### 5.4 System 3 — solution viewer (Phase D)

Reuses the analyzer's win-path; adds a small Layer 1 surface + a YAK-runtime replay tool.

**Approach.**
- *Layer 1* — expose the analyzer's win-path as a serializable solution (a method/exporter that writes `solution.json`). Prefer the **smart-solver's canonical** winning order so it's stable.
- *YAK runtime* — a component that loads `solution.json` and, per step, highlights the column to tap; supports manual-step and auto-advance. Internal QA only.

**Key shapes.** `solution.json` = `{ "levelId": "...", "taps": [ { "step": 0, "column": 2 }, ... ] }`. Optionally include a per-step board-state hash so the in-game tool can assert it's still in sync after edits.

**Edge cases.** Multiple valid solutions → pick one canonically (smallest column index on ties) for reproducibility. Level edited after export → solution stale; recompute, or detect via the state hash and warn.

**Acceptance.** Replaying the exported solution — in the simulator and in-game — deterministically wins the level.

### 5.5 System 4 — overnight batch + review (Phase E)

`YAKLevelGenerator : LevelGeneratorAsset` + config (mirroring YarnTwist) drives `LevelGeneratorRequest` → `LevelGeneratorResult`. A **batch-run harness** loops it overnight in Unity batch mode; a generic **Batch Review** window curates the results.

**Approach — harness (`-batchmode -nographics`, inside Unity, against the active `GameProfile`).** Per candidate: image-AI generates a source image from the batch seed (HTTP) → `YAKImageToGrid` → grid → `YAKSpoolAutofiller` configures spools → `YAKLevelAnalyzer` scores → **filter**. Over-produce, keep survivors; write each as a `LevelDocument` JSON + a thumbnail render + a stats record into a dated staging folder. **Not** a standalone .NET app — it must reach the Editor-side contracts.

**Approach — filter (a candidate is kept only if all hold).** Analyzer-solvable; APS within the band for this level's slot on the difficulty curve; **not a near-duplicate** of an existing keeper (compare a perceptual hash of the grid and/or its color-distribution signature against accepted levels); recognizable (subject vs. background contrast above a threshold).

**Approach — Batch Review window (new generic Layer 1 Editor window).** Scans the staging folder; shows candidates as a grid of thumbnail + stats (render each `LevelDocument` to a `Texture2D` for the thumbnail); supports **multi-select**; **"Import Selected"** copies chosen `LevelDocument`s into the project's levels folder via the normal load path; reject/delete optional. The designer then opens, edits, and exports through `YAKLevelExporter` as today.

**Approach — library flywheel.** A curated folder of known-good `LevelDocument`s the harness draws from and biases AI generation toward; every level the designer imports/approves folds back in. Bootstrap curation is a one-time pass (out of scope) — just lay out the folder so it can grow.

**Key shapes.** Batch spec config: `{ int LevelCount; DifficultyCurve curve; string ThemeSeed; int ColorBudget; Vector2Int ColumnRange; Vector2Int CapacityRange; float OverGenerateRatio; int Seed; }` where `DifficultyCurve` maps level index → target APS band (e.g. ramp 2→8). Stats record: `{ string Id; float Aps; int Band; int DistinctColors; bool Solvable; string ThumbnailPath; }`. A static `[MenuItem]` / `-executeMethod` entry point for the headless run.

**Edge cases.** Image-AI call fails/times out → retry with backoff, then skip and log; a partial batch must still produce a browsable folder. Over-generation never terminating (target band too narrow for the seed) → cap attempts per keeper and report a shortfall. Determinism: a fixed `Seed` reproduces the same batch.

**Acceptance.** One overnight run yields ~20–40 `LevelDocument`s that are all solvable, in their target difficulty bands, de-duplicated, and browsable in the Review window; multi-select Import lands them in the levels folder ready to open and tweak.

### 5.6 Known failure modes to avoid (distilled from YarnTwist's current implementation)

YarnTwist's three systems run but don't work reliably. Do **not** repeat these:

1. **Don't hardcode the economy.** YarnTwist bakes `Columns = 4`, `BallsPerSpool = 3`, `BallsPerItem = 9` as constants duplicated across all three files. YAK is **2–5 columns** with a different capacity economy (~20, not 3). Drive columns, capacity, and wool counts from the profile/config — never constants.
2. **Gate generation on the simulator, not on rules.** YarnTwist's generator never simulates — its accept gate runs only static rules (balance, palette, etc.), so it ships **unsolvable, jam-guaranteed, or buried** levels with `Succeeded = true`; generator and analyzer are completely disconnected. YAK's generation/auto-fill loop must be **generate → `YAKLevelAnalyzer` (solvable + in-band) → reroll**, not "validate against balance only."
3. **Measure APS; don't label it.** YarnTwist maps APS→win-path-count through a hand-authored curve (`WinPathTargetByAPS`), and `TargetAPS` is written to metadata but **never enforced in generation**. For YAK, compute APS as a **measured** quantity (≈ 1 / win-rate from the average-player rollouts) and make it an **enforced gate**, not a recorded field.
4. **Don't trust capped exact counts.** YarnTwist's exact win-path DFS caps on any non-trivial grid and counts *orderings* (scales with item count, not just difficulty). Treat capped counts as **"unknown"**; don't tune narrow bands on them.
5. **One myopic rollout ≠ real difficulty.** YarnTwist's win-rate is a single lookahead-4 player; change the lookahead and measured difficulty shifts. Expose ε/lookahead as config and **validate the policy against real YAK play** before trusting the numbers (the calibration step in 5.1).
6. **Calibrate the belt/timing model against real game logs.** YarnTwist's model assumes instant head-advance (settles to fixpoint between taps), but the real game advances only as physical balls arrive — a **pacing gap that makes solvability/win-rate optimistic**, and it's verified on exactly one level. YAK's v1 turn-based simulator (5.1) carries the **same optimism risk** — budget a calibration pass against real YAK logs and **document the capacity constants and their provenance**.
7. **Never show a confident wrong answer.** YarnTwist swallows analyzer faults to the console, treats timeouts as skipped/null, and reports `Solvable = false` on a *timeout* (false-unsolvable). YAK must propagate **explicit states** — "timed out," "analysis faulted," "unknown" — distinct from a real "unsolvable."
8. **Decide the auto-filler's degrees of freedom.** YarnTwist's filler only permutes a fixed spool multiset, so a target band can be **structurally unreachable** and it returns silent best-effort. For YAK, either let the filler adjust spool counts/structure, or report **"band unreachable for this grid"** as a distinct, honest outcome (ties to the System 1 color cap).

---

## 6. Cross-cutting rules

- **Outputs are `LevelDocument`.** Every new pipeline emits the editor's working document; the existing exporter chain does game-JSON translation. Never write game JSON directly.
- **Colors are string-keyed in Layer 1.** Quantize/operate on `ColorId`/`ColorEntry`; leave enum/int to YAK's exporter and color source. Don't bake a color enum into Layer 1.
- **Difficulty/APS are game-defined.** They already leaked into Layer 1 (`LevelMetadata.Aps`, `LevelGeneratorRequest.TargetAPS`/`Difficulty`); reuse them, but don't assume the 1–10 scale is universal.
- **Package versioning.** Layer 1 changes (new `IImageToGrid` contract, Batch Review window, any `LevelAnalysisResult` generalization) ship via git tag + `manifest.json` pin bump — propose the version, don't hand-edit installed package assets.
- **Single active profile.** Operate against the active `GameProfile`; don't assume multi-profile context.
- **Engine-agnostic where it counts.** The simulator/scorer logic should be plain C# (Runtime-side) for speed + headless unit tests, even though it's invoked through Editor-side assets.
- **`TopSection` is an opaque game blob.** Don't assume "top section" semantics generically; YAK's spool data lives in its own top-section payload.

---

## 7. Out of scope

Boosters, lives, monetization, meta, live-ops, advanced progression. The image-generation API choice/credentials and the one-time library bootstrap curation are handled separately. Non-grid level shapes (the framework is grid-only by design).

---

## 8. Confirm before building

- **Target/reference confirmation:** target = YAK, reference = YarnTwist — correct?
- Whether `LevelAnalysisResult`/`AnalysisRequest` should be generalized in Layer 1 for the APS estimator, or whether a YAK-specific result is cleaner — propose based on the real contract.
- Which **image-generation API/service** the harness calls (endpoint + auth).
- The cleanest **overnight invocation** given the asmdef boundaries (Unity batch mode entry point vs. editor-driven), and where the image-AI HTTP call should live.
- Anything in §2–5 that contradicts how YAK/YarnTwist or the framework already behave — especially the existing `LevelGeneratorRequest` fields and the `ILevelCompleter.MechanicToggles` convention.
