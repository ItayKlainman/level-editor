# Studio Level Editor — Planning Document

## Context

The studio ships multiple mobile puzzle games per year and needs a shared
content pipeline. Every game shares the same core authoring shape —
grid layouts, color-matching mechanics, optional hidden/directional
elements, JSON export — but the specific cell types and rules change per
title. Today every game reinvents its own level editor, which is expensive
and blocks AI-assisted level generation (each game has a different,
undocumented level format).

This project (`hoppa-level-editor-core`) is the **first** of a two-part
delivery:

1. A reusable Unity Editor **framework** shipped as a UPM package
   (`com.hoppa.leveleditor.core`) that provides a pluggable authoring
   shell (EditorWindow + panels), color palette management, rule-based
   validation, JSON I/O, and schema migration. Yarn Sort is a grid
   puzzle, so grid primitives ship in the initial Layer 1 surface;
   whether they remain Layer 1 or extract to an optional module is a
   decision we defer until game 2 arrives.
2. **Yarn Sort Puzzle** — the first consumer, built as a separate game
   project that references the package and registers its own cell types,
   colors, and rules. (Yarn Sort is NOT built in this repo.)

**Success criterion:** a fully working level editor for Yarn Sort,
built on a clean two-layer architecture that does not need to be
rewritten when we add game number two. Second-game fidelity is
validated later with a real driver, not a hypothetical one (see
Phase 6 — deferred).

A secondary goal is that the JSON schema is AI-readable so Claude
Code + Unity MCP can author/edit levels directly without going
through the UI.

---

## Final Decisions Summary (one-page cheat sheet)

**Naming & prefixes**
- Package name: `com.hoppa.leveleditor.core`
- C# namespaces: `Hoppa.LevelEditor.Core` (Runtime), `Hoppa.LevelEditor.Core.Editor` (Editor)
- Generic framework prefix: `core.*` (rule IDs, cell type IDs)
- Per-game prefix matches studio's class/file naming convention (e.g. `SNU`, `CFG`). Yarn Sort uses `ys.*` as placeholder; confirm with team before Phase 5.

**Package hosting**
- Local file path during development (`"file:../hoppa-level-editor-core/Packages/com.hoppa.leveleditor.core"`)
- Private GitHub repo git-URL reference for production
- No npm registry, no tarballs

**Unity**
- Floor: 2022.3 LTS (`"unity": "2022.3"` in `package.json`)
- Forward-compat with 2023 / 6000: nice-to-have, not a gate

**UI**
- IMGUI throughout (not UI Toolkit)
- `IEditorPanel.OnGUI(Rect rect, LevelEditorSession session)`

**Serialization**
- JSON is source of truth; ScriptableObjects are derived artifacts
- Newtonsoft.Json via `com.unity.nuget.newtonsoft-json` (hard dependency)
- Cell polymorphism via `JsonConverter` driven by `ICellTypeRegistry`

**Color palette**
- Per-project `ColorPaletteAsset` (not per-level, not fixed roster)
- Levels reference colors by string `colorId`

**Testing**
- Required: Runtime unit tests (serialization round-trip, validation, schema migration)
- Required: Editor smoke tests (window opens, save/load round-trip, panels populate)
- Out of scope: UI interaction tests

**Yarn Sort Layer 2 specifics (NOT framework constraints)**
- Tunnel queue: plain colored boxes only *for Yarn Sort*. The generic framework places no restriction on tunnel queue contents — a different game may queue anything its cell registry supports.
- Hidden Arrow Box: valid (hidden applies to color only; direction always visible)
- Mixed colors per spool column: allowed
- Row ordering: `bottomUp` (JSON array index 0 = bottom row)
- Arrow Box target: single adjacent cell in arrow direction only
- Level ID format: human-readable strings, e.g. `"level_001"`

---

## 1. Architecture Overview

### Two-layer model

```
┌─────────────────────────────────────────────────────────────────┐
│ Layer 2 — Game-specific (lives in game project, e.g. YarnSort)  │
│   • Cell type definitions (BoxCell, TunnelCell, ArrowBoxCell…)  │
│   • Color palette asset (yarn colors: red/blue/green…)          │
│   • Validation rules (balls==spools*3, arrow target valid…)     │
│   • Top-section schema (spool columns, visibility flag)         │
│   • JSON schema binding (`"schemaVersion": "yarn-sort.v1"`)     │
└─────────────────────────────────────────────────────────────────┘
                              ▲ extends via interfaces + registries
┌─────────────────────────────────────────────────────────────────┐
│ Layer 1 — Generic framework (UPM: com.hoppa.leveleditor.core)   │
│   Runtime/: data models, rule contracts, JSON (de)serialization │
│   Editor/:  EditorWindow, panels, palette, validation UI, exp.  │
└─────────────────────────────────────────────────────────────────┘
```

**Golden rule:** Layer 1 must compile and run with ZERO references to
yarn/spool/box concepts. The package ships a tiny "DemoColorGridGame"
sample as a reference implementation and as a Layer 1 hygiene check —
if the sample breaks when we change the core, we've leaked game
specifics into the wrong layer.

### Package folder structure

```
Packages/com.hoppa.leveleditor.core/
├── package.json                         # name, version, unity, deps
├── README.md                            # how to consume
├── CHANGELOG.md
├── LICENSE.md
├── Runtime/                             # loaded in game builds
│   ├── Hoppa.LevelEditor.Core.Runtime.asmdef
│   ├── Data/
│   │   ├── GridData.cs                  # generic grid<TCell>
│   │   ├── LevelDocument.cs             # envelope: id, version, payload
│   │   ├── CellRef.cs                   # (x,y) struct
│   │   └── ColorId.cs                   # opaque color handle (int+string)
│   ├── Serialization/
│   │   ├── ILevelSerializer.cs
│   │   ├── JsonLevelSerializer.cs       # Newtonsoft-backed
│   │   ├── ISchemaMigration.cs
│   │   └── SchemaRegistry.cs
│   ├── Validation/
│   │   ├── IValidationRule.cs
│   │   ├── ValidationReport.cs
│   │   ├── ValidationSeverity.cs
│   │   └── ValidationContext.cs
│   └── Cells/
│       ├── ICellData.cs
│       └── ICellTypeRegistry.cs         # runtime lookup only
├── Editor/                              # stripped from game builds
│   ├── Hoppa.LevelEditor.Core.Editor.asmdef   (editorOnly)
│   ├── Window/
│   │   └── LevelEditorWindow.cs         # top-level host (IMGUI)
│   ├── Panels/
│   │   ├── IEditorPanel.cs              # pluggable panel contract
│   │   ├── PalettePanel.cs
│   │   ├── GridCanvasPanel.cs
│   │   ├── TopSectionPanel.cs           # abstract base (stacks/columns)
│   │   ├── ValidationPanel.cs
│   │   ├── SummaryPanel.cs
│   │   └── ToolbarPanel.cs              # New/Open/Save/TestPlay
│   ├── Registry/
│   │   ├── CellTypeDefinition.cs        # ICellTypeDefinition impl base
│   │   ├── CellTypeRegistry.cs          # AppDomain scan + explicit add
│   │   └── ValidationRuleRegistry.cs
│   ├── ColorPalette/
│   │   ├── ColorPaletteAsset.cs         # ScriptableObject
│   │   └── ColorPaletteEditor.cs
│   ├── Exporters/
│   │   ├── ILevelExporter.cs
│   │   ├── JsonExporter.cs
│   │   └── ScriptableObjectExporter.cs
│   ├── Undo/
│   │   └── GridUndoScope.cs             # wraps Unity Undo system
│   └── Infrastructure/
│       ├── LevelEditorSettings.cs       # per-project settings
│       └── GameProfile.cs               # the Layer 2 entry point SO
├── Samples~/                            # UPM-convention "Samples"
│   └── DemoColorGridGame/                # trivial Layer 2 sample
└── Tests/
    ├── Runtime/Hoppa.LevelEditor.Core.Tests.asmdef
    └── Editor/Hoppa.LevelEditor.Core.Editor.Tests.asmdef
```

### Key interfaces and abstractions

| Type | Layer | Purpose |
|------|-------|---------|
| `ICellData` | Runtime | Marker interface; per-game cell payload (e.g. `YarnBoxCell`) |
| `ICellTypeDefinition` | Editor | Describes a cell type: id, display name, icon, palette group, default data factory, custom inspector, canvas renderer |
| `ICellTypeRegistry` | Runtime + Editor | Maps `cellTypeId` ↔ `ICellData` concrete type for serialization |
| `IValidationRule` | Runtime | Single rule over a `ValidationContext`, returns `ValidationReport` entries |
| `IValidationRuleProvider` | Editor | Yields a game's registered rules |
| `ILevelSerializer` | Runtime | JSON in/out for `LevelDocument` using Newtonsoft |
| `ISchemaMigration` | Runtime | Version N → N+1 transformer for old files |
| `ILevelExporter` | Editor | Writes additional artifacts (e.g. ScriptableObject) from a validated `LevelDocument` |
| `IEditorPanel` | Editor | Docked region inside `LevelEditorWindow` with `OnGUI(Rect rect, LevelEditorSession s)` (IMGUI) |
| `GameProfile` | Editor (SO) | The single entry point a consumer creates. Lists color palette, grid dims, registered cell types, registered rules, registered exporters, schema id |
| `LevelEditorSession` | Editor | In-memory editing state; owns current `LevelDocument`, undo stack, dirty flag, validation cache |

### How a future game plugs in

The consumer game project does exactly four things:

1. Add the package (`manifest.json` → `"com.hoppa.leveleditor.core": "file:../hoppa-level-editor-core/Packages/com.hoppa.leveleditor.core"` during development; git URL or tarball later).
2. Author a `ColorPaletteAsset` with the game's colors.
3. Author a `GameProfile` ScriptableObject that references: color palette, grid dims, cell type definitions (assets or typed via attribute-discovery), rules, exporters, schema id + version.
4. Implement one `ICellData` class + one `ICellTypeDefinition` asset per cell type; implement one `IValidationRule` class per rule.

Opening the editor window then asks the user to select a `GameProfile` (or auto-picks the only one in the project) and bootstraps everything from it.

---

## 2. Data Model

### Top-level envelope (generic, always present)

```jsonc
{
  "$schema": "https://hoppa.studio/schema/leveleditor/v1.json",
  "schemaVersion": "yarn-sort.v1",      // game id + semver
  "levelId": "level_001",
  "displayName": "Tutorial 1",
  "metadata": {
    "author": "itay",
    "createdAt": "2026-04-21T12:56:00Z",
    "modifiedAt": "2026-04-21T12:58:00Z",
    "tags": ["tutorial", "easy"]
  },
  "grid": {
    "width": 5,
    "height": 7,
    "cells": [ /* length = width*height, row-major, bottom-up or top-down — declared explicitly */ ]
  },
  "topSection": { /* game-specific; empty {} if game has no top section */ },
  "gameData": { /* game-specific extra fields */ }
}
```

### Yarn Sort concrete schema (`yarn-sort.v1`)

```jsonc
{
  "schemaVersion": "yarn-sort.v1",
  "levelId": "level_001",
  "displayName": "Tutorial 1",
  "metadata": { /* …as above… */ },

  "grid": {
    "width": 5,
    "height": 7,
    "rowOrder": "bottomUp",     // explicit: first row of array is y=0 (bottom)
    "cells": [
      { "type": "empty" },
      { "type": "wall" },
      { "type": "box", "colorId": "red", "hidden": false },
      { "type": "arrowBox", "colorId": "blue", "direction": "up", "hidden": false },
      { "type": "tunnel", "direction": "right",
        "queue": [
          { "colorId": "red", "hidden": false },
          { "colorId": "green", "hidden": true }
        ]
      }
      /* … */
    ]
  },

  "topSection": {
    "kind": "spoolColumns",
    "columnCount": 4,
    "columns": [
      {
        "spools": [
          { "colorId": "red",   "visibility": "visible" },  // bottom = index 0 = active first
          { "colorId": "blue",  "visibility": "hidden"  },
          { "colorId": "green", "visibility": "visible" }
        ]
      }
      /* 3 more columns */
    ]
  },

  "gameData": {
    "ballsPerBox": 9,
    "ballsPerSpool": 3
  }
}
```

**Design notes:**

- Cells are a heterogeneous array discriminated by `"type"`. Newtonsoft.Json handles this cleanly via a `JsonConverter` on `ICellData`. `JsonUtility` cannot, which is why we take the Newtonsoft dep.
- `colorId` is a **string** (not an int, not a Color). Strings are stable across palette reorderings and are readable by Claude/AI. Strings also survive palette additions without re-indexing old levels.
- `direction` is a string enum: `"up" | "down" | "left" | "right"`. AI-readable.
- `hidden` and `visibility` are explicit per-cell / per-spool flags — no implicit "everything below is hidden" semantics.
- `rowOrder` is explicit because half the industry does top-down and half does bottom-up. Spell it out; migrate on change.
- `gameData` is a free-form object the generic core passes through untouched to the game.

### Schema versioning

- Each `schemaVersion` is `"<gameId>.v<int>"`.
- Game registers `ISchemaMigration` instances in its `GameProfile`.
- On load: `SchemaRegistry` detects version, runs the chain of migrations to current, marks document dirty so a save re-emits latest.
- Breaking a field = bump version + write a migration; never silently repurpose a field.

### ScriptableObject class hierarchy

```
ScriptableObject
├── ColorPaletteAsset              (generic)
├── GameProfile                    (generic, per game)
├── CellTypeDefinition (abstract)  (generic)
│   └── YarnCellTypeDefinition     (game)  — holds icon, palette group, prefab ref
└── LevelAsset (abstract)          (generic)  — wraps the authored JSON + cached parsed doc
    └── YarnLevelAsset             (game, auto-generated by ScriptableObjectExporter)
```

The SO is a **derived artifact**: on save, `JsonExporter` writes the .json, then `ScriptableObjectExporter` creates/updates the .asset by deserializing the JSON. JSON is the source of truth; SOs exist for runtime convenience and for things like `AssetReference` addressables.

---

## 3. Editor Tool Architecture

### EditorWindow layout (IMGUI)

```
┌─ ToolbarPanel ───────────────────────────────────────────────────┐
│  [New] [Open] [Save] [Save As] [Test Play]   Level: level_001 ▾  │
├──────────────┬──────────────────────────────────┬────────────────┤
│              │                                  │                │
│  Palette     │  Top Section (game-defined)      │  Validation    │
│  (grouped    │  ─ e.g. 4 spool columns editor ─ │  (live)        │
│  by cell-    │                                  │                │
│  type reg.)  │─ Grid Canvas ────────────────────│  Summary       │
│              │  5 × 7 cells, zoom, pan          │                │
│              │  selection, placement, preview   │  Export        │
│              │                                  │                │
├──────────────┴──────────────────────────────────┴────────────────┤
│  Status bar: dirty •  cursor (3,4)  schema yarn-sort.v1          │
└──────────────────────────────────────────────────────────────────┘
```

Panels are `IEditorPanel` implementations; the window's `OnGUI` carves out rects and calls each panel's `OnGUI(Rect, LevelEditorSession)`. Games can add or replace panels via `GameProfile`.

### Grid editor mechanics

- **Model:** `GridData<ICellData>` — a dense `ICellData[width*height]` array plus width/height.
- **Rendering:** IMGUI — the grid canvas is a single `OnGUI` pass that draws each cell with `GUI.DrawTexture` / `EditorGUI.DrawRect` inside a scrollable `Rect` (`GUI.BeginScrollView`). Each cell's visual comes from `ICellTypeDefinition.DrawCell(Rect cellRect, ICellData data)`. 245 cells on IMGUI is well within comfortable perf.
- **Interaction:** driven by `Event.current` inside the canvas `OnGUI`:
  - `MouseDown` + `button == 0` → place currently selected palette entry.
  - `MouseDown` + `button == 1` → erase to registered "empty" cell type.
  - `KeyDown` + `keyCode == R` → rotate (only on cells implementing `IDirectionalCell`).
  - `KeyDown` + `keyCode == Delete` → clear selected cell(s).
  - `MouseDrag` while LMB held → drag-paint.
  - `Shift + MouseDrag` → selection rect.
  - Remember to call `Event.current.Use()` after handling to prevent bubbling, and `GUI.changed = true` to trigger repaint.
- **Undo:** every mutation goes through `GridUndoScope` which wraps `Undo.RegisterCompleteObjectUndo` on the session's backing ScriptableObject proxy. Ctrl+Z works everywhere via Unity's native undo.
- **Cell inspector:** when a cell is selected, the right panel shows an inspector driven by `ICellTypeDefinition.DrawInspector(Rect rect, ICellData data)`. E.g. a `TunnelCell` shows the ordered queue editor; `ArrowBoxCell` shows the direction dropdown. Wrap in `EditorGUI.BeginChangeCheck()` / `EndChangeCheck()` to detect edits.

### Top-section editor

Abstract base `TopSectionPanel` with `void OnGUI(Rect rect, LevelEditorSession s)`. Yarn Sort ships `SpoolColumnsTopSectionPanel` that renders 4 vertical columns using `UnityEditorInternal.ReorderableList` for each column's spool stack. Games without a top section provide `EmptyTopSectionPanel` or omit the panel (window hides the region).

### Validation engine

- `IValidationRule` contract:
  ```csharp
  public interface IValidationRule {
    string Id { get; }                  // "ys.ball-capacity-match"
    ValidationScope Scope { get; }      // Level | Cell | Color | Stack
    IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx);
  }
  ```
- `ValidationContext` gives rules read-only access to `LevelDocument`, `ColorPaletteAsset`, and computed helpers (ball count per color, spool capacity per color, neighbor lookup).
- **Registration:** rules are collected from `GameProfile.Rules` (list of `IValidationRuleProvider` SOs or typed refs). No attribute magic required, but attribute-based auto-discovery is supported as an opt-in.
- **Execution:** on every model mutation, session enqueues a validation pass (debounced ~100 ms). Results feed `ValidationPanel`, which groups by severity and makes each entry clickable to navigate the editor to the offending cell/color/stack.
- **Severity:** `Error | Warning | Info`. Export is blocked on `Error` by default; designer can override with a confirmation.

### Yarn Sort's concrete rules (Layer 2)

IDs below use `ys.*` as a placeholder — confirm the studio prefix (`YRN` vs `YS`) before Phase 5 and substitute.

| Rule id | What it checks |
|---------|----------------|
| `ys.ball-capacity-match` | `total_balls == total_spool_capacity` |
| `ys.color-capacity-match` | per-color `boxes*9 == spools*3` |
| `ys.arrow-target-valid` | arrow box's target cell (single adjacent cell in arrow direction) exists and is not Wall |
| `ys.tunnel-output-valid` | tunnel's output direction cell exists and is not Wall |
| `ys.tunnel-queue-nonempty` | tunnel has ≥1 queued box |
| `ys.hidden-sanity` | hidden flag used only on cell types that support it (Box, ArrowBox, queued tunnel boxes — not Wall/Empty/Tunnel itself) |
| `core.palette-colors-exist` | every `colorId` referenced exists in the `ColorPaletteAsset` (generic rule, not yarn-specific) |

### Load / Save / Export flow

```
File → New         → session = empty LevelDocument from GameProfile defaults
File → Open (.json) → JsonLevelSerializer.Load → SchemaRegistry.Migrate → session
File → Save        → validate (block on Errors) → JsonExporter → ScriptableObjectExporter
File → Save As     → file dialog → Save
Test Play          → Save → EditorApplication.EnterPlaymode with a session-scoped
                      launcher SO pointing at the saved JSON
```

---

## 4. Generic Framework Design

### What lives where

| Concern | Generic (package) | Game-specific (consumer) |
|---------|-------------------|--------------------------|
| Grid model (`GridData<T>`) | ✓ | — |
| Cell type interface | ✓ | — |
| Concrete cell types (Box, Tunnel…) | — | ✓ |
| Color palette **asset type** | ✓ | — |
| Color palette **contents** | — | ✓ |
| Validation engine + report UI | ✓ | — |
| Concrete validation rules | — | ✓ |
| JSON envelope + serializer | ✓ | — |
| Game's JSON payload shape | — | ✓ (via converters) |
| ScriptableObject exporter base | ✓ | — |
| Concrete Level SO class | — | ✓ |
| EditorWindow host + panels | ✓ | — |
| Top-section panel (spool columns) | — (abstract only) | ✓ |

Rule of thumb: **generic owns the shape; game owns the content.**

### Color management

- `ColorPaletteAsset` (SO) holds `List<ColorEntry>` where `ColorEntry = { id: string, displayName: string, color: Color, swatchIcon?: Texture2D }`.
- `ColorId` is a `readonly struct` wrapping the string id plus a cached int hash. Runtime comparisons are O(1).
- Game code refers to colors by id string (`"red"`), never by index. The palette asset is the mapping from id → Color.
- Validation: a rule `core.palette-colors-exist` checks that every id referenced in the level exists in the palette. Generic — every game benefits.

### Cell type registry

- Two discovery paths (both supported, games pick one):
  1. **Attribute-based:** `[LevelEditorCellType(typeof(MyGameProfile))]` on a `CellTypeDefinition` subclass → `CellTypeRegistry` scans on first open.
  2. **Explicit list:** `GameProfile.CellTypes` is a `List<CellTypeDefinition>` SO refs.
- Registry responsibilities:
  - id ↔ concrete `ICellData` type mapping (for JSON deserialization).
  - id ↔ `CellTypeDefinition` mapping (for UI: palette entry, icon, inspector, renderer).
  - Validation: every non-"empty" cell type has exactly one `CellTypeDefinition`.

### Validation rule registry

- `GameProfile.Rules` is a `List<ValidationRuleDefinition>` (SO refs). Each def carries an id + a `Type` ref to the concrete `IValidationRule` class (or a pure-data rule — see below).
- Two rule flavors:
  - **Code rules:** arbitrary `IValidationRule` class. Required for topological checks (neighbor lookups, directional reachability).
  - **Data rules:** pre-built parameterized templates (e.g. `ColorBalanceRule { sourceSelector, sinkSelector, ratio }`) configured entirely in the inspector. Good for counting rules, zero-code. Ship ~3 templates in the core.
- A designer adding a new counting rule configures a data-rule asset; adding a new topological rule requires a dev to write a code rule. That's a fair line.

---

## 5. Implementation Phases

Ordered by dependency. Each phase ends in something demoable.

### Phase 0 — Package skeleton (risk: low)
- Create the `Packages/com.hoppa.leveleditor.core/` folder structure above.
- Write `package.json` (name, version `0.1.0`, `"unity": "2022.3"`, display name, description).
- Write both `.asmdef` files with correct root namespaces (`Hoppa.LevelEditor.Core`, `Hoppa.LevelEditor.Core.Editor`; Editor asmdef has `"includePlatforms": ["Editor"]`).
- Stub README.md (how to consume, link to PLANNING.md).
- Add `com.unity.nuget.newtonsoft-json` to the package's dependencies.
- Set up Tests assemblies (Runtime + Editor).
- **Phase 0 exit protocol (must be completed before Phase 1):**
  1. Ensure the finalized plan is saved as `PLANNING.md` in the project root (done before Phase 0 starts).
  2. Create `Packages/com.hoppa.leveleditor.core/CHANGELOG.md` stub with a `## [0.1.0] - <ISO date>` header and "Initial package skeleton." entry.
  3. Confirm the on-disk folder structure matches Section 1 exactly.
  4. **Stop. Wait for explicit user confirmation before starting Phase 1.** Do not auto-proceed.
- **Demo:** Opening `Window → Level Editor` shows an empty window that says "No GameProfile assigned."

### Phase 1 — Core data + serialization (risk: medium — JSON polymorphism is the landmine)
- `GridData<T>`, `LevelDocument`, `ColorId`, `CellRef`.
- `JsonLevelSerializer` with Newtonsoft `JsonConverter` for `ICellData` polymorphism driven by `ICellTypeRegistry`.
- `ColorPaletteAsset` + inspector.
- `GameProfile` SO + inspector.
- Round-trip unit tests: document → JSON → document equality.
- **Demo:** CLI/test that loads a hand-written sample JSON, validates it parses, re-serializes identically.

### Phase 2 — Validation engine (risk: low)
- `IValidationRule`, `ValidationContext`, `ValidationReport`, registry.
- Ship 3 generic data-rule templates (`ColorBalanceRule`, `PaletteColorsExistRule`, `GridNonEmptyRule`).
- `ValidationPanel` UI (list grouped by severity, click-to-navigate).
- **Demo:** validation panel lights up errors on a deliberately broken sample JSON.

### Phase 3 — EditorWindow + grid canvas (risk: medium — IMGUI event handling is fiddly but well-trodden)
- `LevelEditorWindow` with IMGUI panel host (`OnGUI` carves rects, delegates to panels).
- `PalettePanel`, `GridCanvasPanel`, `ToolbarPanel`, `SummaryPanel`.
- Left/right click placement, drag-paint, R-rotate, Delete, Ctrl+Z undo — all via `Event.current`.
- Cell inspector (right side, context-sensitive per cell type via `ICellTypeDefinition.DrawInspector`).
- **Demo:** Using the bundled "DemoColorGridGame" sample, author a 3-color grid puzzle, save, reopen, continue editing.

### Phase 4 — Top-section abstraction + export (risk: medium)
- `TopSectionPanel` abstract + swap mechanism tied to `GameProfile`.
- `JsonExporter` + `ScriptableObjectExporter`.
- File → New / Open / Save / Save As dialogs.
- Test Play launcher.
- **Demo:** Save produces both `.json` and `.asset`; Open works; schema version round-trips.

### Phase 5 — Yarn Sort Layer 2 (risk: medium — done in the GAME project, not here)
- In the Yarn Sort project, add the package via local file path.
- Author `YarnColorPalette`, `YarnGameProfile`, `YarnCellTypeDefinitions`, `SpoolColumnsTopSectionPanel`, validation rules.
- **Demo:** Full Yarn Sort level authoring per the ticket's UX spec, passing all validation rules.

### Phase 6 — Future: Second-game onboarding

Deferred until Yarn Sort is shipped. Driver game TBD based on next
project. When that game is picked, we will:
- Attempt to stand it up by writing only Layer 2 assets against the
  package.
- Treat any required package edit as a signal to refactor the
  Layer 1 / Layer 2 split using a real game as the driver — not a
  hypothetical one.
- If the next game is not grid-shaped, extract grid primitives out of
  Layer 1 into an optional module at that point.

### Highest-risk technical decisions
1. **The Layer 1/Layer 2 cut for the top section.** Getting this wrong means game 2 fights the framework. Mitigation is deferred: validation against a real second game happens post-ship (Phase 6 — deferred). For this project the discipline is simply to keep yarn/spool/box concepts strictly in Layer 2 and revisit the cut when game 2 arrives.
2. **`ICellData` polymorphism contract.** The Newtonsoft `JsonConverter` driven by the cell-type registry must handle unknown types gracefully (for migrations) and must preserve unknown fields on round-trip (so designers hand-editing JSON don't lose data). Mitigation: comprehensive round-trip tests in Phase 1.
3. **Undo granularity.** Too coarse (one undo per save) = designers lose work; too fine (undo per pixel drag) = unusable. Mitigation: group `MouseDown → MouseUp` into one undo operation using `Undo.IncrementCurrentGroup` at `MouseDown` and `Undo.CollapseUndoOperations` at `MouseUp`.

---

## 6. Resolved Decisions (for the record)

### Architectural decisions (RESOLVED 2026-04-21)

1. **IMGUI, not UI Toolkit.** `IEditorPanel.OnGUI(Rect, LevelEditorSession)`. All panels, the window host, the grid canvas, and cell inspectors use IMGUI / `EditorGUILayout` / `Event.current`. UI Toolkit can be revisited per-game in Layer 2 if a future game needs it.
2. **Newtonsoft.Json is a hard dependency** via `com.unity.nuget.newtonsoft-json`. Required for polymorphic `ICellData` serialization.
3. **JSON is the source of truth**; ScriptableObjects are derived artifacts regenerated on save. The exporter updates existing `.asset` files in place to preserve GUIDs for `AssetReference` stability.

### Yarn Sort GDD (RESOLVED 2026-04-21)

- **Tunnel internal queue**: plain colored Boxes only (no nested Tunnels or Arrow Boxes). `hidden` flag still allowed on queued boxes. → Schema's `tunnel.queue[]` entries carry only `{ colorId, hidden }`, no `type` discriminator needed. (Yarn Sort constraint; the generic framework itself does not restrict tunnel queue contents.)
- **Hidden + Arrow Box**: hidden applies to **color only**; direction is always visible. → `hidden: true` valid on `arrowBox`; validation rule `ys.hidden-sanity` must permit this combo.
- **Spool column colors**: mixed colors allowed top-to-bottom within a column. → Schema already supports this (per-spool `colorId`); no additional constraint rule.
- **Row ordering**: `"rowOrder": "bottomUp"` is canonical. Always emit this value; SchemaRegistry rejects (or migrates) other values on load.
- **Arrow Box target**: the single adjacent cell in the arrow's direction (not a ray). → `ys.arrow-target-valid` checks `grid[x+dx, y+dy]` only.
- **Level ID format**: `level_XXX` (zero-padded string, designer-controlled). → `LevelDocument.levelId` is a string; add a warning-severity rule `core.level-id-format` that suggests the pattern but doesn't block.

### Studio/tooling (RESOLVED 2026-04-21)

- **Package hosting**: private Git URL on the studio's GitHub org. Local file path during development; switch to Git URL in `manifest.json` once ready to share. No npm scoped registry, no tarballs. CI on consumer projects will need a PAT or deploy key to fetch the package.
- **Unity version floor**: **2022.3 LTS**. `package.json` sets `"unity": "2022.3"`. Forward-compat with 2023/6000 is a nice-to-have, not a gate.
- **Test coverage bar**:
  - **Required:** unit tests on Runtime (serialization round-trip, validation rules, schema migration).
  - **Required:** editor smoke tests (window opens from empty state, save/load round-trip via `[UnityTest]`).
  - **Out of scope:** full UI interaction tests.
- **ID prefix convention**: `core.*` for generic framework rules and cell types. Game-specific rules and cell types use the studio's existing short per-game prefix (e.g. `SNU`, `CFG`). Yarn Sort uses `ys.*` as a placeholder; confirm `YRN` vs `YS` with the team before Phase 5. Applies to `IValidationRule.Id`, `ICellTypeDefinition.TypeId`, and optionally `schemaVersion`.
- **Palette scope**: **per-project palette asset**. Yarn Sort ships one `YarnColorPalette` asset in the game project; every level references colors from it by `colorId` string. Extending the palette is a Layer 2 content change.

### Things deferred (not blocking Phase 1)
- Addressables integration for level assets
- Test Play harness wiring (stub for now)
- Editor theming/icons (placeholder icons in early phases)
- Localized display names for cell types

---

## Critical files (to be created in Phase 0)

- `Packages/com.hoppa.leveleditor.core/package.json`
- `Packages/com.hoppa.leveleditor.core/README.md`
- `Packages/com.hoppa.leveleditor.core/CHANGELOG.md`
- `Packages/com.hoppa.leveleditor.core/LICENSE.md`
- `Packages/com.hoppa.leveleditor.core/Runtime/Hoppa.LevelEditor.Core.Runtime.asmdef`
- `Packages/com.hoppa.leveleditor.core/Editor/Hoppa.LevelEditor.Core.Editor.asmdef`
- `Packages/com.hoppa.leveleditor.core/Tests/Runtime/Hoppa.LevelEditor.Core.Tests.asmdef`
- `Packages/com.hoppa.leveleditor.core/Tests/Editor/Hoppa.LevelEditor.Core.Editor.Tests.asmdef`

Existing project has only the default Unity 2022 template (`Packages/manifest.json` with URP + Input System), the `CLAUDE.md` file, and this `PLANNING.md`. Nothing to refactor or reuse — this is genuinely greenfield.

---

## Verification plan

End-to-end acceptance for the package (Phase 4 gate):

1. **Unit tests** (Runtime asm):
   - `LevelDocument` round-trips through `JsonLevelSerializer` with a 3-cell-type fixture.
   - `SchemaRegistry` migrates `v1 → v2` via a test migration.
   - `ColorBalanceRule` fires Error on deliberate imbalance; passes on balanced fixture.
2. **Editor smoke test** (Editor asm, `[UnityTest]`):
   - Open `LevelEditorWindow` with the bundled DemoColorGridGame profile.
   - Programmatically place cells via the session API.
   - Assert palette panel populates from registry.
   - Assert validation panel fires expected entries.
   - Save, reopen, assert model equality.
3. **Manual Yarn Sort dogfood** (Phase 5 gate, in game project):
   - Author 3 real levels end-to-end through the UI following the mockup.
   - Test Play enters Play Mode with the level loaded.
   - Open a saved `.json` in Claude Code, hand-edit a field, reopen in the editor — round-trip clean.
4. **Second-game fidelity** (deferred — post-Yarn-Sort):
   - Covered in Phase 6 (deferred). When the real driver game is
     picked, author a level end-to-end against the package and treat
     any required package edit as a signal to refactor.

Phase 5 (Yarn Sort dogfood) is the acceptance bar for this project.
The two-layer split will be validated against a real second game
later (see Phase 6 — deferred).
