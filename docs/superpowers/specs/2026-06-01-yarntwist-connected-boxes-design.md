# YarnTwist — Connected Boxes

**Date:** 2026-06-01
**Layer:** 1 + 2 (Layer-1 context-action extension → UPM tag bump; Layer-2 YarnTwist mechanic)
**Status:** Approved

## Source of truth

The YarnTwist **game** project (`E:/Projects/Hoppa/YarnTwist`, Layer-2 under `Assets/_YAT/`)
is the authority for the data structure. Eliran already implemented Connected Boxes
game-side (commit `56608a5`, branch `itay-main`). The editor must export JSON the game
already reads. This "mirror the game's structure" rule also governs the **two further
mechanics planned this week** — investigate the game first, then design the editor side.

### What the game actually does (verified from code)

There is **no connection object** — no `ConnectionId`, no `Source`/`Target`, no
connections array. A connected box is an ordinary `BottomConfig` cell with:

- `BottomType = ConnectedBox (6)` — a new ordinal Eliran added to `YATBottomType`.
- `Direction` = a PascalCase string (`"Up"/"Down"/"Left"/"Right"`) pointing at its partner.

A pair is **implied by adjacency + reciprocal Direction**: box `(2,4)` `Direction:"Right"`
↔ box `(3,4)` `Direction:"Left"`. The game resolves the partner at runtime via
`GridPosition + directionVector` (`YATBoxPrefabComponent.InitConnectedBox`). `OnClick` /
`SetActiveBox` cascade to the partner — open-both / clear-both. Each box keeps its own
`ColorType`. The game does **not** validate reverse links (a `ConnectedBox` may point at a
plain box and break at runtime), so **the editor must guarantee reciprocal pairing.**

Game schema (`Assets/_YAT/Scripts/Gamelogic/Managers/YATLevelManager.cs`):

```csharp
public class BottomConfig {
    public YATVector2Int  Position;    // { "x":int, "y":int }  (lowercase keys)
    public YATBottomType  BottomType;  // int ordinal
    public YATColorType   ColorType;   // int ordinal
    public YATDirectionType Direction; // STRING name; used by ArrowBox, Tunnel, ConnectedBox
    public bool           Hidden;
    public TunnelQueueEntry[] Queue;
}
public enum YATBottomType   { None, Wall, Empty, Color = 3, ArrowBox = 4, Tunnel = 5, ConnectedBox = 6 }
public enum YATDirectionType { None, Up, Down, Left, Right }   // serialized as string
```

Target export for a connected pair (must match exactly):

```json
{ "Position": {"x":2,"y":4}, "BottomType":6, "ColorType":3, "Hidden":false, "Direction":"Right" },
{ "Position": {"x":3,"y":4}, "BottomType":6, "ColorType":7, "Hidden":false, "Direction":"Left" }
```

## The mechanic (gameplay, for analyzer modeling)

Two orthogonally-adjacent regular boxes are linked. If either becomes accessible, both
open (even one with no path out). Tapping either **activates both**: both release their
yarn balls and are removed together, then accessibility recalculates. Colors are **fully
independent** (no color-match constraint). A box belongs to at most one connection.

## Scope

**In:** editor authoring (Connect/Un-connect, white outline, validation), export, and
analyzer + auto-fill support. **Out:** game-runtime behavior (already done by Eliran).

## Design

### 1. Data model — Layer 2 (`YarnBoxCell`)

Add one field — an exact mirror of the game's per-box `Direction`:

```csharp
[JsonProperty("connectedDir", NullValueHandling = NullValueHandling.Ignore)]
public YarnDirection? ConnectedDir { get; set; }   // null = not connected
```

Connected iff `ConnectedDir != null`; partner = orthogonal neighbor in that direction.
A connection is **honored** (export/analyzer) only when reciprocal and both endpoints are
regular boxes. It stays a `yt.box` cell (matches the GDD UX: right-click a regular box to
connect; un-connect returns it to a normal box) — no new palette type. `GameData`/grid
serialization, undo, and clone all ride the existing machinery for free.

### 2. Layer-1 context-action extension (UPM change → tag `v0.5.20`)

Today `ICellContextActions.GetContextActions(cell, registry)` cannot see the cell's
position/session, and `CellContextAction` can only replace the clicked cell. Connect/
Un-connect needs neighbor info and two-cell mutation. Generalize, staying generic:

- `GetContextActions` takes a context struct
  `CellActionContext { ICellData Cell; CellTypeRegistry Registry; LevelEditorSession Session; CellRef CellRef; }`.
- `CellContextAction` gains a second flavor: a free-form `Action<LevelEditorSession>` apply
  (alongside the existing `Func<ICellData> create`).
- `GridCellPopup` pushes **one** undo snapshot, then runs `apply(session)` (new) or the
  existing `SetCell(create())`, then revalidates.

In-repo implementers updated to the new signature: `YarnBoxCellDefinition`,
`YarnArrowBoxCellDefinition`. **YAK (YarnKingdom) must be synced** to the same signature
when this ships.

### 3. Editor authoring (`YarnBoxCellDefinition`)

- **Context menu:** when unconnected, emit `Connect Pair: <Dir>` only for directions whose
  neighbor exists and is an **unconnected regular box** (`yt.box`). When connected, emit a
  single `Un-connect`. Both are free-form apply actions mutating *both* boxes'
  `ConnectedDir` in one undo step (set reciprocal on connect; clear both on un-connect).
- **Outline:** `DrawCell` draws a clear **white border** when `ConnectedDir != null`, plus a
  thicker white bar on the edge facing the partner so the link reads as a continuous seam
  between the two boxes.

### 4. Validation (`YarnConnectedBoxRule`, new Layer-2 rule)

Emit an **Error** for any box whose `ConnectedDir` is dangling: neighbor missing, neighbor
not a regular box, or non-reciprocal. This is the guard the game lacks (it never validates
reverse links) and the safety net for grid edits that erase/repaint one half of a pair.
Wire into `YarnTwistProfile.asset` rules.

### 5. Export (`YarnMasterLevelExporter.BuildBottomConfigs`)

When `cell is YarnBoxCell box && box.ConnectedDir != null`:

- emit `BottomType = 6` (ConnectedBox) instead of the normal box ordinal,
- add `["Direction"] = box.ConnectedDir.Value.ToString()` (yields `"Up"/"Down"/"Left"/"Right"`,
  same mechanism arrow boxes already use),
- keep per-box `ColorType` + `Hidden`.

The `BottomType` ordinal `6` comes from a new `yt.connectedbox → 6` entry in
`YarnCellTypeMapping.asset`; the exporter resolves `BottomType` via that key when the box is
connected, else the normal `yt.box` key. Reciprocity is guaranteed because both halves are
emitted and the connect action always sets both. No de-dup / id assignment needed — output
is a per-cell branch.

### 6. Analyzer + auto-fill (`YarnTwistLevelAnalyzer`, `YarnTwistSpoolAutofiller`)

The analyzer abstracts the grid as a multiset of tappable items (boxes tappable any time;
arrow boxes gate on a prereq neighbor; tunnels drain a queue). Connections slot in:

- **Model:** add `int[] Partner` (−1 if none). `BuildItems` detects reciprocal connected
  boxes and links their item indices.
- **Atomic co-tap:** `ApplyTap(i)` on a connected box also consumes its partner — both
  colors' balls enter the belt (9 each), both marked tapped — then one `ResolveMatches`.
  This models "tap either → both release & clear together," and satisfies any arrow-box
  prereq that depended on the partner.
- **No double-count:** only the **canonical member** (lower item index) of a pair is offered
  as a move; tapping it co-activates the higher-index partner. Keeps win-path counts correct.
- **Undo + rollouts:** the DFS undo restores both members' tapped state; the Monte-Carlo
  chooser offers the canonical member and scores it by the **summed demand of both colors**.

**Auto-fill is correct-by-delegation.** `YarnTwistSpoolAutofiller` builds its per-color
inventory from the grid (connected boxes are still `YarnBoxCell` → counted once each = 9
balls = 3 spools of their color; **balance unchanged**) and delegates *all* solvability /
difficulty scoring to `analyzer.Analyze(candDoc, …)` with the real grid attached
(`ShallowCopyWithTop` keeps `Grid = src.Grid`). So once the analyzer models pairs, candidate
evaluation accounts for connected clearing with **no structural autofiller change**.

Auto-fill deliverables are therefore: (a) a connected-box autofiller test (balanced spools +
solvable result), and (b) an explicit comment that per-color inventory is unchanged so a
future session doesn't "fix" it wrongly.

**Tuning watch (not a v1 blocker):** connected pairs shift measured difficulty (two boxes /
18 balls / possibly two colors clear at once → more conveyor pressure, fewer independent
orderings). The `WinPathTargetByDifficulty` / `WinRateTargetByDifficulty` curves were tuned
on connection-free levels, so connection-heavy levels may land off-target for the same curve.
Flag for retune after real levels exist; do not pre-tune.

## Testing (EditMode — agent cannot run Unity, hand to user)

- **Analyzer:** pair clears as one tap (count vs. equivalent independent boxes); a pair lets
  a box clear that would otherwise strand its partner; canonical representative prevents 2×
  inflation; arrow-box prereq satisfied via partner co-tap.
- **Exporter:** reciprocal pair → two `BottomConfig`s with `BottomType:6` + correct reciprocal
  `Direction` strings + per-box `ColorType`; un-connected box → normal box ordinal, no
  `Direction`.
- **Validation:** dangling / non-reciprocal connection → Error; healthy pair → clean.
- **Context actions:** direction filtering (only valid neighbors offered); connect sets
  reciprocal dirs; un-connect clears both; single undo restores both.
- **Auto-fill:** grid with a connected pair → balanced spools + solvable analysis.

## Files touched

**Layer 1 (`Packages/com.hoppa.leveleditor.core/Editor/`):**
- `Registry/ICellContextActions.cs` — new `CellActionContext` parameter.
- `Registry/CellContextAction.cs` — free-form `Action<LevelEditorSession>` apply flavor.
- `Registry/CellActionContext.cs` — new struct (+ `.meta`).
- `Panels/GridCellPopup.cs` — invoke apply path with one undo snapshot.

**Layer 2 (`Assets/YarnTwist/`):**
- `Runtime/YarnBoxCell.cs` — `ConnectedDir` field.
- `Editor/Cells/YarnBoxCellDefinition.cs` — context actions + outline.
- `Editor/Cells/YarnArrowBoxCellDefinition.cs` — signature update.
- `Editor/Validation/YarnConnectedBoxRule.cs` — new rule (+ `.meta`).
- `Editor/YarnMasterLevelExporter.cs` — connected-box export branch.
- `Editor/Analysis/YarnTwistLevelAnalyzer.cs` — `Partner[]` + atomic co-tap.
- `Editor/Analysis/YarnTwistSpoolAutofiller.cs` — inventory comment only.
- `Assets/YarnTwist/Data/Config/YarnCellTypeMapping.asset` — `yt.connectedbox → 6`.
- `Assets/YarnTwist/Data/Config/YarnTwistProfile.asset` — wire `YarnConnectedBoxRule`.
- `Assets/YarnTwist/Tests/Editor/` — analyzer + exporter + validation + autofiller fixtures.

## Rollout

Layer-1 change → tag `v0.5.20`; bump YarnTwist `manifest.json` pin; sync the
context-action signature into YAK. Update `CURRENT_TASK.md` / `SESSION_NOTES.md`.
EditMode verification handed to the user (unity-mcp-cli returns 401 from the agent).

## Out of scope / deferred

- Auto-cleanup of a partner when its box is erased/repainted (needs a Layer-1 "cell removed"
  hook) — validation Error covers it for v1.
- Generator producing connected boxes (designer authors them manually for now).
- Difficulty-curve retune for connection-heavy levels (revisit with real data).

## Note for the two upcoming mechanics this week

Same workflow each time: dispatch a read-only agent into `E:/Projects/Hoppa/YarnTwist`,
find the game-side `BottomConfig` / enum / runtime classes + a sample `level_config.json`,
adopt those identifiers exactly, then brainstorm the editor side (authoring + export +
analyzer/auto-fill) before writing a per-mechanic spec. The analyzer's `BuildItems` +
`ApplyTap` and the exporter's `BuildBottomConfigs` are the recurring extension points; the
autofiller stays correct-by-delegation as long as the analyzer models the new mechanic.
