# Session Notes — Studio Level Editor

> Read this first when resuming work. Companion to `PLANNING.md` (the
> authoritative spec) and `CURRENT_TASK.md` (what's actively in flight).

---

## Current status (as of 2026-06-15)

- **WHERE WE LEFT OFF (2026-06-15):** the **Automated level-generation tooling for YAK**
  initiative (5 phases A–E) is **code-complete, all unit-verified, committed & tagged
  `v0.6.0`, and PUSHED** — but **nothing has been hand-tested in the editor / in-game by a
  human yet**. That verification is the first job tomorrow. Spec: `docs/level-tooling-megaprompt.md`;
  plan: `~/.claude/plans/no-thanks-i-want-velvety-frost.md`; full resume detail + the
  TOMORROW checklist in `CURRENT_TASK.md` → "ACTIVE INITIATIVE". Built generically in Layer 1
  (package → **v0.6.0**) with YAK specifics in `Assets/YAK/`:
  - **A** `YAKLevelAnalyzer` (engine-agnostic simulator + solver + average-player **measured APS**;
    rollout-rescue makes 30×30 solvability work), **B** `YAKSpoolAutofiller` (analyzer-gated,
    balance-by-construction), **C** image→grid (`IImageToGrid` + 🖼 Image toolbar mode +
    `YAKImageToGrid`), **D** `solution.json` export + replay (+ unverified game viewer
    `YAKSolutionViewer.cs` in YarnKingdom), **E** `YAKLevelGenerator` + `YAKBatchHarness` +
    generic `BatchReviewWindow`.
  - **Full EditMode suite 160/160 green; real batch smoke kept 2/2.**
  - **Deferred (by design):** image-gen API (batch image step), APS calibration (needs 10 real
    levels + player data; `YakAveragePlayer.Calibrate` is ready), in-Unity verify of the game viewer.
- **PRIOR (2026-06-03) — Save Solution / belt model (YarnTwist):** DONE/verified — level_044
  play-tested winning 2026-06-04. Detail retained in `CURRENT_TASK.md`. See memories
  `design_yarntw_belt_drain_order` (RESOLVED) + `design_yarntw_box_unlock`.
- **Project**: `hoppa-level-editor-core` — standalone Unity 2022.3 project hosting the UPM package `com.hoppa.leveleditor.core`.
- **Active branch**: `master` — YarnTwist editor was complete at tag `v0.5.14`, then **unparked for the level-generator initiative (v1 ships YarnTwist-only, 2026-05-25)**. YAK Layer 2 onboarding still in progress in parallel — files added in `Assets/YAK/` plus small additive Layer 1 changes (`NewLevelDialog`, public `Profile`/`OpenLevelFile`). The next Layer 1 tag bumps for both the v0.5.15/16 changes that already shipped and the new generator framework added 2026-05-25.
- **Multi-game project**: both YarnTwist and YAK Layer 2s coexist in this same Unity project. The framework's profile selector (`LevelEditorWindow.DrawProfileSelector`) is the switcher.
- **Level generator framework (2026-05-25)**: parameter-driven generator added on top of the editor. Layer 1 owns the generic shell (`ILevelGenerator`, `LevelGeneratorAsset`, `LevelGeneratorRequest/Result`, `LevelGeneratorRunner`, `GeneratorModePanel`, `✨ Generate` toolbar button); Layer 2 ships per-game tuning + algorithm. YarnTwist's implementation (`YarnTwistLevelGenerator` + `YarnTwistGeneratorConfig`) is wired and passing edit-mode tests.
- **Spool auto-fill + win-path analysis (2026-05-26)**: side panel + Layer 1
  `ILevelAnalyzer` + `ILevelCompleter` contracts; `YarnTwistLevelAnalyzer`
  (memoized DFS counting win paths) + `YarnTwistSpoolAutofiller` (reroll
  loop targeting a Difficulty-keyed win-path band) wired and passing
  edit-mode tests. `GameProfile` gains an `_extensions` slot for future
  per-profile data (Layer 1 stays generic — no game-specific vocabulary).
- **Per-level difficulty type (2026-05-28)**: YarnTwist Layer-2 only. A
  Difficulty dropdown (None/Hard/SuperHard) in the Summary panel, stored per
  level in `gameData.levelType` and exported as `LevelConfig.LevelType`
  (enum-name string). Mirrors the `coinReward` pattern. No Layer 1 / UPM change,
  so no tag bump. Synced to YarnTwist (`Assets/_YAT/Scripts/Editor/`).
- **New game mechanics — week of 2026-06-01 (in flight)**: 3 new YarnTwist
  mechanics, one spec each. **Source-of-truth rule:** the GAME project
  (`E:/Projects/Hoppa/YarnTwist`, branch `itay-main`) defines the Layer-2 data
  structure — Eliran implements game-side first, we mirror his `level_config.json`
  shape exactly (no invented editor schema). Per-mechanic workflow: read-only agent
  into the game project → find `BottomConfig`/enum/runtime classes + sample config →
  adopt identifiers → brainstorm editor side → spec. Recurring editor extension
  points: analyzer `BuildItems`/`ApplyTap`, exporter `BuildBottomConfigs`; the
  autofiller stays correct-by-delegation. See memories
  `feedback_eliran_game_code_is_schema_source_of_truth` +
  `reference_connected_boxes_game_schema`.
  - **Mechanic 1 — Connected Boxes** (design approved, not yet implemented):
    two adjacent regular boxes linked; tap either → both clear together; colors
    independent. Game stores it as a `BottomConfig` with `BottomType=6`
    (ConnectedBox) + reciprocal PascalCase `Direction` string (NO connection-id
    object). Editor: `yt.box` gains nullable `ConnectedDir`; right-click
    Connect/Un-connect (needs a Layer-1 context-action extension → tag `v0.5.20`);
    white outline; `YarnConnectedBoxRule` validates reciprocity; export branches to
    `BottomType:6`; analyzer gets a `Partner[]` atomic co-tap. Spec:
    `docs/superpowers/specs/2026-06-01-yarntwist-connected-boxes-design.md`. Live
    checklist in `CURRENT_TASK.md`.
  - **Mechanics 2 & 3 — TBD**: pull from the game project when starting each.

---

## Game profiles in this project

| Profile | Path | Purpose |
|---|---|---|
| `YarnTwistProfile.asset` | `Assets/YarnTwist/Data/Config/` | Yarn Twist (puzzle: spool→box matching, walls, tunnels, arrow boxes). Schema `yarn-twist`. |
| `YAKProfile.asset` | `Assets/YAK/Data/Config/` | YAK (pixel-art wool grid + vertical spool queue + conveyor). Schema `yak`. |
| `DemoProfile.asset` | `Assets/Samples/Hoppa Level Editor Core/0.1.0/DemoColorGridGame/Data/` | Sample / minimal-game reference. Schema `demo`. |

**To switch games**: open `Window ▸ Level Editor`. If a level is loaded, click New or Open to bring up the profile selector; drag a different `GameProfile.asset` into the Game Profile field. The choice persists across sessions via `EditorPrefs` key `Hoppa.LevelEditor.ProfileGuid`. Layer 2 assets do not interfere with each other — different namespaces, asmdefs, and cell-type prefixes (`yt.*` vs `yak.*`).

---

## What exists

### Framework (`Packages/com.hoppa.leveleditor.core/`)

**Runtime assembly** (`Hoppa.LevelEditor.Core.Runtime`):
- `LevelDocument`, `LevelMetadata` (incl. `Notes` field), `GridData<T>`, `CellRef`
- `ICellData`, `ICellTypeRegistry`, `IColorPalette`, `ILevelSerializer`
- `JsonLevelSerializer` + `CellDataConverter` (polymorphic ICellData via "type" discriminator)
- `ValidationReport`, `ValidationEntry`, `ValidationSeverity` (Info/Warning/Error), `ValidationContext`
- `ValidationRuleBase`, `ValidationRuleRegistry`
- `ColorPaletteAsset`, `ColorEntry`

**Editor assembly** (`Hoppa.LevelEditor.Core.Editor`):
- `LevelEditorWindow` — 3-column IMGUI window (Toolbar | Canvas+TopSection | Validation+Inspector+Summary); toolbar has New/Open/Save/SaveAs/Export▸/Undo/Redo/⇅Order/Test. Save/Open dialogs remember last-used directory via `EditorPrefs` (`Hoppa.LevelEditor.LastSaveDir`). Order mode renders `GameProfile.OrderPanel` full-window.
- `LevelEditorSession` — session state: `Document`, `CellTypes`, `ActiveCellType`, `BrushTemplate`, `SelectedCell`, `MultiSelection` (HashSet), `IsDirty`, undo/redo stack, `CloneBrushTemplate()`
- `GameProfile` — ScriptableObject wiring cell types, validation rules, exporters, top section script, and optional `OrderPanel` (`EditorPanelAsset`)
- `EditorPanelAsset` — abstract `ScriptableObject` base implementing `IEditorPanel`; lets games expose panels as Inspector fields
- `CellTypeDefinition` / `ICellTypeDefinition` — abstract SO base; game subclasses implement `DrawCell` + `DrawInspector`
- `CellTypeRegistry` — maps TypeId ↔ concrete type ↔ definition
- `LevelExporterAsset` — abstract SO base for exporters
- `ScriptableObjectExporter` — produces `.asset` alongside `.json`
- `ColorPaletteAsset`, `ColorSwatchDrawer` — palette SO + reusable IMGUI swatch picker
- Panels: `PalettePanel` (cell list + BRUSH config), `GridCanvasPanel` (CTRL+click multi-select), `ToolbarPanel` (incl. `OnOrderToggle`/`OrderMode`), `ValidationPanel`, `CellInspectorPanel`, `SummaryPanel`, `MultiSelectPanel` (batch color/type for multi-selection)
- `TopSectionPanel` / `EmptyTopSectionPanel` — game-overridable top section
- `StringIntMapping` — reusable string→int ScriptableObject
- **Generator framework** (`Editor/Generator/`): `ILevelGenerator` (one-method contract), abstract `LevelGeneratorAsset : ScriptableObject, ILevelGenerator` base (inspector type-filters via this), `LevelGeneratorRequest` (Difficulty 1–10, optional TargetAPS, Seed, AdvancedConfig SO blob), `LevelGeneratorResult` (Document, Succeeded, SeedUsed, CandidatesTried, RuleRejectCounts, ElapsedMs), `LevelGeneratorRunner.Evaluate(doc, profile)` (runs `profile.Rules` and returns per-rule Error counts), `GeneratorModePanel` (IMGUI: params header + Advanced foldout via `Editor.CreateEditor(profile.GeneratorConfig).OnInspectorGUI()` + preview with `GridCanvasPanel` + the profile's top-section panel + Regenerate / Use This Level / diagnostics). `GameProfile` exposes `LevelGenerator` (optional `LevelGeneratorAsset`) and `GeneratorConfig` (optional `ScriptableObject`). `ToolbarPanel` shows ✨ Generate toggle only when the active profile has a generator. `LevelEditorWindow` handles `_inGeneratorMode` parallel to `_inOrderMode`; "Use This Level" hands the candidate to the existing document-load path as unsaved-new.
- **Analysis framework** (`Editor/Analysis/`): `ILevelAnalyzer` +
  `LevelAnalyzerAsset` (generic Analyze contract); `ILevelCompleter` +
  `LevelCompleterAsset` (generic Complete contract); `AnalysisRequest` /
  `LevelAnalysisResult` / `CompletionRequest` / `LevelCompletionResult`
  DTOs; `AutofillPanel` IMGUI side panel rendered in the right column
  when `profile.LevelAnalyzer != null`. `GameProfile` exposes
  `LevelAnalyzer` + `LevelCompleter` accessors and a generic
  `GetExtension<T>()` over `_extensions`.

### Yarn Twist game layer (`Assets/YarnTwist/`)

**Runtime** (`Hoppa.YarnTwist.Runtime`):
- Cell types: `YarnEmptyCell`, `YarnWallCell`, `YarnBoxCell`, `YarnArrowBoxCell`, `YarnTunnelCell`
- `YarnDirection` enum, `YarnTopSectionData`, `YarnSpoolColumn`, `YarnSpoolData`

**Editor** (`Hoppa.YarnTwist.Editor`):
- Cell definitions: `YarnEmptyCellDefinition`, `YarnWallCellDefinition`, `YarnBoxCellDefinition`, `YarnArrowBoxCellDefinition`, `YarnTunnelCellDefinition` (all use color swatches in `DrawInspector`)
- `YarnTopSectionPanel` — 4-column spool editor with drag-and-drop reorder, per-row delete, grid-filtered color picker, smart `+` default color, dynamic height
- Validation rules: `YarnColorBalanceRule` (emits Info table + Error for imbalances), `YarnArrowBoxTargetRule`, `YarnTunnelOutputRule`
- `YarnMasterLevelExporter` — transforms `LevelDocument` → `level_config.json` (game schema) on every Save and on the explicit Export ▸ button. On export, scans existing `LevelConfigs` for an entry matching the current `levelId` and updates it in-place (preserves the slot assigned by Apply Order). Only falls back to the filename-derived key (`level_005.json` → `"5"`) for brand-new levels. Exposes `OutputPath` getter for use by `YarnLevelOrderPanel`. Summary panel now
  has two editable rows — Coins (`gameData.coinReward`) and Difficulty
  (`gameData.levelType`); the latter is exported as `LevelConfig.LevelType`.
- `YarnLevelOrderPanel` — `EditorPanelAsset` shown in the ⇅ Order tab. Reads `level_config.json`, displays levels as a draggable `ReorderableList` with `#N` index labels, supports reordering (Apply Order) and per-level deletion (with confirmation dialog). Assigned to `YarnTwistProfile.asset`'s Order Panel field.
- `StringIntMapping` assets: `YarnColorMapping.asset` (10 colors), `YarnCellTypeMapping.asset` (5 types)
- **Generator** (`Editor/Generator/`): `YarnTwistGeneratorConfig` — `ScriptableObject` with 9 Difficulty-keyed `AnimationCurve` knobs (GridWidth/Height, WallDensity, BoxRatio, ArrowBoxRatio, TunnelCount, ColorCount, HiddenSpoolRatio, CoinReward) + `MaxRerollAttempts` / `MaxTunnelQueueLength` + 3 numeric overrides (GridWidth/Height/ColorCount; 0 = use curve). `OnEnable` defensively populates default linear curves. `YarnTwistLevelGenerator : LevelGeneratorAsset` — Option A from the design: layout-first placement (walls → tunnels with forced-empty neighbor → boxes/arrowboxes → arrow-direction repair) then derive spool distribution from per-color grid totals (every colored grid item = 9 balls = exactly 3 spools, so YarnColorBalanceRule passes by construction). Reroll loop uses `LevelGeneratorRunner.Evaluate` against `profile.Rules`. Tests in `Assets/YarnTwist/Tests/Editor/YarnTwistLevelGeneratorTests.cs` (determinism, Difficulty sweep 1/3/5/8/10, override propagation, diagnostics, APS recording) — all green.
- **Analysis** (`Editor/Analysis/`): `YarnTwistLevelAnalyzer` (memoized
  DFS counting win paths; Box / ArrowBox / Tunnel rules; multiset-on-
  belt abstraction). `YarnTwistSpoolAutofiller` (reroll loop targeting
  `WinPathTargetByDifficulty` ± `WinPathTolerance`; round-robin spool
  distribution across 4 columns; hidden ratio per Difficulty;
  best-so-far fallback when no candidate lands in band).
  `YarnTwistSpoolAutofillConfig` SO referenced from
  `YarnTwistSpoolAutofiller.asset` (NOT from `YarnTwistProfile.asset` —
  per the "Layer 1 stays generic" rule).

**Data assets** (`Assets/YarnTwist/Data/Config/`):
- `YarnTwistProfile.asset` — `GameProfile` wiring all 5 cell types + rules + exporter + generator (`_levelGenerator` → `YarnTwistLevelGenerator.asset`, `_generatorConfig` → `YarnTwistGeneratorConfig.asset`)
- `YarnTwistPalette.asset` — 6 colors (pink, blue, teal, green, yellow, purple) — **needs to exist; see manual steps**
- `YarnMasterLevelExporter.asset` — output path wired to `E:/Projects/Hoppa/YarnTwist/Assets/_YAT/Configs/Resources/Configs/level_config.json`
- `YarnTwistGeneratorConfig.asset` + `YarnTwistLevelGenerator.asset` — generator assets (2026-05-25)

---

## Architecture decisions (stable)

- **IMGUI throughout** — no UI Toolkit
- **JSON is source of truth**; `.asset` files are derived artifacts
- **Cell polymorphism**: `CellDataConverter` writes a `"type"` discriminator field; `ICellData` implementations have `[JsonIgnore]` on `CellTypeId`
- **Brush template pattern**: `PalettePanel` configures `BrushTemplate`; `GridCanvasPanel.PlaceAt` clones it via `CloneBrushTemplate()` (serializer round-trip = deep copy)
- **`DrawInspector` must use absolute rects only** — never `GUILayout.BeginArea` (causes IMGUI render leaks)
- **`MarkDirty()` for non-grid mutations** (Notes, top-section inspector edits); `PushUndoSnapshot()` for grid mutations
- **`PreferredHeight` on TopSectionPanel** is dynamically computed from cached spool count (updated each `OnGUI` call)
- **`ColorSwatchDrawer.Draw()`** is the canonical way to render a palette color picker anywhere in the editor
- **`PushUndoSnapshot()` before mutations** (not after) — undo stack holds pre-mutation JSON snapshots

---

## Package / repo layout

```
hoppa-level-editor-core/
  Packages/
    com.hoppa.leveleditor.core/
      Runtime/               — ICellData, LevelDocument, GridData, serialization, validation
      Editor/                — EditorWindow, panels, GameProfile, CellTypeDefinition, exporters
      Tests/Runtime/         — NUnit tests for serialization + validation
      Tests/Editor/          — Editor NUnit tests
  Assets/
    YarnTwist/               — Yarn Twist game layer (Runtime + Editor + Tests + Data)
    Samples/                 — DemoColorGridGame sample
```

Consumer games reference this package via private GitHub Git URL (production):
- `YarnTwist` (`E:/Projects/Hoppa/YarnTwist`) — pinned at `v0.5.21` (Palette mechanic + canvas-overlay hook).
- `YarnKingdom` (`E:/Projects/Hoppa/YarnKingdom`) — currently pinned at `v0.5.16`.

```
"com.hoppa.leveleditor.core": "https://github.com/ItayKlainman/level-editor.git?path=Packages/com.hoppa.leveleditor.core#v0.5.21"
```

Latest tag: `v0.5.21` (2026-06-02) — Palette mechanic (Layer-2 YarnTwist) + Layer-1
`CanvasOverlayAsset` hook (`GameProfile.CanvasOverlay`, drawn by `GridCanvasPanel`).
Prior: `v0.5.20` (2026-06-01) — Connected Boxes mechanic + Layer-1 cell context-action
extension (`CellActionContext`, free-form apply actions). Connected Spools shipped on
`v0.5.20` too (Layer-2 only, no tag bump).

Layer 2 files live in both repos and must be kept in sync manually when changed.

**YarnTwist Layer 2 target paths:**
- `Assets/_YAT/Scripts/Editor/TopSection/YarnTopSectionPanel.cs`
- `Assets/_YAT/Scripts/Editor/YarnLevelOrderPanel.cs`

**YarnKingdom Layer 2 target paths** (`Assets/YAK/` → `Assets/_YAK/LevelEditor/`):
- `Editor/` → `Editor/` (cells, top section, validation, importer, exporter, color source, asmdef)
- `Runtime/` → `Runtime/` (cell types, top-section data, asmdef)
- `Data/Config/` → `Data/Config/` (CellDefs, Exporters, Rules, Palette, YAKProfile.asset)

**Do NOT sync to YarnKingdom:**
- `Runtime/YAKStaticManagerScriptableObject.cs` — editor-side mirror; game has its own real type under `YAK.Gamelogic`.
- `Data/Config/Palette/StaticManager.asset`, `YAKPalette.asset`, `Exporters/YAKColorMapping.asset` — legacy / editor-only artifacts.
- `Editor/YAK.Editor.asmdef` and `Data/Config/Palette/YAKStaticManagerColorSource.asset` — these intentionally drift between projects (asmdef refs `YAK.Gamelogic` only in YarnKingdom; color source's `_staticManager` points at YarnKingdom's game StaticManager GUID).

---

## Open items

### Manual steps (no code needed)
- [ ] `YarnTwistProfile.asset` → set `_schemaId` to `yarn-twist` (currently causes double `.v1` in schema display)
- [ ] `YarnTunnelCellDef.asset` → assign `YarnTwistPalette` to `_palette` field
- [ ] In YarnTwist project: create `YarnLevelOrderPanel` asset, assign exporter, assign to `YarnTwistProfile.asset` → Order Panel field

### Low-priority Jira gaps (not implemented)
- Arrow box / tunnel target cell highlight
- Top View (Preview) mini-panel
- Copy Level ID button

### Game-side TODOs (YarnTwist repo)
- [ ] Confirm `YATColorType` int values and lock `YarnColorMapping.asset`
- [ ] Grid offset `−3.5f` fix (variable grid dimensions)
- [ ] ArrowBox + Tunnel full prefab implementation
- [ ] Camera auto-fit wiring
- [ ] Win/lose flow restoration

---

## Resolved decisions

### Naming & namespaces
- Package: `com.hoppa.leveleditor.core`
- C# namespaces: `Hoppa.LevelEditor.Core` (runtime), `Hoppa.LevelEditor.Core.Editor`
- Yarn Twist namespace: `Hoppa.YarnTwist` / `Hoppa.YarnTwist.Editor`

### ID conventions
- Framework cell types: `core.*`
- Yarn Twist cell types: `yt.*` (`yt.empty`, `yt.wall`, `yt.box`, `yt.arrowbox`, `yt.tunnel`)
- Schema ID: `yarn-twist` (profile asset `_schemaId`); stamped into levels as `yarn-twist.v1`

### Package hosting
- Dev: local `file:` path in game project's `manifest.json`
- Prod: private GitHub repo Git URL, pinned to a version tag (currently `v0.5.3`). Bump tag and update consumer `manifest.json` to deploy Layer 1 changes.
- **Critical:** every new `.cs` file added to the UPM package must have its `.meta` file committed to git, or Unity will silently exclude the file from compilation in consumers (experienced with `MultiSelectPanel.cs.meta`).

### Deferred
- Addressables integration
- Second-game onboarding (Phase 6) — deferred until next project driver
- Yarn Sort ID prefix decision (`YRN` vs `YS`) — use `yt.*` until confirmed
