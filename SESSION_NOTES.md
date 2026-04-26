# Session Notes — Studio Level Editor

> Read this first when resuming work. Companion to `PLANNING.md` (the
> authoritative spec) and `CURRENT_TASK.md` (what's actively in flight).

---

## Current status (as of 2026-04-26)

- **Project**: `hoppa-level-editor-core` — standalone Unity 2022.3 project hosting the UPM package `com.hoppa.leveleditor.core`.
- **Active branch**: `feat/phase5-data-pipeline` — pushed to GitHub, ready for PR/merge.
- **All planned phases complete.** The framework is fully functional with Yarn Twist as the first game integration.

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
- `LevelEditorWindow` — 3-column IMGUI window (Toolbar | Canvas+TopSection | Validation+Inspector+Summary)
- `LevelEditorSession` — session state: `Document`, `CellTypes`, `ActiveCellType`, `BrushTemplate`, `SelectedCell`, `IsDirty`, undo/redo stack, `CloneBrushTemplate()`
- `GameProfile` — ScriptableObject wiring cell types, validation rules, exporters, top section script
- `CellTypeDefinition` / `ICellTypeDefinition` — abstract SO base; game subclasses implement `DrawCell` + `DrawInspector`
- `CellTypeRegistry` — maps TypeId ↔ concrete type ↔ definition
- `LevelExporterAsset` — abstract SO base for exporters
- `ScriptableObjectExporter` — produces `.asset` alongside `.json`
- `ColorPaletteAsset`, `ColorSwatchDrawer` — palette SO + reusable IMGUI swatch picker
- Panels: `PalettePanel` (cell list + BRUSH config), `GridCanvasPanel`, `ToolbarPanel`, `ValidationPanel`, `CellInspectorPanel`, `SummaryPanel`
- `TopSectionPanel` / `EmptyTopSectionPanel` — game-overridable top section
- `StringIntMapping` — reusable string→int ScriptableObject

### Yarn Twist game layer (`Assets/YarnTwist/`)

**Runtime** (`Hoppa.YarnTwist.Runtime`):
- Cell types: `YarnEmptyCell`, `YarnWallCell`, `YarnBoxCell`, `YarnArrowBoxCell`, `YarnTunnelCell`
- `YarnDirection` enum, `YarnTopSectionData`, `YarnSpoolColumn`, `YarnSpoolData`

**Editor** (`Hoppa.YarnTwist.Editor`):
- Cell definitions: `YarnEmptyCellDefinition`, `YarnWallCellDefinition`, `YarnBoxCellDefinition`, `YarnArrowBoxCellDefinition`, `YarnTunnelCellDefinition` (all use color swatches in `DrawInspector`)
- `YarnTopSectionPanel` — 4-column spool editor with color swatches + hidden toggle, dynamic height
- Validation rules: `YarnColorBalanceRule` (emits Info table + Error for imbalances), `YarnArrowBoxTargetRule`, `YarnTunnelOutputRule`
- `YarnMasterLevelExporter` — transforms `LevelDocument` → `level_config.json` (game schema) on every Save
- `StringIntMapping` assets: `YarnColorMapping.asset` (10 colors), `YarnCellTypeMapping.asset` (5 types)

**Data assets** (`Assets/YarnTwist/Data/Config/`):
- `YarnTwistProfile.asset` — `GameProfile` wiring all 5 cell types + rules + exporter
- `YarnTwistPalette.asset` — 6 colors (pink, blue, teal, green, yellow, purple) — **needs to exist; see manual steps**
- `YarnMasterLevelExporter.asset` — output path wired to `E:/Projects/Hoppa/YarnTwist/Assets/_YAT/Configs/Resources/Configs/level_config.json`

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

Consumer game (`YarnTwist`) references this package via:
```
"com.hoppa.leveleditor.core": "file:../hoppa-level-editor-core/Packages/com.hoppa.leveleditor.core"
```

---

## Open items

### Manual steps (no code needed)
- [ ] `YarnTwistProfile.asset` → set `_schemaId` to `yarn-twist` (currently causes double `.v1` in schema display)
- [ ] `YarnTunnelCellDef.asset` → assign `YarnTwistPalette` to `_palette` field
- [ ] Smoke test the full Save → `level_config.json` export pipeline

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
- Prod: private GitHub repo Git URL ref

### Deferred
- Addressables integration
- Second-game onboarding (Phase 6) — deferred until next project driver
- Yarn Sort ID prefix decision (`YRN` vs `YS`) — use `yt.*` until confirmed
