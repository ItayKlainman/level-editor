# Bus Buddies — Round-to-top bus ordering (design)

**Date:** 2026-07-15
**Profile:** Bus Buddies (Layer-2 only — no schema, export, or package change)
**Depends on / sequenced after:** the BB Hidden/Connected mechanics branch (`feat/bb-hidden-connected-mechanics`). This lands as follow-up commits on that same branch (or a fresh branch off it) once that work is merged — it touches disjoint files, so no conflict.

## 1. Goal

When the designer's **Round-to-5** rule is on, the auto-fill should place the round (multiple-of-5) buses toward the **head** of each column (top, closest to the header) and the non-round **remainder** buses toward the **tail** (bottom) — **without ever making a level unsolvable**.

Success = with Round-to-5 checked, an auto-filled level's columns read round-first/remainder-last wherever that ordering is still winnable; and the solvable-auto-fill guarantee (v0.9.0) is never violated.

## 2. Background — the two independent concerns

- **Capacity** is decided by `BusBuddiesCapacityMath` (Round-to-5 snaps capacities to multiples of 5, leaving at most a small non-round remainder bus per color).
- **Order** is decided by `BusBuddiesConstructiveArranger.Arrange`, which derives a **solvable-by-construction** release order (border-inward peel) and verifies it by exact replay (`ReplayWins`). In Bus Buddies only a column's **head (index 0)** is tappable, so within-column order **is** forced play order — reordering can change solvability.

The feature therefore cannot be a blind sort; it must be a sort **guarded by re-verification**.

## 3. Design

### 3.1 New helper — `BusBuddiesConstructiveArranger.SortRoundToHead`

```csharp
public static BusQueueData SortRoundToHead(
    BusQueueData queue, GridData<ICellData> grid, int columns, int activeSlots)
```

Algorithm (per column, greedy, solvability-guarded):
1. Build a fresh `BusQueueData` mirroring `queue` (new `BusColumn`s + new `List<BusEntry>` holding the **same** `BusEntry` references — we only reorder, never clone buses).
2. For each column `c`:
   - Compute `sorted = StableRoundFirst(column.Buses)` — a stable partition: buses with `Capacity % 5 == 0` first (preserving their relative order), then the remainder buses (preserving theirs).
   - If `sorted` equals the current order, skip.
   - Otherwise set the column to `sorted` and call `ReplayWins(grid, working, columns, activeSlots)`. If it **still wins**, keep the sort; if not, **revert that column** to its original order (the other columns' accepted sorts stay).
3. Return the working queue. It is guaranteed **still solvable** (every accepted change was re-verified against the whole queue) and as-sorted-as-safely-possible.

Complexity: at most `1 + columns` replays (columns ∈ 1..5) — negligible.

### 3.2 New public helpers (for the sort + tests)

- `public static List<BusEntry> StableRoundFirst(IReadOnlyList<BusEntry> buses)` — the pure ordering primitive (multiples-of-5 first, stable). Round test = `bus.Capacity % 5 == 0`.
- `public static bool IsSolvable(GridData<ICellData> grid, BusQueueData queue, int columns, int activeSlots) => ReplayWins(grid, queue, columns, activeSlots);` — exposes the existing exact-replay check so `SortRoundToHead` and tests can call it. (`ReplayWins` stays private; this is the public seam.)

### 3.3 Wiring — `BusBuddiesAutofiller.Complete`

After the attempt loop selects `bestQueue` and **only when the level came out solvable AND `settings.RoundToFive` is true**, pass the queue through the sorter before serializing:

```csharp
            if (solvable && settings.RoundToFive)
                bestQueue = BusBuddiesConstructiveArranger.SortRoundToHead(
                    bestQueue, doc.Grid, columns, slots);

            result.TopSection = JObject.FromObject(bestQueue);
            result.Succeeded  = solvable;
```

(Insert immediately before the existing `result.TopSection = JObject.FromObject(bestQueue);` at `BusBuddiesAutofiller.cs:125`. The APS read-out that follows then runs on the final, sorted queue — correct, since it should measure what ships.)

- When `RoundToFive` is off → not called; order is exactly the arranger's (unchanged behavior).
- When not solvable → not called; the honest unsolvable fallback is untouched.
- No new checkbox — the behavior is automatic with Round-to-5, per the lead's "always happen".

### 3.4 Difficulty tab

No new control. Add a one-line note to the **Round-to-5** checkbox tooltip in `BusBuddiesDifficultyPanel.cs` so the behavior is discoverable, e.g.:
`"Round-to-5: snap bus capacities to multiples of 5, and place the round buses toward the head of each column (remainders at the tail) when it keeps the level solvable."`

## 4. Testing (EditMode) — `Assets/BusBuddies/Tests/Editor/BusBuddiesRoundToTopTests.cs`

Namespace `Hoppa.BusBuddies.Editor.Tests` (matches `BusBuddiesConstructiveArrangerTests`).

1. **`StableRoundFirst_PartitionsRoundThenRemainder_Stable`** (pure):
   caps `[7,10,5,3,20]` → order `[10,5,20,7,3]` (rounds 10,5,20 in original relative order; remainders 7,3). Also: all-remainder `[7,3]` unchanged; all-round `[10,20]` unchanged.
2. **`SortRoundToHead_KeepsSolvable_OnArrangedQueue`** (invariant): build a solvable queue via `BusBuddiesConstructiveArranger.Arrange` on the `RingGrid` fixture (reuse the helper), apply `SortRoundToHead`, assert `BusBuddiesConstructiveArranger.IsSolvable(...) == true` on the result.
3. **`SortRoundToHead_PutsRoundBusAtHead_WhenSafe`** (applied-and-kept): an open single-color board where any order wins — 8×1 grid of `"blue"`, one column `[{cap 3},{cap 5}]`. `SortRoundToHead(queue, grid, columns:1, activeSlots:5)` → head capacity is `5` (round), tail is `3` (remainder); result is solvable.
4. **`SortRoundToHead_RevertsColumn_WhenSortBreaksSolvability`** (guard): use the `RingGrid` "O ring encloses I interior" case — a column ordered so the round-first sort would surface interior `I` buses (small caps) ahead of the enclosing `O` bus (cap 15, a multiple of 5) and deadlock. Hand-build a column `[{O,cap? non-multiple},{I...}]` where sorting O (if made a multiple of 5) to the head is fine, but choose caps so the *round* buses are the interior singletons; assert the result reverts (interior not surfaced) and stays solvable. If a deterministic revert case proves hard to hand-craft, replace this with a property check: run `SortRoundToHead` over the arranger's output for seeds 1..25 on the dithered fixture and assert every result `IsSolvable` (the safety invariant is the load-bearing guarantee; the revert path is what makes it hold).

Full EditMode suite stays green (baseline 413 + Hidden/Connected tests + these).

## 5. Out of scope

- Reordering on manual (non-auto-fill) edits — the feature is an auto-fill step; a designer who hand-reorders afterward is not overridden.
- Any capacity change — Round-to-5's capacity math is untouched; this only reorders.
- Cross-column moves — ordering is within each column (head = top); buses are not moved between columns.
- Gating behind a separate toggle — it rides on the existing Round-to-5 checkbox.
