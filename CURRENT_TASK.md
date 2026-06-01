# Current Task

> Live state of in-flight work. Update after every meaningful chunk so a fresh
> session can resume cold. Companion to `PLANNING.md` (spec) and
> `SESSION_NOTES.md` (longer-lived context).

---

## Active phase

**New game mechanics — week of 2026-06-01 — 3 mechanics, one per spec.**
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
- [ ] **Rollout (awaiting user)** — needs: (1) **user runs EditMode tests in Unity** (agent
      can't — mcp 401); (2) **user approval** for the outward-facing steps: tag `v0.5.20`, bump
      YarnTwist `manifest.json` pin to `#v0.5.20`. NOTE: YAK sync is NOT needed (YarnKingdom does
      not implement `ICellContextActions`). Also decide on the unrelated staged NuGet `.dll`
      deletions before committing.
      Watch-if-tests-fail: (a) hand-written `.meta` GUIDs for `CellActionContext`,
      `YarnConnectedBoxRule` (.cs+.asset), `YarnConnectedBoxTests` must be unique & importable;
      (b) the `CellContextAction` Func/Action ctor overload resolves by the named `create:`/
      `apply:` argument at every call site.

### Mechanic 2 — TBD (pull from game project when starting)
### Mechanic 3 — TBD (pull from game project when starting)

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

### Parked — Per-level Difficulty Type — YarnTwist (2026-05-28) — FEATURE DONE; game-project editor BLOCKED

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

### BLOCKER — Level Editor broken in the YarnTwist GAME project (`E:/Projects/Hoppa/YarnTwist`)

Pre-existing, **not caused by the feature**. Opening any level NREs and the
Difficulty dropdown never renders.

- Symptom: `NullReferenceException` in package code
  `LevelEditorWindow.AutoActivateCellType()` at `LevelEditorWindow.cs:534`
  (`def.CreateDefault()` where `def = _profile.CellTypes[1]` is **null**), then
  `EndLayoutGroup`/`Stack empty` IMGUI cascade. Palette is empty; Summary shows
  no Coins/Difficulty rows. Root: the profile's cell-type definitions resolve
  to **null** (missing-script binding) at runtime.
- Environment: game project is **Unity 2022.3.62f2**; editor-core is Unity 6.
  Game consumes package via git UPM pinned `#v0.5.18` (= commit `bc7477a`,
  confirmed by `git tag --contains`). Lock hash matches. Package is NOT stale.

What was VERIFIED on disk (all correct — none is the cause):
- Profile `_cellTypes`/`_exporters` GUIDs all resolve to existing assets.
- Cell-def `.asset` `m_Script` GUIDs match their `.cs.meta` GUIDs.
- v0.5.17 sync (`3197cef`) did NOT touch cell-def assets — only added
  generator/analyzer fields to the profile. Cell-def scripts/metas stable
  since original import.
- No duplicate `YarnWallCellDefinition` type; no duplicate Layer-1 framework
  copy in `Assets`.

What was TRIED (none fixed it):
- **Added 2 asmdefs to the game project** (NEW, uncommitted, unverified-as-fix):
  `Assets/_YAT/Scripts/Runtime/Hoppa.YarnTwist.Runtime.asmdef` and
  `Assets/_YAT/Scripts/Editor/Hoppa.YarnTwist.Editor.asmdef`. Rationale: the
  cell-def assets record `m_EditorClassIdentifier: Hoppa.YarnTwist.Editor::…`
  but the game had NO such assembly (scripts compiled into
  `Assembly-CSharp-Editor`). After adding them, `Hoppa.YarnTwist.Editor.dll`
  builds and contains `YarnWallCellDefinition` (verified) — but the NRE
  PERSISTS. **Consider reverting these two files if they turn out irrelevant.**
- Reimport All, folder Reimport, full Unity restart — NRE persists.
- NOTE: the game has NEVER had a `Hoppa.YarnTwist.Editor` asmdef in git history
  (only `YAT.Editor`/`YAT.Gamelogic`, added at init). User says the editor
  "worked before with a simple version bump" — so binding worked previously
  with types in `Assembly-CSharp-Editor`. That contradicts the assembly-name
  theory and is unresolved.

### RESUME HERE (next session)
The one decisive datapoint never gathered: **in the game Unity, click
`Assets/_YAT/LevelEditor/Config/CellDefs/YarnWallCellDef.asset` and read the
Inspector.**
- If it shows "script can not be loaded" / Script = `None` → asset genuinely
  can't bind (re-assign the script / re-create the asset; the GUID link is the
  issue regardless of YAML).
- If it shows normal fields (Script=`YarnWallCellDefinition`, `yt.wall`, `Wall`)
  → cell-defs are fine; the null is in the **profile's** `_cellTypes` reference
  list → re-assign cell types on `YarnTwistProfile.asset`.
This single answer splits the problem; pick the matching fix. Also worth a
fresh look from the "package version bump" angle the user raised.

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
