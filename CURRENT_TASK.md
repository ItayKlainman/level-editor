# Current Task

> Live state of in-flight work. Update after every meaningful chunk so a fresh
> session can resume cold. Companion to `PLANNING.md` (spec) and
> `SESSION_NOTES.md` (longer-lived context).

---

## ⏸ PAUSED MID-BUILD (2026-07-19) — Audio Balance panel (new package `com.hoppa.audiobalance`)

**Resume here first tomorrow.** Branch `feat/audio-balance`, **16 commits, NOTHING PUSHED**.
Tasks **1–8 of 13** complete and reviewed. **512/512 EditMode green** (60 in the new package).

- **Spec:** `docs/superpowers/specs/2026-07-19-audio-balance-panel-design.md`
- **Plan:** `docs/superpowers/plans/2026-07-19-audio-balance-panel.md` (13 TDD tasks; its
  **"Plan self-review" section at the bottom records all 5 deviations** and why)
- **Ledger (GITIGNORED — do not rely on it surviving `git clean -fdx`):** `.superpowers/sdd/progress.md`
- Executed with `superpowers:subagent-driven-development`: fresh implementer per task, opus/sonnet
  reviewer per task, fix-round + re-review when a review finds Critical/Important.

### What it does
Pick one clip as the **anchor** (normally level BG music). Every other clip is measured for
*perceived* loudness (LUFS, ITU-R BS.1770-4 — not the Unity volume field) and assigned a gain
placing it at a deliberate offset from the anchor, via category offsets (Music 0 / SFX +3 / UI −6)
plus a per-clip trim. Gains bake into a runtime `AudioGainTable` asset. **Source .wav files are
never modified.** v1 wires up **no game** — BB adoption is a separate, deliberate step.

### Built (Tasks 1–8)
`AudioGainMath`/`AudioGainTable`/`AudioGainTableExtensions` (Runtime) · `KWeighting` (coefficients
match the published 48 kHz table to 1e-9) · `LufsMeter` integrated loudness + two-stage gating
(**calibration test reads −22.9933 LUFS**) · `MomentaryMax` · `PeakMeter.SamplePeakDb` ·
`AudioBalanceProfile`/`AudioCategory`/`ClipSettings` · `GainSolver` + headroom pass · `ClipSampleReader`.

### Remaining (Tasks 9–13)
`LoudnessCache` → `LoudnessAnalyzer` → window shell → clip table → preview player + write-table + docs.
Tasks 11–13 are IMGUI and should be expected to need in-editor iteration.

### ✅ BOTH TASK-8 FOLLOW-UPS CLOSED (2026-07-19, commit `fd4e7ff`)
- **(A) APPLIED.** `LoadAudioData()` is **async** — a `true` return means *queued*, not loaded, and
  the code was calling `GetData` immediately. Now re-checks `clip.loadState` afterwards and fails with
  `ClipSampleReader.LoadPendingError` if it is not `Loaded`. `GetData` is provably unreachable unless
  `loadState == Loaded`. Single re-check, no polling — editor-time code, so a not-ready clip is a
  reportable condition, not something to block on.
- **(B) LEAD DECIDED: accept the gap, document it.** The Streaming rejection branch has no automated
  coverage and will not get any: `AudioClip.Create` (the only way these tests build clips) always
  yields a fully-resident non-streaming clip, and `Streaming` can only be set on an imported asset via
  its `AudioImporter`. Covering it would need a committed `.wav` fixture with `.meta` pinned to
  Streaming — deliberately rejected to keep binaries out of the package. Reasoning is recorded in the
  XML doc on `ClipSampleReader.StreamingError` so no future maintainer assumes coverage exists.

**→ Task 9 (`LoudnessCache`) is the next thing to dispatch. Nothing is blocking it.**

### ⚠️ Three plan errors caught by TESTS, not review (all lead-approved, plan amended + committed)
1. **T3** — a tolerance that was mathematically impossible to satisfy (block-straddling artifact
   forces a −0.223 dB offset; the plan asserted 0.2).
2. **T4 — the measure mode's rationale was BACKWARDS.** The claim was "integrated gating discards a
   one-shot's decay tail so short SFX under-read." The relative gate *already* excludes the tail —
   that is its job — so the specified 3 s window averaged the attack with silence and read **6 dB
   below** the mode it was meant to beat (−25.8 vs −19.5). Now `MomentaryMax` at **400 ms**
   (−17.99 vs −19.54). Renamed throughout; Tasks 6/10/11 reference the new name.
3. **T5 — `ApproxTruePeakDb` struck entirely.** Linear interpolation yields a convex combination of
   its endpoints, so `|a+(b−a)t| ≤ max(|a|,|b|)`: it can never exceed the sample peak, hence never
   detect an inter-sample peak — the only purpose of a true-peak meter. It also read 2.5 dB *below*
   sample peak on mono `[0,0,1.0]`. `ClipAnalysis`/`CachedLoudness` now carry **one `PeakDb`** field.

### Design constraint worth re-reading before touching `GainSolver`
`AudioSource.volume` is hard-capped at 1.0, so a clip needing +6 dB **cannot get it**. Gains are
therefore normalised **downward**: `final = raw − max(raw over analyzable clips)`, pinning the
loudest clip at exactly 0 dB. Relative spacing is preserved exactly and clipping becomes structurally
impossible. Accepted cost (lead signed off at design time): overall output is quieter, compensated
once on the master mixer. Silent/unanalyzable clips are excluded from the max so one broken asset
cannot attenuate the whole project.

### Op notes for this initiative
- Unity MCP is bound to **this** checkout (`dataPath` probe confirmed) — that is why the work is on
  a **branch in place, NOT a worktree**: a worktree would be invisible to `tests-run`, which would
  report green on code it never compiled.
- Unity **6000.3.8f1**; package targets `"unity": "2022.3"`; **zero package dependencies** (JsonUtility,
  not Newtonsoft).
- **HAZARD:** a subagent ran MCP `enroll_engine_plugin`, which auto-resolved the Unity MCP plugin to
  "latest" (0.84.3) and broke compatibility. It was reverted and verified (`manifest.json` still pins
  `com.ivanmurzak.unity.mcp: 0.76.0`). **Tell subagents not to run `enroll_engine_plugin`.**
- The lead's pre-existing `Assets/` dirt (designer `BusBuddiesImageToGrid.asset` edit + YAK
  `_prev_prompts` deletions) is **not ours — leave it alone.**

---

## ✅ SHIPPED (2026-07-15) — BB Hidden Pixels · Hidden Buses · Connected Buses

Three hand-authored Bus Buddies mechanics, TDD'd task-by-task on branch
`feat/bb-hidden-connected-mechanics`. Plan:
`docs/superpowers/plans/2026-07-15-bus-buddies-hidden-connected-mechanics.md`.

- **Full EditMode suite: 434/434 green** (413 baseline + 21 new tests, 0 regressions).
- **Package `0.9.0 → 0.10.0`**, tag `v0.10.0` (LOCAL — not pushed; lead's gate).
- **Hidden Pixels (new):** `BBPixelCell.Hidden`; generic Layer-1 flag-paint tool
  (`ICellFlagPainter`/`CellFlagPainterAsset`/`GameProfile.FlagPainter`/`GridEditTool.Hide`,
  driven by `GridCanvasPanel`+`PalettePanel`); `BusBuddiesHiddenPixelPainter` wired into
  `BusBuddiesProfile._flagPainter`; grid overlay; export/import `HiddenPixels` (`x*width+y`).
- **Hidden Buses (verify-only):** round-trip test pins `BusType:1` export + import.
- **Connected Buses (new):** `BusConnection` ops + `ConnectionsDeadlock`; `BBConnectedBusRule`
  appended to `_rules`; export/import `ConnectedBuses` coordinate pairs; connect/disconnect UI
  in `BusBuddiesQueuePanel`.
- **Open items:** (1) LEAD — re-pin BB game `#v0.10.0` + re-mirror BB Layer-2 + compile-check +
  push (agent can't compile-verify the game). (2) GAME TEAM — confirm the `HiddenPixels` `x*width+y`
  vs `PixelColors` `y*width+x` transpose is intended (square grids only) — see SESSION_NOTES flag.
  (3) VERIFIER — eyeball the 4 IMGUI surfaces (✦ Hide tool + overlay; connect UI + badges + soft-lock
  refusal) and one in-game hidden-pixel spot-check.

---

## 🔴 ACTIVE CONSTRAINT (2026-07-14) — 30 playable levels by ~2026-07-19

**The team needs at least 30 playable levels by the end of this week.** The lead has **PARKED the
image→grid automation work** — designers are converting the generated images and **fixing them by
hand**. His words: *"trying to do things automatically will have a limit."*

**Judge any new work by: does this get a designer to 30 playable levels faster?** Friction-removal for
designers is in scope; chasing full automation is not. Do not reopen the parked items unasked.

### The one open thread — BB game compile-check + push

- editor-core: **`master` `68f2e30`, tag `v0.9.0`**, pushed, 413/413 EditMode green.
- BB game (`E:/Projects/Hoppa/BusBuddies`, `main`): commit **`d654732` — COMMITTED, NOT PUSHED**
  (manifest re-pinned `#v0.9.0` + Layer-2 mirror re-synced).
- **Why held:** v0.9.0 is BREAKING — `IImageToGrid.Convert` now takes width/height and
  `ImageToGridAsset.Convert` is abstract, so a stale mirror won't compile. A bad push breaks `main`
  for the designers mid-deadline. The agent **cannot compile-verify BB** (Unity is bound to editor-core).
- **Lead step:** open BB in Unity → confirm compile → push `d654732`.
- A designer's in-progress `BusBuddiesImageToGrid.asset` edit (dark-blue→black remap) is deliberately
  left uncommitted.

### Shipped 2026-07-14 (all on `master`, pushed)

1. **BB auto-fill can no longer emit an unsolvable level** — `BusBuddiesConstructiveArranger` derives the
   bus queue from a simulated border-inward peel and verifies by exact replay. (The old dig-search had no
   real solvability gate above 64 cells; the lead's cupcake had its black outline buried 6 buses deep.)
2. **Bus queue draws HEAD at the top**, matching in-game pull order (display-only; data was never reversed).
3. **Grid-size selector** in the Image→Grid tab (the breaking change behind v0.9.0).
4. **Pixel-art prompt system** — 5 prompts × 20 ideas bound by `# @style:` tags.
   Spec: `docs/superpowers/specs/2026-07-14-yak-multi-prompt-image-generation.md`.

### PARKED — do not reopen without the lead asking

- **The dark outline.** Every generated image still has one. Three escalating rounds of "NO outline" in
  the prompt failed — the pixel-art convention beats the instruction. **The fix belongs in the converter**
  (absorb the dark border ring), not in more prompting.
- **90 of the 100 boss-brief images** are ungenerated ("Select all tagged" runs the rest, ~$6).
- **End-to-end was never verified** — we only ever judged the source PNGs; nobody converted at 40×40 and
  played the result. If automation resumes, start there.
- `1024 ÷ 40 = 25.6` is not a clean divisor — if smearing reappears at 40×40, that is why (32 is exact).

---

## ★ ACTIVE INITIATIVE — Bus Buddies (Food Hunt clone) — as of 2026-06-29

A new game authored through this editor: a re-theme of the live game **Food Hunt**, ~100%
mechanically identical (buses + passengers theme), built as a **YAK-sibling Layer-2** (configured
`BusBuddiesProfile.asset` + Layer-2 assets, NOT a subclass — `GameProfile` is sealed).
- **Backlog:** `docs/BUS-BUDDIES-BACKLOG.md` · **Spec:** `docs/superpowers/specs/2026-06-29-bus-buddies-design.md`
- **Goal:** generate + export **30 solvable, difficulty-graded levels** for a D1 40%+ validation campaign.
- **A companion game IS being built now on the YAK codebase** → exporter stays YAK-shaped (buses≡spools,
  `conveyorCount` key reused for the Active-Bus-Row slot count). The game must implement the SAME
  canonical rules we define (4-way accessibility + nearest-to-hole targeting).

**Core mechanic (canonical contract):** no gravity (removed blocks vanish, nothing moves); a block is
accessible if reachable from the frame edge through empty cells **4-way, outside-the-grid counts as open**
(edge blocks always reachable); tap pulls the top bus of a queue column into a stationary **Active Bus
Row (max 5)** which auto-empties into reachable matching blocks; **passenger targets the matching block
NEAREST the Hole** (bottom-center, squared-Euclidean, tie-break lowest index — makes the solver sound);
WIN = board cleared + all buses empty; LOSE = 5 active slots full + none can release (deadlock).

### SHIPPED to master (all pushed to origin)
- **Task 4** — image→grid options (subject-only Empty + black Outline) — master `2a26059`.
- **Spec** approved — master (`a3c3212`).
- **Sub-phase 1a — engine** — master `ea8ee76`. Pure-C# `Assets/BusBuddies/Runtime/Sim/`
  (BusLevelModel / BusSimState / BusSolver / BusAveragePlayer) + cells + queue data + 23 tests.
- **Sub-phase 1b-i — author & analyze** — master `52bfbd9`. `Hoppa.BusBuddies.Editor` asmdef + BB
  cell brushes + `BusBuddiesPalette.asset` (8 colors: red/blue/green/yellow/orange/purple/pink/cyan)
  + `BBColorBalanceRule` + `BusBuddiesAnalyzer`(+config, wraps 1a onto ILevelAnalyzer) +
  `BusBuddiesProfile.asset` (wires palette/cells/rule/analyzer; 30×30; `_spoolsBelowGrid=true`).
- **Sub-phase 1b-ii — author half (autofiller + queue panel)** — master `74aa848` (2026-07-05).
  `BusBuddiesAutofiller : LevelCompleterAsset` + config (bus-scaled cap 3–12/avg6, ColumnRange 1–5,
  APS-only gating — no complexity axis; `Partition` copied from YAK) + `BusBuddiesQueuePanel :
  TopSectionPanel` (author color/capacity/hidden, add/del/reorder/move, HEAD=Buses[0]; connected-bus
  UI deferred). Wired `_levelCompleter` + `_bottomSectionScript`. 7 TDD tests.
- **Sub-phase 1c — generation pipeline** — master `8d8c53f` (2026-07-05). `BusBuddiesImageToGrid`
  (Empty-bg + outline defaults, emits BB cells, outline=purple/tunable) + `BusBuddiesLevelGenerator`
  + config (analyzer-gated; procedural fallback all-pixel) + `BusBuddiesBatchHarness` + a mirrored
  BB difficulty-curve stack (config/tier-builder[clone-safe]/curve-harness/window) + a seeded default
  30-level 5-tier curve asset. Wired `_imageToGrid` / `_levelGenerator` / `_generatorConfig`. 16 TDD tests.
- **Full EditMode suite: 278/278 green.** Engine + analyzer + autofiller + generator all opus-reviewed.

### ⏭️ TOMORROW — the LEAD's in-editor eyeball (the whole pipeline is now runnable)
Open the Level Editor on `BusBuddiesProfile` and exercise the flows by hand:
1. **Author + auto-fill:** paint BBPixelCells → Bus Queue panel renders below the grid (bottom bus =
   HEAD) → **Auto-fill** → buses appear, `BBColorBalanceRule` green → **Analyze** → Solvable + APS + Band.
2. **Image → grid:** 🖼 Image mode → pick an image → Convert (Empty bg + purple outline) → Use This Level.
3. **Generate:** ✨ Generate → single procedural/image level, analyzer-gated Solvable.
4. **Batch / curve:** run `BusBuddiesBatchHarness` (menu) or the Difficulty Curve window → Batch Review
   window → import the good ones. Tune the default curve asset bands + outline color to taste.

### Deferred / blocked-on-game (not actionable until the BB game exists)
- **Exporter (1c Task 4) NOT built** — BB game doesn't exist yet → no target schema. Build when the
  game defines its level format (or lead provides a provisional schema).
- **APS calibration** — APS is measured but uncalibrated (no Bus Buddies player data; shares YAK's "Gap B").
- **Prove a generated level plays in the real game** (YAK "Gap C" analog).
- **3 mechanics' difficulty modeling** (Hidden cube / Hidden Bus / Connected Bus) — authorable+exportable
  later, accurate scoring deferred. Connected-bus authoring UI also deferred (ConnectedId carried as data).
- **Fast-follows (both inherited from YAK, present in both games):** `HashBlock` int.MinValue →
  negative-modulo IndexOOB (rare); `PickSourceImage` disk-branch Texture2D leak (editor-only); the
  column-remove-frame `ArgumentOutOfRangeException` in `BusBuddiesQueuePanel`/`YAKSpoolSectionPanel`.

### Op notes (Unity MCP, this session)
- Use the **direct `mcp__ai-game-developer__*` tools**, NOT `npx unity-mcp-cli` (CLI 500s on a stale token).
- Adding a new asmdef triggers a Unity **domain reload** → Editor-API calls 500 for ~tens of seconds; retry.
- **`git add` the FOLDER** in every commit so Unity `.meta` sidecars ship (1a missed them; caught at push).
- Team-workflow tracker: `.claude/teams/board.md` (Active table + Journal). Improvements: `/retro`.

---

## (previous initiative) — Automated level-generation tooling for YAK (A–E, phase-gated)

Plan: `C:\Users\itay0\.claude\plans\no-thanks-i-want-velvety-frost.md` · Spec: `docs/level-tooling-megaprompt.md`.
Five systems built as phases A–E; **stop for explicit approval at each gate.** Generic logic →
Layer 1 package (now **v0.6.0**); YAK specifics → `Assets/YAK/`.

### Phase A — YAK difficulty scorer / simulator — ✅ DONE, verified, awaiting gate (2026-06-15)

**NOT committed/pushed yet.** Full EditMode suite **145/145 green** (11 new YAK tests).

- **Layer 1 (v0.6.0, additive/non-breaking):** `AnalysisStatus` enum (Unknown/Solvable/Unsolvable/
  TimedOut/Faulted); `LevelAnalysisResult` gained `Status`, `ApsEstimate`+`ApsCalibrated`, `Band`,
  `WinPath`; `AnalysisRequest` gained `NodeBudget`, `Seed`. `package.json` 0.5.22→0.6.0 + CHANGELOG.
  **Pending: git tag v0.6.0 + game manifest pin bump when this initiative ships** (deferred until a
  Layer-1 consumer needs it; no game currently consumes these new fields).
- **YAK Runtime sim (engine-agnostic plain C#, `Assets/YAK/Runtime/Sim/`):** `YakLevelModel` (interned
  level: grid reduced to per-column bottom→top color sequences since consumption is bottom-row + gravity;
  empties omitted), `YakSimState` (ApplyMove/ResolveBelt lap-model to steady state/IsWin/IsDeadlock/
  HasLegalMove/Key), `YakSolver` (DFS+memo over the DAG; budget hit → BudgetExceeded, never Unsolvable),
  `YakAveragePlayer` (ε-careless myopic lookahead player; APS≈1/win-rate; `Calibrate` grid-search hook).
- **YAK Editor (`Assets/YAK/Editor/Analysis/`):** `YAKLevelAnalyzer : LevelAnalyzerAsset` +
  `YAKAnalyzerConfig`; assets created at `Assets/YAK/Data/Config/Analysis/` and wired into
  `YAKProfile._levelAnalyzer` (via script-execute). Conveyor slots resolved request→GameData["conveyorCount"]→config.
- **Tests:** `Assets/YAK/Tests/Editor/` (`Hoppa.YAK.Editor.Tests.asmdef` + `YakAnalyzerTests.cs`):
  solver trivial/order-sensitive(1-slot deadlock vs 2-slot solve)/budget-hit; player determinism/
  trivial-APS-1/slack-lowers-APS; analyzer solvable+uncalibrated-APS/balance-mismatch-Unsolvable/
  no-spools-Unknown/null-fault; smoke-loads the 2 TestConfigs without faulting.
- **Calibration deferred (decided):** only 2 test levels in repo (one spool-less), no real-player APS.
  ε default 0.1, `ApsCalibrated=false`. Run `YakAveragePlayer.Calibrate` when the 10 levels + APS land.

### Phase B — YAK spool auto-filler — ✅ DONE, verified, awaiting gate (2026-06-15)

**NOT committed/pushed yet.** Full EditMode suite **151/151 green** (6 new autofill tests).

- **YAK Editor (`Assets/YAK/Editor/Analysis/`):** `YAKSpoolAutofiller : LevelCompleterAsset` +
  `YAKSpoolAutofillConfig`; assets at `Assets/YAK/Data/Config/Analysis/`, wired into
  `YAKProfile._levelCompleter` (script-execute). Surfaces in the shared `AutofillPanel` (Auto-fill button).
- **Algorithm:** tally per-color wool (via IColoredCell) → `Partition(total,min,max,avg)` splits each
  color into capacities summing EXACTLY (balance by construction; total<min → one undersized spool) →
  random sweep: assign into a column count in `ColumnRange` (2–5), gate each candidate on
  `YAKLevelAnalyzer` (Status==Solvable + |APS−target|≤tol), accept first in-band, else honest
  best-effort (closest-solvable / "no solvable arrangement" / "target unreachable"). No mechanic toggles.
- **Conveyor precedence fix:** YAK analyzer + autofiller resolve belt slots from
  `GameData["conveyorCount"]` FIRST (authored truth), since the shared AutofillPanel's 24/30 dropdown is
  YarnTwist belt-capacity and would mis-drive YAK. (Known Layer-1 nicety deferred: make the panel's
  conveyor presets profile-configurable — not blocking.)
- **Tests:** `Assets/YAK/Tests/Editor/YakAutofillTests.cs` — Partition sums-exactly+in-range +
  undersized exception; single-color balanced+solvable+caps-in-range; two-color balanced; empty-grid →
  empty top success; no-analyzer fails cleanly.

### Phase C — image→grid converter — ✅ DONE, verified, awaiting gate (2026-06-15)

**NOT committed/pushed yet.** Full EditMode suite **155/155 green** (4 new image tests) + real-profile smoke.

- **Layer 1 (v0.6.0, additive):** `IImageToGrid` + `ImageToGridAsset` (`Editor/ImageToGrid/`);
  `GameProfile._imageToGrid` slot + `ImageToGrid` accessor; `ImageToGridModePanel` (mirrors
  GeneratorModePanel: source-texture field, converter-asset inspector in Advanced, Convert + preview +
  Use-This-Level via OnUseLevel); `ToolbarPanel` `OnImageToggle`/`ImageMode`/`ShowImage` + `🖼 Image`
  button; `LevelEditorWindow` `_inImageMode` (mutually exclusive with Order/Generate; reuses
  `HandleGeneratorUseLevel` load handoff). CHANGELOG 0.6.0 updated.
- **YAK (`Assets/YAK/Editor/ImageToGrid/YAKImageToGrid.cs`):** pipeline = `ReadablePixels` (blit→RT,
  handles non-readable) → `Downscale` (area average to GridW×H) → `Segment` (BorderRing flood-fill from
  edge of border-dominant color; Alpha and MostSaturated modes; ambiguity → MostSaturated fallback) →
  `NearestId` redmean quantize to palette → background = most luminance-contrasting neutral (excludes
  subject's own id) → `MergeToCap` (least-used → nearest neighbour) → emit all-`YAKWoolCell`
  LevelDocument, **zero empties**, GameData conveyorCount=5. Config: `ColorCap=6`, `BackgroundNeutrals`
  (Grey/GreyLight/GreyDark/DarkGrey/White/Black), `SegmentationMode`. Asset wired into
  `YAKProfile._imageToGrid`.
- **Tests:** `Assets/YAK/Tests/Editor/YakImageToGridTests.cs` (hermetic synthetic palette) — dims+all-wool
  +no-empties; determinism; color-cap respected; subject≠background. Real-profile smoke (script-execute):
  16×16 red-on-grey → 30×30, distinct=2 [White,Red], palette=36.

### Phase D — solution export + in-game viewer — ✅ DONE (editor verified; game viewer UNVERIFIED) (2026-06-15)

**NOT committed (editor-core or game).** Full EditMode suite **157/157 green** (2 new solution tests).

- **Layer 1 (v0.6.0, additive):** `Editor/Solution/LevelSolution` (`schemaVersion`/`levelId`/`steps:int[]`)
  + `SolutionJson` (Serialize/Deserialize/Write). Flat lowercase fields so the SAME file round-trips
  through Newtonsoft (editor) AND JsonUtility (game, zero deps). `AutofillPanel."Save Solution…"` now also
  writes `<levelId>.solution.json` from `LevelAnalysisResult.WinPath` (graceful skip when WinPath null —
  YarnTwist doesn't set it). CHANGELOG 0.6.0 updated.
- **Tests:** `Assets/YAK/Tests/Editor/YakSolutionTests.cs` — export → JSON round-trip → replay steps
  through `YakSimState` → `IsWin` (acceptance: solution replays to a win in the simulator); no-WinPath
  writes nothing.
- **Game viewer (UNVERIFIED):** `E:\Projects\Hoppa\YarnKingdom\Assets\_YAK\Scripts\Gamelogic\Gameplay\YAKSolutionViewer.cs`
  — self-contained MonoBehaviour (UnityEngine/JsonUtility only, namespace `YAK.Gamelogic.Gameplay`):
  loads a `.solution.json` (TextAsset or path), tracks current step, `CurrentColumn` + Next/Prev/Reset +
  auto-advance + `onStepChanged(column)`/`onCompleted` UnityEvents + optional column-anchor gizmo + OnGUI
  controls. Does NOT tap anything itself (decoupled from the game's board). **User must open YarnKingdom in
  Unity 2022.3, let it import (generates the .meta), wire `onStepChanged` to the board highlight, and
  play-test.** Not committed in the game repo.

### Phase E — batch generator + review window — ✅ DONE, verified (2026-06-15)

**Phase E NOT committed yet.** Full EditMode suite **160/160 green** + real batch smoke (kept 2/2,
wrote json+png+stats).

- **Layer 1 (v0.6.0, Editor/Batch):** `LevelThumbnail` (grid→Texture2D/PNG, 1px+/cell from palette);
  `BatchStaging` + `LevelStats` + `BatchCandidate` (pure scan/import helpers); `BatchReviewWindow`
  (`Window▸Hoppa▸Batch Review`: folder scan, thumbnail+stats grid, multi-select, Import Selected → target
  folder). CHANGELOG 0.6.0 updated.
- **YAK (Editor/Generator):** `YAKLevelGenerator : LevelGeneratorAsset` + `YAKGeneratorConfig` — source
  image from a library folder (picked by seed) → `YAKImageToGrid`, procedural fallback when none →
  `YAKSpoolAutofiller` → `YAKLevelAnalyzer` gate (Succeeded iff solvable + APS in band). Wired into
  `YAKProfile._levelGenerator` + `_generatorConfig`. `YAKBatchHarness` ([MenuItem]
  `Window▸Hoppa▸YAK▸Run Batch (20)` + `RunHeadless` for `-batchmode -executeMethod`): loop → dedup
  (grid FNV signature) → filter (solvable + |APS−target|≤tol) → write `<id>.json` + `<id>.png` +
  `<id>.stats.json` to dated `YAK_Batch/<ts>/` (gitignored).
- **Analyzer scale fix (important):** rollout-rescue in `YAKLevelAnalyzer` — the exact solver
  budget-exceeds on 30×30, so a winning Monte-Carlo playout now upgrades `TimedOut → Solvable`
  (WinPath null at that scale; exact Save-Solution remains best for small/medium levels). This is what
  makes full-size generation/auto-fill actually accept levels.
- **Tests:** `YakBatchTests.cs` — thumbnail size/color; staging scan+import round-trip; generator
  procedural end-to-end → solvable autofilled level.
- **Image-AI deferred:** the HTTP image step is not built (no endpoint/auth yet). Source images come
  from the config's library folder (drop images in) or the procedural fallback. A future `IImageSource`
  fetching AI images into that folder drops straight into the same flow.

---

## ✅ INITIATIVE CODE-COMPLETE — all phases A–E done, committed, tagged, PUSHED (2026-06-15)

Editor-core `master`: `b89b78f` (A–C), `1921303` (D), `111cc47` (E) — **tag `v0.6.0`** at E.
All **pushed to origin**. Full EditMode suite **160/160 green**; real batch smoke kept 2/2.
**Caveat: NOTHING has been hand-verified in the editor / in-game by a human yet.**

### ⏭️ TOMORROW — TEST & VERIFY (do this first)

Open the Level Editor on `YAKProfile` and exercise the new flows by hand:
- [ ] **Analyzer:** open a YAK level (or import a TestConfig), press **Analyze** — confirm the
      Spool Analysis panel shows a sensible Status + measured APS (marked *uncalibrated*).
- [ ] **Auto-fill (B):** paint/Import a grid, press **Auto-fill** — confirm spools appear, the level
      validates (YAKColorBalanceRule green) and reads solvable.
- [ ] **Image→grid (C):** 🖼 **Image** toolbar mode → pick a source image → **Convert** → preview →
      **Use This Level**. Confirm a clean blocky 30×30, no empty cells, subject vs background.
- [ ] **Solution (D):** **Save Solution…** on a small/medium solvable level → confirm both `.txt` and
      `.solution.json` are written. (Then in **YarnKingdom**: import `YAKSolutionViewer.cs`, wire
      `onStepChanged` to the board highlight, load the json, play-test that following it WINS.)
- [ ] **Batch (E):** **Window ▸ Hoppa ▸ YAK ▸ Run Batch (20)** → then **Window ▸ Hoppa ▸ Batch Review**,
      point it at the dated `YAK_Batch/<ts>/` folder → confirm thumbnails + stats, multi-select,
      **Import Selected** lands levels in a target folder that open + export normally.

### Deferred (by design — not blocking)
- **Image-gen API** (endpoint + auth) for the batch image step — currently library-folder + procedural
  fallback. A future `IImageSource` drops into `YAKLevelGenerator.PickSourceImage`.
- **APS calibration:** run `YakAveragePlayer.Calibrate` once the 10 real levels + player APS exist
  (ε currently default, `ApsCalibrated=false`).
- **Game manifest pin:** bump the YAK game's `manifest.json` to `#v0.6.0` when it needs the new Layer-1
  contracts (no game consumes them yet).
- **Game viewer** `YAKSolutionViewer.cs` (YarnKingdom) — open in Unity 2022.3, wire + play-test (above).

---

## Latest shipped — APS auto-fill + mechanic toggles + palette solver (2026-06-09) — ✅ SHIPPED

editor-core `master c7edf40` + tag **`v0.5.22`**; game `itay-main 1d9fdd7` (manifest `#v0.5.22`).
**122 EditMode green; UI verified via screenshot.** See memories
`design_yarntw_aps_and_mechanic_toggles` + `design_yarntw_palette_countdown`.
- Auto-fill is **APS 1-6** (replaced Difficulty 1-10); `TargetAPS`, APS curves in config.
- Per-mechanic **bool table** = **Hidden Spools** + **Connected Spools** checkboxes (generic Layer-1
  `MechanicToggles` hook). Auto-fill now GENERATES connected pairs (deadlock-guarded).
- Analyzer **models Palettes** (`PaletteReq`/`_opened`; covered box tappable iff normal-access AND
  global opens ≥ amount — matches game `BoxClicked` countdown, +2 per connected pair) + replay-based
  **labelled solutions** (co-taps, palette reveals, spool unlocks, hidden reveals).
- Summary panel trimmed to **ID / Grid / Coins / Notes**.
- [ ] **PENDING — lead:** confirm the game compiles in Unity 2022.3 (agent can't compile-verify game).

---

## Active phase

**Save Solution / Win-path solver correctness — YarnTwist (2026-06-03) — ✅ VERIFIED WORKING (2026-06-04).**

> **DONE.** User play-tested level_044 (the hardest, zero-slack case) in the real game on
> 2026-06-04 — the generated solution **won perfectly.** The belt model is confirmed correct
> against the live game (see "Model verified equivalent" below). Everything is committed + pushed
> (editor-core `master`, game `itay-main`).
>
> **Logger KEPT (user decision, 2026-06-04):** the temporary `YATSolutionDebug` logger +
> `SolutionVisualizer` are intentionally left in the game project — still useful for validating
> more levels. Do NOT remove them. The removal checklist below is parked, not abandoned.

### Tunnel solver fix — output-cell modeling (2026-06-04) — CODE COMPLETE, awaiting in-game re-test

While validating more levels, level_041 (has tunnels) generated a solution whose tunnel steps
pointed at the wrong tiles. **Root cause:** the analyzer modeled a tunnel's tappable item at the
**tunnel tile**, but in-game the tunnel spawns its queued boxes into the adjacent cell in
`OutputDirection` and the player taps THEM there (`YATTunnelPrefabComponent` +
`YATGameManagerComponent:485-495`: `boxGridPosition = tunnelPos + direction`; unlock via
`IsBoxActive` at that cell). The tunnel tile is never tapped.

**Fix (analyzer-only, Layer-2; 5 edits to `YarnTwistLevelAnalyzer.cs`):**
- `BuildItems`: place the tunnel `Item` at `tunnelPos + OutputDirection` (fallback to tile if
  off-grid); map `idxAt` there.
- `Analyze` grid-open map: treat the tunnel TILE as solid (added `!(cell is YarnTunnelCell)`) so it
  never falsely unlocks neighbours.
- `IsCellCleared` (both SearchContext + RolloutContext): tunnel cell opens only when fully depleted
  (`queueIdx >= Queue.Length`), matching the game's ActivateNeighbors-on-last-box.
- `FormatSolution`: relabel "Tap Tunnel box (x,y) → …"; coordinate now resolves to the output cell.

**Verified:**
- New EditMode fixtures 26 (solution targets output cell) + 27 (accessibility follows output cell;
  dead-ended tunnel = unsolvable). Both went red→green.
- **Full EditMode suite green: 117/117** (incl. pre-existing tunnel fixtures 5 & 15).
- Ran the analyzer on the real `level_041.json` via script-execute: **Solvable, 26 steps**, tunnel
  steps now correctly at output cells — tunnel#1 `(4,3)`, tunnel#2 `(3,2)` player-coords, each
  tapped 3× in queue order.
- Synced to game repo `Assets/_YAT/Scripts/Editor/Analysis/YarnTwistLevelAnalyzer.cs` (identical 5
  edits). Layer-2 only → **no UPM tag bump.** Both repos UNCOMMITTED.

**Resume / next:**
- [ ] **User:** regenerate level_041's solution (Save Solution) in the game and re-test in-play to
      confirm the tunnel steps now highlight the correct (output-cell) boxes and the level wins.
- [ ] **User:** confirm the game project still compiles in Unity 2022.3 (agent can't compile-verify).
- [ ] Commit + push both repos once confirmed (editor-core `master`, game `itay-main`). Analyzer +
      the 2 new test fixtures only.

### Goal
Make the **Save Solution** feature (Spool Analysis panel → "Save Solution…") produce a
tap sequence a player can actually follow to WIN the level. It was producing unsolvable /
nonsensical / belt-jamming solutions. Test level = **level_044** (`E:/Projects/Hoppa/YarnTwist/
Assets/_YAT/Configs/LevelEditor configs/level_044.json`), the hardest current level (369 balls
= 369 spool capacity, zero slack — a brutal calibration case).

### What we fixed today (all committed + synced, in order)
All changes in `Assets/YarnTwist/Editor/Analysis/YarnTwistLevelAnalyzer.cs` (Layer-2; synced to
game `Assets/_YAT/Scripts/Editor/Analysis/YarnTwistLevelAnalyzer.cs`; no UPM tag — Layer-2 only):

1. **Demand-ordered solution recording** (editor-core `7a51c7e`, refined `03eadd4`) — the DFS
   records taps in *demand-score order* (taps whose color matches current column heads go first)
   so the hint reads naturally instead of grid-scan order. (A strict pre-tap capacity gate was
   tried here and **reverted** — it made recording time out; see "dead ends".)
2. **Player-perspective Y coords** (`e5fdfab`) — solution text + `BuildItems` scan flipped so
   `(x,0)` = TOP row nearest the belt (what the player sees), not data-space bottom row.
   `Model.GridHeight` added; `FormatSolution` flips `displayY = GridHeight-1 - dataY`.
3. **Box-tap unlock mechanic** (`151bbf3`) — only the top row is tappable initially; tapping a
   box unlocks its 4 orthogonal neighbours (walls block; empty cells always open). Modelled via
   `IsAccessible(i)` in both SearchContext + RolloutContext. `Model` gained `GridWidth`,
   `GridItemIdx[]`, `GridCellOpen[]`. See memory `design_yarntw_box_unlock`.
4. **Belt drain order = RIGHTMOST column first** (`1d0421f`) — real belt circulates
   counter-clockwise; rightmost column (highest index) consumes a shared color first.
   `ResolveMatches` iterates `k = Columns-1 → 0`.
5. **Belt jams AT capacity, not above** (`f706493`) — the real lose fires when on-belt count
   == capacity AND nothing matches (full belt of unmatchable balls = permanent jam; queued
   balls can't flow through). DFS/rollout check changed `_bagSum > _capacity` → `>= _capacity`.

### How #4 and #5 were nailed: a real game-log capture
We added a TEMPORARY debug logger to the GAME project (`YATSolutionDebug.cs` under
`Assets/_YAT/Scripts/Gamelogic/Gameplay/`) that writes a full run to
`E:/Projects/Hoppa/YarnTwist/yat_solution_debug.log` (gitignored). The user played level_044;
the log gave ground truth: **capacity = 30, drain order = rightmost-first, lose at belt==30
with no match.** That confirmed #4 and pinpointed #5.

### Current status — level_044 now SOLVABLE & SAFE
Analyzer produces a 41-step solution whose between-taps belt residue peaks at **27/30 — never
jams**. All **115 EditMode tests pass**. Capacity is set via the AutofillPanel dropdown
(**24** for levels 1–15, **30** for 16+; level_044 = 30).

### Model verified equivalent to the real lose-check (2026-06-04)
Read the game's `YATGameManagerComponent.CheckLose()` + `CanAnyBallFillAnyWinder()`. The real
lose fires iff **belt full (`_splineYarnBalls.Count >= MaxRoadBalls`) AND no ball matches any
current non-full winder head.** The analyzer's `_bagSum(settled) >= capacity` is *provably the
same* condition: settled residue contains only balls matching no head, so residue>=cap ⟺
belt-full-and-nothing-matches. **The transient-peak worry is dead** — the game does NOT lose on a
momentarily-full belt if any ball still matches a winder (`CanAnyBallFillAnyWinder` short-circuits
the loss); the 9-ball spike drains. So the model is structurally correct, not just empirically
calibrated. **The only residual sim-vs-real gap is PACING:** the model assumes `ResolveMatches`
settles to fixpoint between taps (winder heads advance instantly); the game advances a head only
when 3 physical balls arrive. Tapping faster than the belt drains can hit a transient
"full + matching head hasn't advanced yet" state the model never represents → loss.

### Resume checklist (do these tomorrow)
- [x] **User play-test (2026-06-04):** level_044 solution played through in the real game and
      **WON.** Pacing guidance confirmed sound: follow the recorded order one tap at a time,
      letting the belt settle between taps (sim == reality under that condition).
- [~] **Logger removal — DEFERRED (user keeping it 2026-06-04).** When eventually removing the
      temporary `YATSolutionDebug` logger, the 5 hook sites are (grep-verified 2026-06-04):
      `YATBoxPrefabComponent.cs:268` (`LogTap` in OnClick), `YATYarnBallPrefabComponent.cs:89`
      (`LogConsume` in OnFindWinder), `YATGameManagerComponent.cs:275` (`Reset` at level start) +
      `:763` (`LogFail` in CheckLose), plus the read-only accessors added for it:
      `YATWinderPrefabComponent.cs:69` (`WinderColumn` getter) and
      `YATWinderColumnPrefabComponent.cs:19` (`ColumnIndex`). Also the `.gitignore` line + delete
      `YATSolutionDebug.cs`. (`SolutionVisualizer.cs` is the play-time path overlay — keep/remove
      independently.)
- [ ] **If a FUTURE level fails:** capture a fresh `yat_solution_debug.log` of the user FOLLOWING
      the generated solution (not free-play) and re-read it. **At the failing tap, compare the
      winder head colors against the belt contents** — if a column whose head *should* have
      advanced (per the model) is still showing its old color, that confirms the
      head-advancement-timing/pacing gap, not a model bug. Only then consider a positional belt sim.
- [ ] **Difficulty drift check (low priority):** the `>=` change slightly tightens the shared
      hot path (win-count/autofill/difficulty). No test regressed, but spot-check that
      auto-fill difficulty bands still feel right on a couple of levels.

### Dead ends (don't re-try)
- **Strict pre-tap gate** (`residue + 9 ≤ capacity`): too pessimistic — made level_044 unsolvable
  / timed out. The real belt drains *while* balls feed in, so the correct bound is post-resolve
  `residue >= capacity`, not pre-tap room-for-9. Reverted.
- **Order-robust (all-24-permutations)**: level_044 has zero slack (peaks at capacity even under
  the single best order), so requiring safety under all orders → no solution. Abandoned in favour
  of matching the real (rightmost-first) order exactly.

### Also shipped today (separate, done)
- **Palette exporter fix** (`d57beff`, game `f9a52c8`): export `PaletteAmount` + move
  `ExtraFeatureBottomType`/`PaletteAmount` after `ColorType`/`Hidden` (Eliran's field order).
- **`scan-project` skill** created (`.claude/skills/scan-project/SKILL.md`) — run `/scan-project`
  at the start of a familiar-project session for a resume briefing.
- **SolutionVisualizer** (game-only, `Assets/_YAT/Scripts/SolutionVisualizer.cs` + Editor):
  drop on a GameObject, assign a `.solution.txt`, click "Show Solution Path" in Play mode →
  white gizmo squares over the boxes to tap (yellow = current). Auto-advances on box click
  (subscribes to `YATBoxPrefabComponent.BoxClicked`). Gizmos are Scene-view only.

---

## Parked — New game mechanics — week of 2026-06-01 — 3 mechanics, one per spec.

Mechanic 1 = **Connected Boxes** (design approved, spec written, NOT yet implemented).
Mechanics 2 & 3 = TBD, to be pulled from the game project as we get to them.

> **Source-of-truth rule for every new mechanic this week:** the YarnTwist GAME
> project (`E:/Projects/Hoppa/YarnTwist`, Layer-2 under `Assets/_YAT/`, branch
> `itay-main`) defines the Layer-2 data structure. Eliran implements game-side first;
> we mirror his `level_config.json` shape EXACTLY (field names, enum ordinals, key
> casing) — do **not** invent an editor-side schema. Workflow per mechanic: dispatch a
> read-only agent into the game project → find the `BottomConfig`/enum/runtime classes
> + a sample `level_config.json` → adopt identifiers → brainstorm editor side → write a
> per-mechanic spec. See memory `feedback_eliran_game_code_is_schema_source_of_truth`.

### Mechanic 1 — Connected Boxes (2026-06-01) — CODE COMPLETE; awaiting user EditMode test run + rollout approval

Spec: `docs/superpowers/specs/2026-06-01-yarntwist-connected-boxes-design.md` (read it first).

**What it is:** two orthogonally-adjacent regular boxes are linked; tap either → both
release balls & clear together. Colors independent. Authored via right-click
Connect/Un-connect, shown with a white outline.

**Game schema (Eliran, commit `56608a5`) — there is NO connection object.** A connected
box is a normal `BottomConfig` with `BottomType = 6` (ConnectedBox) + a PascalCase string
`Direction` pointing at its partner. Pair = two boxes with reciprocal Direction
(`(2,4)"Right"` ↔ `(3,4)"Left"`). Each keeps its own `ColorType`. The game does NOT
validate reverse links → the editor must guarantee reciprocity. Full schema in the spec
+ memory `reference_connected_boxes_game_schema`.

**Implementation checklist (resume here next session):**
- [x] **Layer 1 (UPM → tag `v0.5.20`)** — extended the context-action system:
      `CellActionContext` struct (Cell/Registry/Session/CellRef) + `.meta` (guid
      `7d3f9b2a1c8e4f06b5a09d2e4c6f8a1b`) passed to `GetContextActions`; `CellContextAction`
      gained a free-form `Action<LevelEditorSession>` apply ctor; `GridCellPopup` runs apply
      under one `PushUndoSnapshot`. Updated implementers `YarnBoxCellDefinition` +
      `YarnArrowBoxCellDefinition`. **NOTE: YAK does NOT implement `ICellContextActions`
      (verified by grep) → no YarnKingdom sync needed for this signature change.**
- [x] **Data model** — `YarnBoxCell.ConnectedDir` (`YarnDirection?`, null = unconnected),
      `[JsonProperty("connectedDir", NullValueHandling=Ignore)]`. Done.
- [x] **Authoring** — `YarnBoxCellDefinition`: `Connect Pair: <Dir>` actions filtered to
      valid unconnected-box neighbors; `Un-connect` when connected; both mutate BOTH boxes
      reciprocally in one undo step (`Connect`/`Disconnect` helpers). White border +
      shared-edge seam via `DrawConnectionOutline`. Convert-to-arrowbox now gated to
      unconnected boxes only (avoids orphaning a partner). Done.
- [x] **Validation** — `YarnConnectedBoxRule` (Error on out-of-bounds / non-box / non-
      reciprocal). Created `.cs` (guid `c1a2b3d4e5f60718293a4b5c6d7e8f90`) + `.asset`
      (guid `d2b3c4e5f6071829304a5b6c7d8e9f01`, `_id: yt.connected_box`); wired into
      `YarnTwistProfile.asset` `_rules`. Done.
- [x] **Export** — `YarnMasterLevelExporter.BuildBottomConfigs`: connected box overrides
      `BottomType → 6` + adds `Direction` string (keeps own ColorType/Hidden). Added
      `yt.connectedbox → 6` to `YarnCellTypeMapping.asset`. Done.
- [x] **Analyzer** — `YarnTwistLevelAnalyzer`: added `int[] Partner` to `Model`; `Item.PartnerIndex`
      + `BuildItems` Pass 3 links reciprocal pairs (degrades to independent box if non-reciprocal);
      `Partner` in intern + StructHash; `IsTappable` Box case skips the non-canonical (higher-index)
      member; `ApplyTap` co-taps partner (both colors' balls, both tapped, one ResolveMatches) in
      BOTH SearchContext + RolloutContext; DFS undo restores the co-tapped partner; rollout
      `FeedDemand` scores the canonical member by summed demand of both colors. Done.
- [x] **Auto-fill** — confirmed **correct-by-delegation**; no structural change. Added the
      inventory comment (connected box still = 9 balls = 3 spools of its color; analyzer owns the
      clear-together effect). Connected-box autofiller test still to add (see Tests).
      Tuning watch: difficulty curves were tuned connection-free; revisit retune later, not now.
- [x] **Tests (EditMode)** — COMPLETE:
      - [x] analyzer (`YarnTwistLevelAnalyzerTests` fixtures 18–21): same-color pair = 1 path,
            distinct-color pair = 1 path, arrow-prereq satisfied by co-tap, non-reciprocal =
            2 independent paths.
      - [x] exporter (`YarnMasterLevelExporterTests` +3): connected → BottomType 6 + Direction
            string + own ColorType; reciprocal pair; unconnected box has no Direction key.
      - [x] validation + context actions (new `YarnConnectedBoxTests.cs`, +meta guid
            `e3c4d5f60718293a4b5c6d7e8f901a2b`): rule errors on dangling/non-box/out-of-bounds
            & passes reciprocal; connect sets reciprocal dirs; direction filter; neighbour-already-
            connected excluded; connected box offers Un-connect only; un-connect clears both;
            connect→undo restores (also covers ConnectedDir serialization round-trip).
      - [x] autofiller (`YarnTwistSpoolAutofillerTests` fixture 10): connected pair grid →
            balanced (6 pink spools) + solvable.
- [x] **Tests verified** — user ran EditMode tests in Unity: all pass.
- [x] **Committed + tagged locally** — commit `bfc0752` on `master` (25 files; Connected Boxes
      only, no unrelated working-tree changes); lightweight tag `v0.5.20` points at it.
- [x] **Rollout — SHIPPED (2026-06-01).** editor-core: `master` `7af843b` + tag `v0.5.20`
      (`bfc0752`) pushed to `origin`. YarnTwist game (`itay-main`, commit `e8a727f`, repo
      `hoppa-cloppa/YAT---Yarn-Twist`): manifest pinned `#v0.5.20` + Layer-2 sync (YarnBoxCell,
      Yarn{Box,ArrowBox}CellDefinition, YarnMasterLevelExporter, YarnTwistLevelAnalyzer, new
      YarnConnectedBoxRule). **User confirmed the game compiles clean in Unity 2022.3.** YAK sync
      NOT needed. The v0.5.20 `ICellContextActions` signature change was breaking — BOTH game cell
      defs (box + arrowbox) had to sync in lockstep with the pin bump (lesson for future Layer-1
      interface changes).
      - **Deferred game-side follow-ups (optional, non-blocking):** the game's embedded editor has
        connected-box authoring (ConnectedDir + Connect/Un-connect UI + outline) and export emits
        `BottomType 6` via the exporter's `Get(...)` default, BUT (a) `YarnConnectedBoxRule` is NOT
        wired into the game's `GameProfile` asset (no `.asset` created there) → no validation in the
        game's editor; (b) `yt.connectedbox→6` not added to the game's cell-type-mapping asset
        (harmless). Wire these in the game project if its embedded editor needs the full safety net.
        Primary authoring is in editor-core where everything is wired + tested.
      Watch-if-tests-fail: (a) hand-written `.meta` GUIDs for `CellActionContext`,
      `YarnConnectedBoxRule` (.cs+.asset), `YarnConnectedBoxTests` must be unique & importable;
      (b) the `CellContextAction` Func/Action ctor overload resolves by the named `create:`/
      `apply:` argument at every call site.

### Mechanic 2 — Connected Spools (2026-06-02) — CODE COMPLETE; awaiting user EditMode test run + rollout approval

Spec/plan: `C:\Users\itay0\.claude\plans\let-s-talk-about-the-stateless-meerkat.md`.

**What it is:** two spools in *adjacent* columns are linked; each stays locked until BOTH
reach their column's bottom active row, then the chain breaks and both activate. Authored in
the spool panel via right-click Add Connect / Disable Connect; shown with a number badge +
chain line.

**Game schema (Eliran) — NO connection object.** A connected spool is a `WinderConfig` with
`WinderType = ConnectedWinders` + `ConnectedColumnIndex` + `ConnectedWinderIndex` pointing at its
partner (mirrors Connected Boxes). Source: `YarnTwist/.../YATLevelManager.cs` (`WinderConfig`,
`enum YATWinderType { None, ConnectedWinders }`). Editor stores a stable `ConnectionId` on the
spool; the exporter translates to the partner's `(column, index)`.

**User decisions (all the ambitious option):** (1) commit-on-first-click — the first spool sticks
and an unfinished pair is flagged incomplete by validation; (2) draw the actual chain line across
columns; (3) analyzer models the lock now; (4) auto-fill stays analyzer-accurate (it still wipes
connections — connect AFTER auto-filling).

**Implementation checklist:**
- [x] **Data model** — `YarnSpoolData.ConnectionId` (`int?`, `[JsonProperty("connId", Ignore)]`).
- [x] **Authoring** — `YarnTopSectionPanel`: row right-click `GenericMenu` (Change Color… moved into
      the menu; Add/Complete/Disable/Cancel Connect); number badge (amber while pending) + cross-column
      chain line drawn after the scroll views; deferred color-picker (col,idx) so it reopens inside the
      scroll view. Connection ops live in new **`YarnSpoolConnection`** (UI-agnostic, tested):
      `BuildConnInfo`/`DisplayNumber`/`AllocId`/`CanComplete`/`Connect`/`DisconnectGroup`.
- [x] **Validation** — `YarnConnectedSpoolRule` (Scope=Level): incomplete (size 1) → *"Connected
      Spool Pair N is incomplete…"*; size>2; same/non-adjacent columns. `.cs` guid
      `c2a3b4d5e6f70819203a4b5c6d7e8f91`, `.asset` guid `d3b4c5e6f7081920304a5b6c7d8e9f02`
      (`_id: yt.connected_spool`); wired into `YarnTwistProfile.asset` `_rules`.
- [x] **Soft-lock prevention (2026-06-02 follow-up)** — user hit a mutual deadlock by authoring two
      *crossing* pairs (pair links can't cross between a column-pair). `YarnSpoolConnection.ConnectionsDeadlock`
      = color-blind head-advance simulation (infinite yarn; a connected spool clears only when its partner
      is simultaneously at its head). Enforced two ways: (a) the connect menu disables a completion that
      `CompletingDeadlocks` → *"would soft-lock — links can't cross"*; (b) `YarnConnectedSpoolRule` emits a
      soft-lock Error (safety net for reorder/move/hand-edit; blocks export). Tests added (+4) in
      `YarnConnectedSpoolTests`.
- [x] **Export** — `YarnMasterLevelExporter.BuildTopConfigs`: complete pair → both winders get
      `WinderType:"ConnectedWinders"` (string, Direction precedent) + reciprocal `ConnectedColumnIndex`/
      `ConnectedWinderIndex`; unconnected/incomplete spools unchanged.
- [x] **Analyzer (lock)** — `YarnTwistLevelAnalyzer`: `Column` gains `PartnerCol`/`PartnerPos`,
      `ParseTopSection` resolves complete pairs (size≠2 → independent), `Model.ColPartnerCol/Pos` +
      `Intern` + `ComputeStructHash`. **Key:** shared `ResolveMatches` gates a connected head spool with
      the pure latching check `spoolHead[partnerCol] < partnerPos` — memo-safe, alloc-free, covers DFS
      + rollouts. Caveat understood: belt capacity is checked at node entry (post-resolve), so a
      one-way lock only reduces win paths when balls stay stuck across a node; mutual locks deadlock →
      unsolvable.
- [x] **Auto-fill** — no behavior change; inventory comment noting it clears connections.
- [x] **Tests (EditMode)** — exporter (+3: reciprocal pointers / no WinderType when unconnected /
      incomplete exports unconnected); analyzer (+2: connected lock removes orderings vs unconnected
      baseline [relational, cap 12]; mutual lock → unsolvable); new `YarnConnectedSpoolTests.cs`
      (.meta `e4c5d6f70819203a4b5c6d7e8f901a2c`): validation + authoring (two-step connect, adjacency
      gate, disable clears both, connect→undo round-trip, pending detection).
- [x] **Tests verified** — user ran EditMode tests in Unity: all green (incl. soft-lock suite).
- [x] **Rollout — editor-core SHIPPED (2026-06-02).** `master` commit `ebaddc8` pushed to `origin`
      (18 files, Connected Spools only; spec at `docs/superpowers/specs/2026-06-02-yarntwist-connected-spools-design.md`).
      Layer-2 only → **no UPM tag bump** (manifest stays `#v0.5.20`).
- [x] **Rollout — game-repo sync SHIPPED (2026-06-02).** YarnTwist game (`itay-main`, commit `d711bcb`,
      repo `hoppa-cloppa/YAT---Yarn-Twist`) pushed. 9 files mirrored under `Assets/_YAT/Scripts/`:
      `Runtime/YarnTopSectionData.cs`, `Editor/TopSection/YarnTopSectionPanel.cs` + new
      `Editor/TopSection/YarnSpoolConnection.cs`, `Editor/YarnMasterLevelExporter.cs`,
      `Editor/Analysis/YarnTwistLevelAnalyzer.cs`, `Editor/Analysis/YarnTwistSpoolAutofiller.cs`, new
      `Editor/Validation/YarnConnectedSpoolRule.cs` (+metas, reusing editor-core GUIDs). Pre-sync divergence
      was only a missing comment in the autofiller (applied my hunk, didn't overwrite). Rule `.asset` NOT
      created / NOT wired into game `GameProfile` — deferred, mirroring `YarnConnectedBoxRule`. Manifest pin
      unchanged (`#v0.5.20`). **User to confirm the game compiles in Unity 2022.3** (agent can't compile-verify).

### Mechanic 3 — Palette (2026-06-02) — SHIPPED; exporter follow-up patched 2026-06-03

Spec: `docs/superpowers/specs/2026-06-02-yarntwist-palette-design.md`.

**What it is:** a 3×3 cover over boxes that hides them in-game and reveals them after the player
opens enough boxes elsewhere (a countdown). Editor authors it; gameplay is Eliran's parallel task.

**Exporter follow-up (2026-06-03):** `PaletteAmount` was missing from export + `ExtraFeatureBottomType`
was emitted before `ColorType`/`Hidden`. Fixed in editor-core `d57beff` + game-repo `f9a52c8`.
Layer-2 only — manifest stays `#v0.5.21`.

**Game schema:** only the stub exists — `YATBottomType.Palette = 7` + `BottomConfig.ExtraFeatureBottomType`.
**User decisions:** no ID (center = identity); covered always 3×3 (derived); amount editable+stored
editor-side; `PaletteAmount` exported; export marks **center box only** with
`ExtraFeatureBottomType="Palette"`; placement via **right-click context actions** (not a button);
analyzer/difficulty **deferred**.

**Layer 1 (UPM → tag `v0.5.21`):** new `CanvasOverlayAsset` (abstract SO) + `GameProfile.CanvasOverlay`;
`GridCanvasPanel` calls `CanvasOverlay?.DrawOverlay(session, cellRect)` after cells (inside scroll view).
Additive/optional → YAK/YarnKingdom unaffected. `.cs` guid `f4a1c2e3b5d60718293a4b5c6d7e8f10`.
package.json bumped 0.1.0→0.5.21 + CHANGELOG entry.

**Layer 2 (YarnTwist):**
- [x] **Data** — `YarnPalettes` (guid `a5b6c7d8e9f0a1b2c3d4e5f60718293a`): palettes in
      `GameData["palettes"]` (`[{center{x,y},amount}]`); `All/Write/CanPlace/CoveredCells/TryPaletteAt/
      Add/Remove/SetAmount/IsBox`. "Box" = `yt.box`/`yt.arrowbox`.
- [x] **Authoring** — `YarnBoxCellDefinition.GetContextActions`: Add Palette (3×3 here) when `CanPlace`;
      on a covered box, Set Palette Requirement (inline IntField via `drawOptions`) + Remove Palette.
- [x] **Overlay** — `YarnPaletteOverlay : CanvasOverlayAsset` (.cs guid `b6c7d8e9f0a1b2c3d4e5f60718293a4b`,
      .asset guid `c7d8e9f0a1b2c3d4e5f60718293a4b5c`): red 3×3 outline + tint + amount badge. Wired into
      `YarnTwistProfile._canvasOverlay`.
- [x] **Validation** — `YarnPaletteRule` (Scope=Level; .cs guid `d8e9f0a1b2c3d4e5f60718293a4b5c6d`,
      .asset guid `e9f0a1b2c3d4e5f60718293a4b5c6d7e`, `_id: yt.palette`): in-bounds / all-boxes / no-overlap.
      Wired into `_rules`.
- [x] **Export** — `YarnMasterLevelExporter.BuildBottomConfigs(grid, document)`: center cell gets
      `ExtraFeatureBottomType:"Palette"`. (Confirm center-vs-all-9 with Eliran when spawner lands.)
- [x] **Tests** — exporter (+2: center flagged / no-palette no key); new `YarnPaletteTests` (.meta
      `f0a1b2c3d4e5f60718293a4b5c6d7e8f`): helper (CanPlace valid/edge/non-box/overlap, CoveredCells,
      TryPaletteAt), authoring (add offered for valid center only, covered-box offers remove+requirement,
      add stores default 5, remove clears, add→undo GameData round-trip), validation (valid / non-box /
      overlap / off-grid).
- [x] **Tests verified** — ran EditMode via MCP (the unity-mcp 401 is resolved): 113 tests, 0 failures,
      all Palette tests green. Also fixed 2 **pre-existing stale** exporter reward tests
      (`Export_NewLevel_StubsRewardEntry`, renamed `Export_ExistingReward_IsPreserved` →
      `Export_AlwaysWritesReward_OverwritingExisting`) — they asserted old "preserve reward" behavior /
      depended on EditorPrefs; updated to the canonical always-write-reward behavior, deterministic via
      `GameData["coinReward"]`. Not Palette regressions.
- [x] **Rollout — SHIPPED (2026-06-02).** Layer-1 change → **UPM tag `v0.5.21`**. editor-core commit +
      tag + push to `origin`; game (`itay-main`) manifest pin `#v0.5.21` + Layer-2 mirror pushed.

---

### Awaiting verification — Spool auto-fill v2 + follow-ups (2026-05-31) — CODE COMPLETE, shipped as `v0.5.19`

Tagged & pushed (`v0.5.19`); still **pending the user's EditMode test run** (agent can't —
unity-mcp-cli 401). Summary kept for context:
- **Alloc-free DFS** + **Monte-Carlo WinRate** (hidden spools lower measured difficulty) in
  `YarnTwistLevelAnalyzer.cs`; **parallel sweep + win-rate-band acceptance + guided
  hill-climbing** in `YarnTwistSpoolAutofiller.cs`; raised caps + new knobs in
  `YarnTwistSpoolAutofillConfig`; AutofillPanel win-rate line.
- Follow-ups: **unsolvable red banner** in `AutofillPanel`; **exportable solution steps**
  (`RecordSolution` → `SolutionSteps`, "Save Solution…" button writing `<levelId>.solution.txt`).
- Watch points if a test fails: (1) `Span`/`stackalloc` in target Unity; (2) noise-tolerant
  `hidden ≤ visible + 0.05` assertion; (3) probabilistic fixtures (`CapacityOverride`,
  `DifficultyOne_HasHigherAveragePathCount`) under the candidate RNG.

> **Connected Boxes touches the same analyzer/autofiller files** — when implementing it,
> fold its changes in carefully so as not to disturb the v0.5.19 alloc-free hot path.

---

### Parked — Per-level Difficulty Type — YarnTwist (2026-05-28) — FEATURE DONE (game-project editor blocker since resolved, see below)

Small Layer-2 feature. The game's `LevelConfig` gained `YATLevelType LevelType`
(`None` / `Hard` / `SuperHard`). Editor now lets the designer pick a difficulty
per level via a dropdown in the Summary panel (second row, under Coins), stored
as `doc.GameData["levelType"]` and exported as `LevelConfigs[key].LevelType`
(enum-name string, matching how `Direction` is written).

Feature status: **complete, committed, pushed, deployed.**
- Spec: `docs/superpowers/specs/2026-05-28-yarntwist-level-difficulty-type-design.md`
- Code: `Assets/YarnTwist/Editor/YarnMasterLevelExporter.cs` (`ExtraSummaryRowCount`
  1→2, dropdown in `DrawExtraSummaryRows`, `LevelType` in `Export`).
- Tests: `Assets/YarnTwist/Tests/Editor/YarnMasterLevelExporterTests.cs` (+2;
  user confirmed they pass).
- editor-core commit `e9136c1` pushed to `origin/master`; game repo commit
  `5fb4707` pushed to `origin/itay-main`. Layer-2 only → no UPM tag bump.
- **Verified working in editor-core** (Unity 6): dropdown shows "None" under
  Coins in the Summary panel.

### RESOLVED — Level Editor in the YarnTwist GAME project works fine (user-confirmed 2026-06-02)

The "blocker" below is **no longer reproducing**. User confirms the Level Editor
opens and works correctly in the YarnTwist game project. Most likely fixed by the
`v0.5.20` rollout (manifest pin bump from `#v0.5.18` → `#v0.5.20` + the Layer-2 sync
that came with Connected Boxes, which the user verified compiles clean in Unity
2022.3). Do not chase this next session — kept here only as historical context.

<details><summary>Original blocker writeup (historical — left for context)</summary>

Was: opening any level NRE'd in `LevelEditorWindow.AutoActivateCellType()` at
`LevelEditorWindow.cs:534` (`def.CreateDefault()` where `def = _profile.CellTypes[1]`
was **null**), then an `EndLayoutGroup`/`Stack empty` IMGUI cascade; palette empty,
Summary showed no Coins/Difficulty rows. Game was then pinned at `#v0.5.18`. Disk
checks (cell-def GUIDs, `m_Script` bindings, no duplicate types) all came back clean;
adding two game-side asmdefs (`Hoppa.YarnTwist.Runtime`/`Editor`) did not fix it at
the time. The version bump to v0.5.20 appears to have resolved it.

</details>

---

**Spool auto-fill + win-path analysis — YarnTwist v1 (2026-05-26)**

New authoring flow on top of the editor: the designer paints the grid by
hand, then the Spool Analysis side panel auto-completes the top section
and reports exact win-path count so she has a concrete difficulty signal.

Architecture (Layer 1): generic `ILevelAnalyzer` + `ILevelCompleter`
contracts under `Packages/.../Editor/Analysis/`; `GameProfile` gains two
typed fields (`_levelAnalyzer`, `_levelCompleter`) and a generic
`_extensions: List<ScriptableObject>` slot for future per-profile data.
`AutofillPanel` IMGUI side panel shown in the right column when an
analyzer is wired.

Layer 2 (YarnTwist): `YarnTwistLevelAnalyzer` (memoized DFS over tap
orderings; Box / ArrowBox / Tunnel rules; multiset-on-belt
abstraction; configurable conveyor capacity), `YarnTwistSpoolAutofiller`
(reroll loop targeting a Difficulty-keyed win-path band),
`YarnTwistSpoolAutofillConfig` (curves + caps + timeouts owned by the
autofiller asset, NOT by the profile).

Tests: `YarnTwistLevelAnalyzerTests` (9 fixtures) +
`YarnTwistSpoolAutofillerTests` (7 fixtures), all green.

Manual verification (must run in Unity) — see "Manual verification" below.

---

**Level generator framework — YarnTwist v1 (2026-05-25)**

New initiative on top of the existing editor: a parameter-driven level
generator that produces candidate `LevelDocument`s the operator previews,
then hands off to the normal save/export flow. Architecture is Layer 1
(generic generator framework — `ILevelGenerator`, `LevelGeneratorRequest/
Result`, `LevelGeneratorRunner`, `GeneratorModePanel`, abstract
`LevelGeneratorAsset` base, `GameProfile._levelGenerator` + `_generatorConfig`
fields, ✨ Generate toolbar button) + Layer 2 YarnTwist implementation
(`YarnTwistLevelGenerator`, `YarnTwistGeneratorConfig` with AnimationCurve
knobs driven by Difficulty). v1 ships **YarnTwist only**; YAK gets its own
generator later. Reuses the profile's existing validation rules as the
sanity gate — no new playability-check interface.

This unparks YarnTwist for generator work. The prior "YAK only focus"
memory (`project_yak_only_focus.md`) is now scoped: it still applies to
non-generator YarnTwist work (existing tests, refactors), but generator
work is YarnTwist-first by user direction (2026-05-25). YAK Layer 2
onboarding (below, "Added 2026-05-18") remains in flight in parallel.

**YAK Layer 2 onboarding (2026-05-18)** — still in progress, manual
verification pending (see Open items).

YarnTwist's editor work was previously complete at tag `v0.5.14`. The
framework supports multi-profile coexistence
(`LevelEditorWindow.DrawProfileSelector`); YAK lives alongside YarnTwist
at `Assets/YAK/`. Switching games = drag a different `GameProfile` asset
into the editor window field.

### Added this session (2026-05-18)

**YAK Layer 2 — initial scaffold (`Assets/YAK/`)**

Runtime (`Hoppa.YAK.Runtime`):
- `YAKEmptyCell` (`ICellData`, `yak.empty`) — no-wool sentinel.
- `YAKWoolCell` (`IColoredCell`, `yak.wool`) — holds `ColorId`.
- `YAKSpoolEntry` (color + capacity + hidden), `YAKSpoolColumn`, `YAKTopSectionData`.

Editor (`Hoppa.YAK.Editor`):
- `YAKEmptyCellDefinition` — checker pattern in canvas, empty inspector.
- `YAKWoolCellDefinition` — palette-coloured fill, `ColorSwatchDrawer` in inspector.
- `YAKSpoolSectionPanel` — adapted from `YarnTopSectionPanel`. Variable column
  count (2–5) via `+ / −` header buttons; per-spool capacity int field next to
  the color swatch; per-spool `Hidden` toggle; drag-reorder + per-row delete +
  cross-column move + column swap (same UX as Yarn).
- `YAKColorBalanceRule` — counts wool tiles per color vs spool capacity sum per
  color (including hidden); Info row when balanced, Error when not.
- `YAKLevelExporter` — writes one YAK `LevelConfig` JSON per level. Color values
  serialised as ints via `YAKColorMapping`. `PixelColors` is a flat int array
  of length `Width*Height` in row-major bottom-up (`index = y*Width + x`), with
  `0` = empty cell. Configurable `_outputDir` field on the asset (left empty
  until the game repo is connected). Adds Columns / Spools / Wool to the
  Summary panel, plus a Conveyor int field stored in `doc.GameData["conveyorCount"]`.
- `YAKLevelImporter` — parses a YAK LevelConfig JSON back into a `LevelDocument`
  using reverse-lookup on `YAKColorMapping`. Validates `PixelColors.Length == Width*Height`.
- `YAKImportMenu` — `Tools › Hoppa Level Editor › Import YAK Level…`. Picks a
  source YAK JSON, asks where to save the editor's working file, writes the
  `LevelDocument` JSON, and auto-opens it in the Level Editor window.

Framework (Layer 1) additions:
- `NewLevelDialog` (modal) — pops on **New Level** with Width/Height fields
  pre-filled from profile defaults. Lets YAK levels start at any size (30×30,
  40×40, …) without changing the profile.
- `LevelEditorWindow.Profile` — public getter so Layer 2 tooling can read the
  active profile.
- `LevelEditorWindow.OpenLevelFile(path)` — public hook used by `YAKImportMenu`
  to auto-load the freshly imported file.
- `LevelEditorSession.CreateEmpty(profile, width, height)` — overload accepting
  per-level dimensions.

Data assets (`Assets/YAK/Data/Config/`):
- `YAKProfile.asset` — schema `yak`, default grid 30×30, wires empty+wool cell
  defs, color balance rule, exporter, and the spool section panel script. No
  order panel (one file per level, no master config to renumber).
- `YAKPalette.asset` — 13 colors copied from `YarnTwistPalette` as starting set.
- `YAKEmptyCellDef.asset`, `YAKWoolCellDef.asset`, `YAKColorBalanceRule.asset`.
- `YAKColorMapping.asset` — 13 entries (`blue→1`, `cyan→2`, …, `purplebright→13`).
  Reserves `0` for empty cells.
- `YAKLevelExporter.asset` — color mapping wired; output dir empty pending game repo.

---

## Open items / known gaps

### Manual verification — spool auto-fill (must run in Unity)

- [ ] Open Level Editor, select `YarnTwistProfile`; confirm the Spool
      Analysis panel appears in the right column (Validation 40% /
      Summary 30% / Analysis 30%).
- [ ] Open `Assets/YarnTwist/Data/Levels/YT_001.json`; press Analyze;
      confirm win-path count + elapsed time appear in the result block.
- [ ] Paint a small 3-box grid; press Auto-fill; confirm the top section
      populates with spool columns satisfying YarnColorBalanceRule.
- [ ] Sweep Difficulty 1 / 5 / 10; confirm the auto-filled hidden-spool
      count visibly tracks Difficulty (D=1: 0 hidden; D=10: ~40% hidden).
- [ ] Lock seed 🔒 + press Auto-fill twice; confirm identical top section.
- [ ] Press Ctrl-Z after Auto-fill; confirm prior top section restored.
- [ ] Switch active profile to `YAKProfile`; confirm Spool Analysis panel
      disappears (YAK has no analyzer wired — regression check).

### Generator framework — added this session (2026-05-25)

**Layer 1 (`Packages/com.hoppa.leveleditor.core/Editor/Generator/`):**
- `ILevelGenerator` — contract; one method `Generate(request, profile)`.
- `LevelGeneratorAsset` — abstract `ScriptableObject, ILevelGenerator` base
  (mirrors `LevelExporterAsset` / `EditorPanelAsset` pattern). Profiles
  serialize the field as this type so the inspector type-filters.
- `LevelGeneratorRequest` — `Difficulty` (1–10), `TargetAPS?`, `Seed`
  (0 = random), `AdvancedConfig` (ScriptableObject blob the game uses).
- `LevelGeneratorResult` — `Document`, `Succeeded`, `SeedUsed`,
  `CandidatesTried`, `RuleRejectCounts`, `ElapsedMs`. `ToString()` formats
  the diagnostics line.
- `LevelGeneratorRunner.Evaluate(doc, profile)` — runs `profile.Rules`
  against a candidate and returns the report + per-rule Error counts.
  Layer 2 generators own the reroll loop; this helper exists so every
  game produces consistent rejection diagnostics.
- `GeneratorModePanel` — IMGUI panel; params header (Difficulty slider,
  Target APS, Seed + 🎲 + 🔒) + Advanced foldout rendering
  `Editor.CreateEditor(profile.GeneratorConfig).OnInspectorGUI()` for
  game-specific tuning, + preview using `GridCanvasPanel` and the
  profile's top-section panel, + Regenerate / Use This Level / diagnostics.
  "Use This Level" hands off to `LevelEditorWindow` via `OnUseLevel` event;
  the document loads as unsaved-new in normal edit view.

**Layer 1 modifications:**
- `GameProfile` — two new optional fields: `_levelGenerator`
  (`LevelGeneratorAsset`) + `_generatorConfig` (`ScriptableObject`).
- `ToolbarPanel` — `OnGenerateToggle` event, `GenerateMode` + `ShowGenerate`
  flags, ✨ Generate toggle button (visible only when profile has a generator).
- `LevelEditorWindow` — `_inGeneratorMode` state, `_generator` panel,
  `HandleGenerateToggle` (exits Order mode if active), `HandleGeneratorUseLevel`
  (calls existing document-load path with the candidate doc).

**Layer 2 — YarnTwist (`Assets/YarnTwist/Editor/Generator/`):**
- `YarnTwistGeneratorConfig` — `ScriptableObject` with 9 `AnimationCurve`
  knobs (GridWidth, GridHeight, WallDensity, BoxRatio, ArrowBoxRatio,
  TunnelCount, ColorCount, HiddenSpoolRatio, CoinReward), `MaxRerollAttempts`
  (50), `MaxTunnelQueueLength` (3), and 3 overrides (GridWidthOverride,
  GridHeightOverride, ColorCountOverride; 0 = use curve). `OnEnable`
  defensively populates default linear curves if the asset YAML loads
  them empty.
- `YarnTwistLevelGenerator : LevelGeneratorAsset` — Option A from the
  design: layout-first (walls → tunnels → boxes/arrowboxes), then
  derive spool distribution from per-color grid totals (each colored grid
  cell = 9 balls = exactly 3 spools, so balance is exact by construction).
  Arrow-direction repair pass demotes unfixable arrows to plain boxes.
  Wraps each candidate in a `LevelGeneratorRunner.Evaluate` call and rerolls
  with a derived sub-seed on `Error`-severity rule failures, capped at
  `MaxRerollAttempts`.

**Assets written (with .meta GUIDs so teammates pick them up):**
- `Assets/YarnTwist/Data/Config/YarnTwistGeneratorConfig.asset`
- `Assets/YarnTwist/Data/Config/YarnTwistLevelGenerator.asset`
- `Assets/YarnTwist/Data/Config/YarnTwistProfile.asset` updated with
  `_levelGenerator` + `_generatorConfig` references.

**Tests (`Assets/YarnTwist/Tests/Editor/YarnTwistLevelGeneratorTests.cs`):**
- Determinism: same seed + config → identical structural signature
  (timestamps/levelId stripped).
- Difficulty sweep (1, 3, 5, 8, 10) × parameterized seeds — each must
  produce a `Succeeded == true` document that re-passes
  `LevelGeneratorRunner.Evaluate`.
- `GridWidthOverride` propagates to output `Grid.Width`.
- Diagnostics fields populated (`SeedUsed`, `CandidatesTried`, `ElapsedMs`,
  `RuleRejectCounts`).
- `TargetAPS` recorded on `Metadata.Aps`.

### Manual verification still pending (must run in Unity)

**Generator framework:**
- [ ] Recompile scripts; confirm no compile errors in
      `Hoppa.LevelEditor.Core.Editor` / `Hoppa.YarnTwist.Editor` /
      `Hoppa.YarnTwist.Editor.Tests`.
- [ ] Run `YarnTwistLevelGeneratorTests` from the Test Runner (Edit Mode);
      confirm all pass.
- [ ] Open Level Editor window, select `YarnTwistProfile`; confirm
      ✨ Generate button appears in toolbar.
- [ ] Click Generate; confirm generator mode renders params panel +
      preview canvas + top-section preview.
- [ ] Sweep Difficulty 1 / 5 / 10; click Regenerate a few times per
      Difficulty; confirm grids vary and the diagnostics line populates
      (e.g. `Generated in 14 ms · 3 candidate(s) · seed 1745032`).
- [ ] Lock a seed (🔒) and regenerate twice; confirm preview is identical.
- [ ] Expand Advanced; set `GridWidthOverride = 8`; regenerate; confirm
      the preview grid is 8 wide.
- [ ] Click Use This Level → confirm exit to normal edit view with
      unsaved-modified indicator, the generated grid loaded in the canvas,
      spool columns rendering in the top section.
- [ ] Save As `Assets/YarnTwist/Data/Levels/YT_GEN_001.json`, then Export.
      Confirm `YarnMasterLevelExporter` produces a new entry in the master
      `level_config.json`.
- [ ] Open ⇅ Order panel; confirm the new level appears in the list
      with a key.
- [ ] **Regression** — switch profile back to `YAKProfile`; confirm
      ✨ Generate button is hidden (YAK has no generator wired yet).

**YAK Layer 2 (carried over from 2026-05-18):**
- [ ] Recompile scripts; confirm no compile errors in `Hoppa.YAK.Runtime` /
      `Hoppa.YAK.Editor` / `Hoppa.LevelEditor.Core.Editor`.
- [ ] Switch active profile to `YAKProfile` in `LevelEditorWindow`; confirm
      grid renders, palette shows Empty + Wool, spool section panel renders
      with two empty columns.
- [ ] Click **New Level** → confirm the size dialog pops and 30×30 default is
      pre-filled; create a level and paint a few wool cells.
- [ ] Add spool columns / spools / capacities; confirm the Color Balance rule
      shows one row per color and turns Error on mismatch.
- [ ] Save level (LevelDocument JSON). Open it again via Open → round-trip works.
- [ ] Set `_outputDir` on `YAKLevelExporter.asset` to a real path; click Export;
      confirm `level_NNN.json` is written with int color values + correct
      `Width`/`Height`/`ConveyorCount`/`SpoolColumnConfigs`/`PixelColors`.
- [ ] **Regression** — switch profile back to `YarnTwistProfile`; confirm Yarn
      flows (open existing `YT_*.json`, edit, save, export) still work.
- [ ] **Importer** — once Eliran shares his `level_001.json`, run
      `Tools › Hoppa Level Editor › Import YAK Level…` and round-trip it.

### Decisions still pending input
- [ ] Game repo output path for `YAKLevelExporter._outputDir`
- [ ] Confirm `YAKColorType` int values once the game's enum is settled (current
      mapping copied from YarnTwist's order: blue=1 … purplebright=13).
- [ ] Whether YAK needs an Order panel later (currently filesystem-driven order;
      can add later if a master config is introduced).

---

## Done (completed phases)

| Phase | Status |
|-------|--------|
| Planning | ✅ Complete |
| Phase 0 — Package skeleton | ✅ Complete |
| Phase 1 — Core data + serialization | ✅ Complete |
| Phase 2 — Validation engine | ✅ Complete |
| Phase 3 — EditorWindow + grid canvas | ✅ Complete |
| Phase 4 — Top-section abstraction + export | ✅ Complete |
| Phase 4.5 — UI/UX polish | ✅ Complete |
| Phase 5 — Data pipeline integration | ✅ Complete |
| YarnTwist Jira UI gaps | ✅ Complete |
| YarnTwist brush template + color swatches | ✅ Complete |
| YarnTwist export key fix + Level Order Manager | ✅ Complete |
| YarnTwist save/open directory memory | ✅ Complete |
| YarnTwist spool columns drag-reorder | ✅ Complete |
| YarnTwist multi-select + profile persistence | ✅ Complete |
| YarnTwist summary panel enhancements + coin reward (v0.5.14) | ✅ Complete |
| Phase 6 — Second-game onboarding | 🔵 **In progress (YAK)** |

---

## Key design notes (carry forward)

- `GridData` is bottomUp: y=0 = bottom row in data, drawn at bottom of canvas.
  YAK's `PixelColors` follows the same convention so export needs no inversion.
- YAK reserves `0` in its color mapping for the empty/no-wool sentinel; wool
  colors start at `1`.
- First cell type in `GameProfile.CellTypes` must be the "empty" type (erase + fill).
- `PushUndoSnapshot()` must be called BEFORE mutations (not after).
- `CloneBrushTemplate()` is the correct way to get a fresh painted cell — never
  store `BrushTemplate` reference directly in the grid.
- `DrawInspector` must use absolute `GUI.*` / `EditorGUI.*` calls — never
  `GUILayout.BeginArea` (causes render leaks outside panel bounds).
- `LevelEditorSession.MarkDirty()` is the safe way to flag unsaved state from panels.
- Game switching = drag a different `GameProfile` asset into the
  `LevelEditorWindow`'s profile selector. The selector remembers the choice
  per project via `EditorPrefs` (`Hoppa.LevelEditor.ProfileGuid`).
- Every new `.cs` file in the package or under `Assets/` **must** have a
  committed `.meta` file with a stable GUID, or Unity silently excludes it from
  compilation in package consumers.
- Layer 1 UPM changes deploy via git tag + consumer `manifest.json` bump
  (currently `v0.5.16`). Tag history:
  - `v0.5.15` — `NewLevelDialog`, public `Profile`/`OpenLevelFile`,
    `CreateEmpty(profile, w, h)` overload, framework bottom-section support,
    YAK single-source color refactor.
  - `v0.5.16` — `GridCanvasPanel` mouse hit-test now follows the scroll
    offset; previously, once the canvas scrolled (e.g. when the YAK bottom
    spool panel shrunk the canvas below the grid's required height), the
    bottom `_scroll.y` pixels of the visible viewport silently rejected
    clicks. Layer 2: `YAKProfile.asset` rewired from the legacy
    `YAKPalette.asset` to `YAKStaticManagerColorSource.asset` (single
    source of truth per `design_yak_colors_single_source.md`) — fixes the
    empty color picker popup when right-clicking spool swatches.
