# Bus Buddies — Hidden Buses · Hidden Pixels · Connected Buses (design)

**Date:** 2026-07-15
**Profile:** Bus Buddies (Layer-2) + minimal generic Layer-1 additions
**Authoring model:** hand-authoring only (no autofill-generation, no analyzer/difficulty modeling of these mechanics)

## 1. Goal

Let designers hand-author three mechanics that the Bus Buddies **game** already supports, and round-trip them through export/import against the real game schema:

1. **Hidden Buses** — a bus whose color is concealed until it reaches the active row.
2. **Hidden Pixels** — grid pixels concealed until revealed in play.
3. **Connected Buses** — two buses linked so they can only clear together.

Success = a designer can place each mechanic in the editor, export a level whose JSON matches the game schema exactly, re-open (import) it losslessly, and the mechanic behaves correctly in the real game.

## 2. Source of truth — the BB game schema

Verified against the BB game project (`E:/Projects/Hoppa/BusBuddies`, branch `main`, commit `73b0897 "pixel hidden"`), model classes in `Assets/_BUB/Scripts/Gamelogic/Managers/BUBLevelManager.cs`. JSON is Newtonsoft with **default settings**: PascalCase member names, **enums as integer ordinals**.

Per-level `LevelConfig`:

```csharp
public class LevelConfig {
    public BUBLevelType LevelType;              // enum: None=0, Hard=1, SuperHard=2
    public int SlotsAmount;
    public int Width;
    public int Height;
    public BusColumnConfig[] BusColumnConfigs;  // one entry per column/queue
    public BUBConnectedBuses[] ConnectedBuses;  // ← Connected Buses
    public BUBColorType[] PixelColors;          // flat, len = W*H
    public int[] HiddenPixels;                  // ← Hidden Pixels (sparse index list)
}
public class BusColumnConfig { public BusConfig[] BusConfigs; }
public class BusConfig { public BUBColorType ColorType; public int Capacity; public BUBBusType BusType; }   // ← Hidden Buses
public class BUBConnectedBuses { public BUBConnectedBus BusA; public BUBConnectedBus BusB; }
public class BUBConnectedBus  { public int ColumnIndex; public int Index; }

public enum BUBBusType   { None = 0, Hidden = 1 }
public enum BUBPixelType { None = 0, Hidden = 1 }   // runtime only, derived from HiddenPixels membership
```

`BUBColorType` ordinals are explicitly assigned (stable): None=0, Blue=1, Cyan=2, Yellow=3, Green=4, Magenta=5, Orange=6, Pink=7, Purple=8, Red=9, … BrownVeryDark=36.

### Load-bearing indexing note (Hidden Pixels)

The game reads the two pixel arrays with **different, transposed formulas**:

- `PixelColors` — `index = y * width + x` (`BUBPixelService.cs:134`).
- `HiddenPixels` — membership tested against `position = x * width + y` (`BUBPixelService.cs:170`).

We **mirror the game exactly**: the exporter must emit each hidden cell's index as `x * width + y` (NOT the `PixelColors` formula). Because we use the same `(x,y) → x*width+y` mapping the game tests against, the correct visual cells hide — the two arrays' internal inconsistency does not misplace our data. This is covered by an explicit export test and confirmed by one in-game spot-check. We also leave a `SESSION_NOTES` flag that the game's two formulas differ (a latent game-side bug) so the game team can confirm/reconcile.

## 3. Editor-side current state (what already exists)

- `BusEntry { string ColorId; int Capacity; bool Hidden; int ConnectedId = -1 }` (`Assets/BusBuddies/Runtime/BusQueueData.cs`) — `Hidden` and `ConnectedId` fields already present.
- `BusBuddiesQueuePanel` (`Editor/TopSection/`) — already has a per-bus **Hidden toggle**; no connected-bus UI ("pairing deferred / data-only").
- `BusBuddiesGameLevelExporter` — already emits `BusType:1` for hidden buses; does **not** emit `ConnectedBuses` or `HiddenPixels`.
- `BusBuddiesGameLevelImporter` — reads `BusType==1 → Hidden`; does not read connections or hidden pixels.
- `BBPixelCell { string ColorId }` (`Runtime/`) — **no hidden flag** (the gap).
- Grid canvas (`Packages/.../Editor/Panels/GridCanvasPanel.cs`) — tool model `GridEditTool { Paint, Select, Delete, Move }`; per-cell `DrawCell`; per-profile `CanvasOverlay` hook; `ToolbarPanel` renders mode toggles gated by `Show*` flags.
- YarnTwist reference (complete): `YarnSpoolConnection` (static ops incl. `ConnectionsDeadlock`), `YarnConnectedSpoolRule`, connect/disconnect popup UI, reciprocal export.

## 4. Feature designs

### 4.1 Hidden Buses — verify only

No new production code expected. Deliverable = an EditMode round-trip test: author a hidden bus → export → re-import → assert still `Hidden`, and assert the queue-panel toggle reads/writes `BusEntry.Hidden`. If the round-trip reveals a gap, fix minimally.

### 4.2 Hidden Pixels — new

**Model.** Add `bool Hidden` to `BBPixelCell` (`[JsonProperty("hidden")]`, default `false`). A hidden pixel retains its `ColorId`.

**Rendering.** `BBPixelCellDefinition.DrawCell` draws a dim + hatch overlay on top of the color swatch when `pixel.Hidden` (a few `EditorGUI.DrawRect` diagonal marks + a translucent darken). No new overlay subsystem — `DrawCell` already receives the cell.

**Gesture — generic Layer-1 "flag paint" tool.**
- Add `GridEditTool.Hide` to the Layer-1 enum.
- Add `ICellFlagPainter { string ToggleLabel { get; } void Toggle(LevelEditorSession session, CellRef cell); }` and a `FlagPainter` property on `GameProfile` (null = feature absent).
- `GridCanvasPanel.HandleEvents`: in Hide mode, left-click and left-drag call `profile.FlagPainter.Toggle(session, cell)` with an undo snapshot on mouse-down (mirrors the `Paint` case; drag toggles are idempotent-safe — see below).
- `ToolbarPanel`: a `✦ Hide` toggle (label from `FlagPainter.ToggleLabel`) shown only when `Profile.FlagPainter != null`, wired like the existing Order/Generate/Image toggles; selecting it sets `session.ActiveTool = GridEditTool.Hide`.
- **Drag semantics:** to avoid a drag flipping the same cell on/off repeatedly, `Toggle` sets/unsets based on the value at the stroke's first cell (paint-hidden vs erase-hidden determined at mouse-down), tracked per-stroke — matching a natural "paint the flag" feel. (Simplest acceptable alternative if per-stroke tracking is fiddly: toggle only on distinct cells entered during the drag.)

BB supplies `BusBuddiesHiddenPixelPainter : ICellFlagPainter` — `ToggleLabel => "✦ Hide"`; `Toggle` flips `Hidden` on a `BBPixelCell`, no-op on empty/other cells. Wired into `BusBuddiesProfile._flagPainter`.

**Export.** `BusBuddiesGameLevelExporter.BuildHiddenPixels` → `int[]` of `x * width + y` for every cell that is a hidden `BBPixelCell`. Emitted as `LevelConfig.HiddenPixels`.

**Import.** `BusBuddiesGameLevelImporter` reads `HiddenPixels[]`, converts each index back to `(x,y)` via the inverse of `x*width+y`, and sets `Hidden = true` on the corresponding pixel cell.

### 4.3 Connected Buses — new

**Model.** Reuse `BusEntry.ConnectedId` (shared int id per pair; `-1` = unconnected). A pair = exactly two buses sharing an id.

**Static ops — `BusConnection`** (new, `Editor/TopSection/`, UI-agnostic; mirrors `YarnSpoolConnection`):
- `BuildConnInfo(BusQueueData, out members, out pendingId)` — group by `ConnectedId` → member `(column, index)` lists; identify any in-progress single-member id.
- `AllocId(BusQueueData)` — next free id.
- `Connect(session, queue, busRef, id)` / `DisconnectGroup(session, queue, id)` — mutate with undo.
- `ConnectionsDeadlock(BusQueueData) → bool` — color-blind soft-lock check (see below).

**UI — `BusBuddiesQueuePanel`.** Add a "Connect" mode/action: click bus A (anchor), click bus B → `AllocId` + `Connect` both. A link badge (shared id number) renders on each connected bus, plus a connector line when both are visible. A "Disconnect" affordance (button or right-click) calls `DisconnectGroup`. Any two buses may connect (game imposes no adjacency); a bus already in a pair can't join a second.

**Export.** `BusBuddiesGameLevelExporter.BuildConnectedBuses` → for each id-group of two, emit `BUBConnectedBuses { BusA:{ColumnIndex,Index}, BusB:{ColumnIndex,Index} }` resolving each bus's live column and in-column index. Emitted as `LevelConfig.ConnectedBuses`. Incomplete groups (1 member) are dropped at export (and flagged by validation — see below).

**Import.** Read `ConnectedBuses[]`; for each pair, `AllocId` a fresh id and set it on both referenced `(ColumnIndex, Index)` buses.

**Validation — `BBConnectedBusRule`** (`ValidationRuleBase`, `Scope = Level`, added to `BusBuddiesProfile._rules`):
- Error if any `ConnectedId` group has ≠ 2 members (incomplete or over-linked).
- Error if the two members are not reciprocal / a bus appears in >1 group (structurally impossible with shared-id but assert anyway).
- **Deadlock Error (blocks export):** run `ConnectionsDeadlock`.

**Deadlock model** (adapted from YarnTwist, color-blind / structural): simulate each column's head advancing monotonically; an unconnected head clears freely; a connected head clears only when its partner is *simultaneously* at its own column head; if a fixpoint is reached with heads remaining that can never satisfy their partner constraint → deadlock. O(total buses). It validates only connection-structural winnability — **not** color-solvability (that remains `BusBuddiesAnalyzer`'s job). Document this boundary in the rule's summary.

## 5. Layer-1 vs Layer-2 split

**Layer-1 (generic, ships in the package → version bump + tag):**
- `GridEditTool.Hide` enum value.
- `ICellFlagPainter` interface + `GameProfile.FlagPainter`.
- `GridCanvasPanel` Hide-tool handling.
- `ToolbarPanel` `✦ Hide` toggle gated on `Profile.FlagPainter != null` + `LevelEditorWindow` wiring.

**Layer-2 (Bus Buddies):**
- `BBPixelCell.Hidden` + `BBPixelCellDefinition` hatch rendering.
- `BusBuddiesHiddenPixelPainter`.
- `BusConnection` static ops + `BBConnectedBusRule`.
- `BusBuddiesQueuePanel` connect/disconnect UI.
- Exporter `BuildHiddenPixels` + `BuildConnectedBuses`; importer reads for both.
- `BusBuddiesProfile.asset` wiring: `_flagPainter`, `_rules += BBConnectedBusRule`.

## 6. Deployment

Same rollout as v0.9.0:
1. Bump `Packages/com.hoppa.leveleditor.core/package.json` (minor — additive Layer-1 API) + tag.
2. Re-mirror the BB Layer-2 stack into the game project's `Assets/BusBuddies/` copy; re-pin `Packages/manifest.json` to the new tag.
3. Game-project compile-check + push = **lead step** (agent's Unity MCP is bound to editor-core; can't compile the game or screenshot the custom window there).

## 7. Testing (EditMode)

- **Hidden Buses:** round-trip (author → export → import → still Hidden); toggle reads/writes `BusEntry.Hidden`.
- **Hidden Pixels:** export index formula (`x*width+y`, asserted against a hand-computed fixture); full round-trip; `BusBuddiesHiddenPixelPainter.Toggle` flips only pixel cells (no-op on empty); `DrawCell` doesn't throw when `Hidden`.
- **Connected Buses:** export/import round-trip (id-group ↔ `ConnectedBuses[]`, live positions correct); each validation branch — incomplete group, over-linked, and a **known soft-lock fixture** → `ConnectionsDeadlock == true` (Error); a known-good pair → no error.
- Full suite green, 0 regressions vs the current baseline.

## 8. Out of scope (explicitly deferred)

- Autofill-generation of any of the three mechanics (analyzer/sim difficulty modeling of hidden/connected — the sim carries the arrays but does not model reveal/lock difficulty; unchanged).
- Adjacency constraints on connections (game imposes none).
- Reconciling the game's transposed `HiddenPixels`/`PixelColors` index formulas (game-side; we mirror + flag).
- Zoom, and any non-BB profile consuming `ICellFlagPainter` (built generic, exercised only by BB).
