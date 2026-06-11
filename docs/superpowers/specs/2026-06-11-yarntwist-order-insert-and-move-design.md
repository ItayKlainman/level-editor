# YarnTwist — Order panel: new-level insert + move-to-position

**Date:** 2026-06-11
**Layer:** 2 (YarnTwist) — no Layer-1 / UPM change, **no tag bump**. Needs sync to the game repo mirror.
**Files:** `Assets/YarnTwist/Editor/YarnMasterLevelExporter.cs`,
`Assets/YarnTwist/Editor/YarnLevelOrderPanel.cs`, plus tests.

---

## Problem

Two related pain points in the level-ordering workflow:

1. **Export overwrites existing levels.** On `Export`, the master-JSON key is parsed as the
   *last integer in the filename* (`YarnMasterLevelExporter.Export`,
   `level002_new` → `2`). The upsert only treats a level as "existing" when an entry's
   `levelId` exactly equals the full filename. A newly-named level whose number collides
   with an existing slot (`level002_new` vs `level_002`) therefore falls back to key `2`
   and **silently overwrites** the real `level_002` entry — data loss.

2. **Repositioning costs a long drag.** The Order panel (`YarnLevelOrderPanel`) is a
   drag-reorderable list. Placing a level into slot 4 of a 60–70 level list means dragging
   one row across the whole list. There is no way to jump a level to a target slot.

---

## Design

### Part 1 — Export never overwrites (insert new levels at top)

In `YarnMasterLevelExporter.Export`, decide the write key by **`levelId` presence**, not by
the filename number:

- **Existing `levelId`** — the level's `levelId` already appears as a `LevelConfigs[*].levelId`
  value → **update in place** at its current key. *(Unchanged: re-exporting an edited level
  keeps its slot, coins, position.)*
- **New `levelId`** — not present anywhere → **insert at the top (slot #1)**:
  1. Collect existing entries as `(levelConfig, rewardConfig)` pairs, sorted ascending by
     their current numeric key (each level paired with its reward config from the **same** old
     key so coins stay attached).
  2. Build the new level's pair (level config + reward config, as today).
  3. Rebuild `LevelConfigs` and `LevelRewardConfigs` from scratch, keyed `1..N` over the list
     `[new] + existing`. The new level gets `"1"`; every prior level shifts down by one. This
     also self-heals any gaps in the old keys (consistent with what **Apply Order** produces).

**Filename number is no longer authoritative.** It stops deciding the slot for new levels, so
`level002_new` can never clobber `level_002`. The hard-fail "Could not parse integer key from
filename" gate is **removed** — a number in the filename is now optional (the slot is assigned,
not parsed). A filename with no digits exports fine as a new top-inserted level.

> Edge note: detection is by exact `levelId` string. Renaming a saved level's file produces a
> new `levelId` → treated as a brand-new level inserted at top (the old entry stays under its
> old `levelId` until removed via the Order panel). This is acceptable and expected.

### Part 2 — "Move to position…" in the Order panel

Add a right-click context menu to each row in `YarnLevelOrderPanel` (IMGUI `ContextClick` over
the element rect → `GenericMenu`):

- **Move to position…** — opens a prompt for a target slot. The number is the **final 1-based
  slot** the level should end up at (matching the `#N` column the user sees). Entering `4`
  relocates the row to index 3 (`#4`); everything from old `#4` onward shifts down by one.
  Clamp the input to `1..N`; ignore a no-op / out-of-range / cancelled entry.
- **Remove level** — same behavior as the existing remove button (reuse `onRemoveCallback`
  logic / confirmation dialog).

The reorder is **in-memory** (same as a drag) — the user still clicks **Apply Order** to
renumber `1..N` and write the master JSON. Drag-to-reorder stays as-is for fine nudges.

**Dialog mechanism:** Unity has no built-in numeric input dialog. Use a tiny modal
`EditorWindow` (`ShowModalUtility`) with a single `IntField` + OK/Cancel, or — simpler — a
lightweight inline approach: clicking "Move to position…" sets the row into an "editing" state
that shows an `IntField` + a "Go" button on that row until committed. **Chosen: tiny modal
`EditorWindow`** (`MoveToPositionDialog`) — keeps the list-row drawing untouched and is
unit-test-isolated from the move math.

**Testable core:** extract the pure reorder into a static helper

```csharp
// Moves item at `from` (0-based) to land at `to` (0-based), shifting the rest. Returns false on no-op/out-of-range.
internal static bool MoveEntry<T>(List<T> list, int from, int to)
```

so the relocation logic is unit-tested without IMGUI. The context menu and dialog stay thin
wrappers that call `MoveEntry`, then `BuildList()`.

---

## Testing

### Exporter (`YarnMasterLevelExporterTests`, EditMode)

- **New colliding name keeps both** — seed master with `level_002` at key `"2"` (and others);
  export a new doc `level002_new`. Assert both survive: `level002_new` at `"1"`, `level_002`
  shifted to a higher key, no entry lost.
- **Plain new level inserts at top + reward shifts** — seed master with `level_001`; export
  new `level_010`. Assert `level_010` at `"1"`, `level_001` at `"2"`, and the reward config
  for `level_001` moved with it (coins intact).
- **Re-export existing levelId updates in place** — export `level_005` twice (second time with
  a changed coin reward); assert the entry count is unchanged, key unchanged, and the new coin
  value is written (no shift, no duplicate).
- **Empty master → key "1"** — export into a fresh/empty file → key `"1"`.

**Update existing tests:** `Export_ParsesFileName_025_ToKey25` asserts the removed filename→slot
behavior (export `level_025` into an empty master → key `"25"`). Under the new rule a new level
into an empty master is `"1"`. Update/rename it to assert the new top-insert behavior (or remove
if fully covered by the new cases). `Export_ParsesFileName_001_ToKey1` still passes by
coincidence (empty master + new level → `"1"`) — keep but consider renaming for clarity.

### Move helper (`YarnLevelOrderPanelTests` — new, or fold into exporter tests file)

- `MoveEntry` from top to middle shifts the in-between items up by one; target lands at the
  requested index.
- Move from middle to top; move to bottom (`to == N-1`); no-op (`from == to`) returns false;
  out-of-range returns false and leaves the list unchanged.

---

## Rollout

1. Implement + green EditMode suite (agent can self-run via Unity MCP).
2. **Sync both files** to the game repo mirror under `Assets/_YAT/Scripts/Editor/`
   (`YarnMasterLevelExporter.cs`, `YarnLevelOrderPanel.cs` + the new dialog/helper). Layer-2
   only → **no manifest pin bump.**
3. User confirms the game compiles in Unity 2022.3 (agent can't compile-verify the game).
4. Commit + push both repos.

## Out of scope

- Multi-select move (moving several rows at once) — single-row move covers the stated need.
- Changing drag behavior, Apply Order renumbering, or the YAK order panel
  (`YAKLevelOrderPanel`) — Yarn only for now.
