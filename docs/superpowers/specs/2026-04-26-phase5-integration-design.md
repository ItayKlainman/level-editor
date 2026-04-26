# Phase 5 Integration Design: Yarn Twist Level Editor → Game Pipeline

**Date:** 2026-04-26
**Status:** Approved — ready for implementation planning

---

## Goal

Wire the `hoppa-level-editor-core` level editor to the Yarn Twist game project so that
saving a level from the editor automatically updates the game's master
`level_config.json` in a format the game can load without any manual steps.

---

## Context (key findings from pre-design scan)

- The game reads one master `Assets/_YAT/Configs/Resources/Configs/level_config.json`
  loaded via `Resources.LoadAll<TextAsset>("Configs")` at boot. Per-level editor `.json`
  and `.asset` files are invisible to the game.
- The game uses integer enums (`YATColorType`, `YATBottomType`) where the editor uses
  string IDs (`"pink"`, `"yt.box"`). A generic mapping layer is required.
- ArrowBox and Tunnel have **zero implementation** in the game. Stubs with TODO
  comments are sufficient for Phase 5.
- The `−3.5f` world-space grid offset hardcodes an 8-wide grid. This is **not changed**
  in Phase 5 — deferred to a future grid-dimension pass.
- Newtonsoft in the game uses default settings (`MissingMemberHandling = Ignore`), so
  extra fields from the editor JSON are silently dropped. The loader is forgiving.
- The win/lose flow in the game is fully commented out — not a blocker for data
  integration but noted for QA scope.

---

## Components

### 1. `StringIntMapping` — generic string→int converter (Layer 1 package, new)

**Location:** `Packages/com.hoppa.leveleditor.core/Editor/Mapping/`

A reusable ScriptableObject mapping string keys to integer values. Designed to
handle any string-to-enum translation across current and future game projects.

```
StringIntMapping : ScriptableObject
  [SerializeField] List<StringIntEntry> _entries
  bool TryGet(string key, out int value)
  int  Get(string key, int fallback = 0)
  IReadOnlyList<StringIntEntry> Entries { get; }

StringIntEntry : [Serializable]
  string Key
  int    Value
```

**Usage:** The `YarnMasterLevelExporter` holds two `StringIntMapping` assets — one
for color IDs (`"pink"→7`) and one for cell type IDs (`"yt.box"→3`).

**Inspector menu:** `Hoppa/Level Editor/String-Int Mapping`

---

### 2. `LevelExporterAsset` — exporter base class (Layer 1 package, refactor)

**Location:** `Packages/com.hoppa.leveleditor.core/Editor/Exporters/`

Currently `GameProfile._exporters` is `List<ScriptableObjectExporter>`, which locks
all exporters into the "write `.asset` next to `.json`" pattern.
`YarnMasterLevelExporter` writes to a different location and needs a different base.

**Change:** introduce `LevelExporterAsset : ScriptableObject, ILevelExporter` as the
new base. `ScriptableObjectExporter` inherits from it (no behaviour change).
`GameProfile._exporters` becomes `List<LevelExporterAsset>`.

```
LevelExporterAsset : ScriptableObject, ILevelExporter   (new abstract base)
  └── ScriptableObjectExporter   (existing, inherits from LevelExporterAsset)
  └── YarnMasterLevelExporter    (new, inherits from LevelExporterAsset)
```

Impact: two file edits in the package, zero behaviour change for existing exporters.

---

### 3. `YarnMasterLevelExporter` — the integration exporter (YarnTwist layer, new)

**Location:** `Assets/YarnTwist/Editor/YarnMasterLevelExporter.cs`

A `LevelExporterAsset` subclass. Runs on every Save alongside the existing
`JsonExporter`. Transforms the editor's `LevelDocument` into the game's runtime
schema and upserts it into the master `level_config.json`.

**Inspector fields:**

| Field | Type | Purpose |
|---|---|---|
| `_outputPath` | `string` | Absolute path to game's `level_config.json` |
| `_colorMapping` | `StringIntMapping` | `"pink"→7`, `"blue"→1`, etc. |
| `_cellTypeMapping` | `StringIntMapping` | `"yt.box"→3`, `"yt.wall"→1`, etc. |
| `_defaultRewardScoreType` | `string` | Stubbed reward type (default `"Coin"`) |
| `_defaultRewardAmount` | `int` | Stubbed reward amount (default `10`) |

**Export logic:**

1. Parse integer level key from `document.LevelId`: extract trailing digit sequence and
   parse as int (e.g. `"level_001"` → `1`, `"level_025"` → `25`). Log a warning and
   skip export if no digit sequence is found.
2. Build `BottomConfig[]` from `document.Grid.Cells`:
   - Map `cell.CellTypeId` → `BottomType` int via `_cellTypeMapping`
   - Map `cell.ColorId` → `ColorType` int via `_colorMapping` (default `0` for
     non-coloured cells)
   - Emit `Position { x, y }` from grid index
   - Emit `Direction` string for ArrowBox/Tunnel (pass-through from cell data)
   - Emit `Hidden` bool for Box/ArrowBox cells
   - Emit `Queue[]` for Tunnel cells (each entry: `ColorType` int + `Hidden` bool)
3. Build `TopConfig[]` from `document.TopSection.columns` (always emit exactly 4 entries;
   pad with empty `WinderConfigs[]` if the editor has fewer than 4 columns configured):
   - `Index` = column index (0–3)
   - `WinderConfigs[]` from column spools: `ColorType` int + `Hidden` bool
4. Read existing `level_config.json` (or start with empty `{ }` if missing).
5. **Upsert** `LevelConfigs[key]` with new BottomConfigs + TopConfigs.
6. **Stub** `LevelRewardConfigs[key]` only if the key is not already present
   (preserves hand-authored reward values).
7. Write back to `_outputPath` with `Formatting.Indented`.

**Error handling:** If `_outputPath` is empty or `_colorMapping` / `_cellTypeMapping`
is null, log a warning and skip (don't block the rest of the save pipeline).

**Inspector menu:** `Hoppa/Yarn Twist/Master Level Exporter`

---

### 4. Game data model extensions (YarnTwist game project)

**File:** `Assets/_YAT/Scripts/Gamelogic/Managers/YATLevelManager.cs`

#### `YATBottomType` enum additions

```csharp
//TODO: ArrowBox — a colored box with a directional arrow overlay. When triggered,
// pushes yarn balls in the specified direction. Requires Direction field on
// BottomConfig and a corresponding YATArrowBoxPrefabComponent + prefab.
ArrowBox = 4,

//TODO: Tunnel — a cell that holds a queue of colored boxes delivered sequentially.
// Direction indicates which side the output faces. Requires Queue field on
// BottomConfig and a corresponding YATTunnelPrefabComponent + prefab.
Tunnel = 5,
```

#### `BottomConfig` new fields

```csharp
//TODO: Direction — used by ArrowBox (direction the arrow points) and Tunnel
// (direction of output). Values: "up", "down", "left", "right".
// Ignored by the spawner until ArrowBox/Tunnel prefabs are implemented.
public string Direction;

//TODO: Hidden — when true, this cell's color is concealed from the player until
// revealed by gameplay. Currently unused in spawn logic.
public bool Hidden;

//TODO: Queue — used by Tunnel cells only. Ordered list of colored boxes waiting
// to be delivered. See TunnelQueueEntry. Null for all other cell types.
public TunnelQueueEntry[] Queue;
```

#### New stub class

```csharp
//TODO: TunnelQueueEntry — one item in a tunnel's delivery queue. Maps directly
// to the editor's tunnel queue format { colorId, hidden }.
public class TunnelQueueEntry
{
    public YATColorType ColorType;
    public bool Hidden;
}
```

#### `WinderConfig` new field

```csharp
//TODO: Hidden — when true, this spool's color is concealed until revealed by
// gameplay. Currently unused in YATWinderPrefabComponent.Init.
public bool Hidden;
```

---

### 5. Game spawner stubs (`YATGameManagerComponent.cs`)

Two commented-out stub branches added inside `InitBoxes`, after the existing
`YATBottomType.Color` block:

```csharp
//TODO: ArrowBox spawn — instantiate _arrowBoxPrefab at this position and call
// Init(bottomConfig) to configure its direction and color.
// Wire _arrowBoxPrefab in the Inspector (YATArrowBoxPrefabComponent).
// if (bottomConfig.BottomType == YATBottomType.ArrowBox) { ... }

//TODO: Tunnel spawn — instantiate _tunnelPrefab at this position and call
// Init(bottomConfig) to configure its direction and queue.
// Wire _tunnelPrefab in the Inspector (YATTunnelPrefabComponent).
// if (bottomConfig.BottomType == YATBottomType.Tunnel) { ... }
```

---

### 6. Stub prefab components (YarnTwist game project, new files)

**`YATArrowBoxPrefabComponent.cs`**
```csharp
//TODO: ArrowBox prefab component. Renders a colored box with a directional arrow
// overlay. Call Init(BottomConfig) to set color and direction.
// When triggered, launches yarn balls in the arrow's direction toward adjacent cells.
// Inherits from YATLogicMonoBehaviour (same pattern as YATBoxPrefabComponent).
```

**`YATTunnelPrefabComponent.cs`**
```csharp
//TODO: Tunnel prefab component. Renders a tunnel cell that sequentially delivers
// colored boxes from its queue. Call Init(BottomConfig) to load the queue.
// Direction indicates which face is the output. Queue entries are consumed in order.
// Inherits from YATLogicMonoBehaviour.
```

---

### 7. Editor bug fixes (existing files)

| Bug | Fix | File(s) |
|---|---|---|
| Double `.v1` in schemaVersion | Set `GameProfile._schemaId` to `"yarn-twist"` (not `"yarn-twist.v1"`) | Inspector asset (no code change) |
| `CellTypeId` leaking into JSON | Add `[JsonIgnore]` to `CellTypeId` property on every `ICellData` implementation | `YarnEmptyCell.cs`, `YarnWallCell.cs`, `YarnBoxCell.cs`, `YarnTunnelCell.cs`, `YarnArrowBoxCell.cs` |

---

## Data flow (end-to-end after integration)

```
Designer saves level in Level Editor
  │
  ├─► JsonExporter          → writes per-level .json (editor source of truth)
  ├─► YarnSortExporter      → writes per-level .asset (runtime convenience SO)
  └─► YarnMasterLevelExporter
        │  reads existing level_config.json from game project
        │  transforms LevelDocument → game runtime schema
        │    · string colorId  → YATColorType int (via StringIntMapping)
        │    · string cellTypeId → YATBottomType int (via StringIntMapping)
        │    · levelId string  → int key (parse digits)
        │  upserts this level entry
        │  stubs LevelRewardConfigs entry if new level
        └─► writes level_config.json to game's Resources/Configs/
```

---

## What is NOT changing in Phase 5

- `−3.5f` world-space x-offset in `YATGameManagerComponent` — deferred, grid offset
  pass is a future task
- Win/lose flow (commented out in game) — out of scope for data integration
- `TopConfig.Index` field — dead in game runtime; exporter emits it for completeness
- `Quantity` field — dead in game runtime; exporter omits it
- `current_configs.json` — no changes needed (already points to `level_config`)
- Camera auto-fit (`YATCameraScaler`) — not wired, deferred

---

## Files touched summary

### `hoppa-level-editor-core` (editor repo)
| File | Action |
|---|---|
| `Editor/Exporters/LevelExporterAsset.cs` | New |
| `Editor/Exporters/ScriptableObjectExporter.cs` | Extend `LevelExporterAsset` instead of `ScriptableObject` |
| `Editor/Infrastructure/GameProfile.cs` | `_exporters` type: `List<ScriptableObjectExporter>` → `List<LevelExporterAsset>` |
| `Editor/Mapping/StringIntMapping.cs` | New |
| `Editor/Mapping/StringIntEntry.cs` | New |
| `Assets/YarnTwist/Editor/YarnMasterLevelExporter.cs` | New |
| `Assets/YarnTwist/Runtime/Yarn*Cell.cs` (×5) | Add `[JsonIgnore]` to `CellTypeId` |

### `YarnTwist` (game repo)
| File | Action |
|---|---|
| `YATLevelManager.cs` | Add enum values + new fields + `TunnelQueueEntry` class |
| `YATGameManagerComponent.cs` | Add TODO stub branches in `InitBoxes` |
| `YATArrowBoxPrefabComponent.cs` | New stub |
| `YATTunnelPrefabComponent.cs` | New stub |

---

## Open items (post-Phase 5)

- Confirm `YATColorType` int values with the game team and lock the color mapping SO
- Grid offset `−3.5f` fix (when grid dimensions become variable)
- ArrowBox + Tunnel full prefab implementation
- Camera auto-fit wiring
- Win/lose flow restoration
