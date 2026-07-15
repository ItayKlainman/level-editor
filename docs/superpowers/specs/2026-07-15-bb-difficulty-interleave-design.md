# Bus Buddies — Make Difficulty affect gameplay (outline interleave) + round-to-top reconcile (design)

**Date:** 2026-07-15
**Profile:** Bus Buddies (Layer-2 only — no schema/export/package change)
**Branch:** folds into `feat/bb-hidden-connected-mechanics` (after the 3 mechanics + round-to-top, before the combined push)

## 1. Problem (root cause — investigated 2026-07-15)

On every auto-filled level the **outline (black) buses cluster at the head of the queue regardless of the Difficulty knob (1–5)**, so difficulty has no felt effect. Two compounding causes:

1. **Flood-accessibility forces the outline first (structural, unavoidable).** The outline is the silhouette ring enclosing the picture; at the start only the outline is border-accessible, so the peel must pull outline before any interior is reachable.
2. **The outline is never deferred, so ALL of it drains before any interior (the fixable part).** In `BusBuddiesConstructiveArranger.PickColumn` the difficulty deferral applies to **main colors only** (`rank = isMain ? -(f*1e6) : 0`), and the outline is excluded from "main" (`BusBuddiesColorRoles.ClassifyMain` with `ExcludeOutlineFromMain`). So the outline's rank is always 0, and the `flow*1000` tie-break (outline has the most accessible cells early) drains the whole outline before touching the interior. Difficulty only reshuffles interior colors among themselves — a weak, invisible effect.

## 2. Goal

Make Difficulty visibly change the arrangement while keeping the solvable-by-construction guarantee:
- **Difficulty 1 (easy):** front-load the outline (drain it, then the interior) — exactly today's behavior.
- **Difficulty 5 (hard):** peel only the *minimum* outline needed to open the interior, then **interleave** interior with the remaining outline (the player juggles more colors at once).

## 3. Design

### 3.1 Part A — Interleave via a difficulty-scaled "spread" penalty (`PickColumn` v2)

Replace the main-only deferral with a **pulls-based spread** that discourages re-draining a color once it has been pulled, scaled by difficulty. This naturally produces "minimum outline, then interleave" without needing the outline/main split, and is backward-compatible at difficulty 1.

In `BusBuddiesConstructiveArranger`:
- `Schedule` maintains `Dictionary<int,int> pullsByColor` (color index → buses of that color scheduled so far); increment after each `ApplyMove`. Pass it into `PickColumn`.
- New `PickColumn` scoring (higher = picked sooner), among ready columns (pullable, `flow > 0`):
  ```
  f       = (Clamp(difficulty,1,5) - 1) / 4f;          // 0..1
  flowMax = max flow among ready colors this step (>= 1);
  pulls   = pullsByColor[color]  (0 if none);
  // Spread penalty: at high f, each prior pull of THIS color subtracts ~a full flow-unit,
  // so a color we've been draining loses to a fresh ready color → the peel switches → interleave.
  score   = flow - f * pulls * flowMax + rng.Next(0, 1000);
  ```
  - At `f = 0` (difficulty 1): `score = flow + rng` — identical to today's flow-greedy behavior (outline drains first). **No regression on easy.**
  - At `f = 1` (difficulty 5): the penalty dominates flow, so once the interior is accessible the peel round-robins across ready colors → interleave.
- **This replaces the `isMain` deferral term.** The `mainColors` argument is kept in the signature for now (harmless / other call sites) but no longer drives the score. See §5 note.
- **Solvability preserved:** the peel still only ever picks colors with an accessible block (`flow > 0`), and `Arrange` still re-verifies the whole queue by exact replay (`ReplayWins`); a color you actually need to open the interior is pulled because at that moment it is the only ready color (nothing to interleave with yet). The spread is a preference among *ready* colors, self-limiting.

### 3.2 Part B — Reconcile round-to-top so it can't undo the interleave

`SortRoundToHead`/`StableRoundFirst` (shipped earlier this branch) currently sort a whole column's buses ×5-first **regardless of color**, which re-clusters the (round) outline buses at the head and undoes Part A. Redefine the sort to be **within-color, position-preserving**:

- New `StableRoundFirst` contract: for each **color** present in the column, take the set of positions that color occupies, and reorder **only that color's buses among their own positions** so multiples-of-5 come before remainders (stable within each subgroup). **Positions belonging to other colors are untouched** — the column's inter-color interleave pattern is preserved exactly.
  - Example: column `[O20, I5, O25, I3]` → O occupies pos 0,2 (both ×5, unchanged); I occupies pos 1,3 (`I5` ×5, `I3` remainder → already round-first) → result unchanged, interleave intact.
  - Example: column `[O3, O20]` (same color) → `[O20, O3]` (round-first within O).
- `SortRoundToHead` keeps its per-column exact-replay solvability guard (reordering same-color buses by capacity can still shift slot occupancy, so re-verify and revert a column if it breaks).

## 4. Testing (EditMode)

Add to `Assets/BusBuddies/Tests/Editor/` (extend `BusBuddiesConstructiveArrangerTests` and `BusBuddiesRoundToTopTests` where natural; namespace `Hoppa.BusBuddies.Editor.Tests`).

1. **`Difficulty_ChangesArrangement_MoreInterleaveAtHighDifficulty`** — the proof the knob works. On `RingGrid` (O outline enclosing I interior) with enough I buses to interleave, arrange at difficulty 1 and at difficulty 5. Assert `InteriorBefore LastOutline(diff5) > InteriorBeforeLastOutline(diff1)` (helper: flatten the queue in play order — round-robin across columns — and count interior buses appearing before the last outline bus). Difficulty 1 should be ~0; difficulty 5 clearly positive.
2. **Existing `Arrange_AllDifficulties_StaySolvable` + `Arrange_DitheredFilledPicture_StaysSolvable_AllSeeds` MUST stay green** (the core guarantee). Extend the dithered test to also assert solvability after `SortRoundToHead` at difficulty 5 with Round-to-5 semantics.
3. **`Arrange_Difficulty1_MatchesFlowGreedy_Baseline`** — difficulty 1 still front-loads the outline (assert the interior-before-last-outline count is 0 / outline drains first), guarding the no-regression-on-easy promise.
4. **`StableRoundFirst_PreservesColorPositions_OrdersRoundFirstWithinColor`** — column `[O20, I5, O25, I3]` keeps color positions; column `[O3, O20]` → `[O20, O3]`; a mixed interleaved column keeps its color pattern. Update the existing round-to-top tests to the within-color contract.
5. **Existing `Arrange_HighDifficulty_DefersMainAfterBackground`** — re-examine: under the new spread model, high difficulty still pulls background/outline before draining into main *initially*, but interleaves after. Adjust the assertion to the new semantics if needed (it currently asserts background at head, which still holds since the very first ready color is background).

Full EditMode suite stays green (baseline 440 + new tests, 0 regressions).

## 5. Note — semantic shift of the Difficulty knob (flag for the boss's model)

The boss's difficulty spec defined Difficulty as the **"dig" axis** — bury *main* colors deep toward the queen end ([[project_bb_difficulty_model]]). This change repurposes Difficulty to **"interleave/juggle intensity"** (spread all colors, including the outline). That is what the lead asked for (2026-07-15) to make the knob felt. The pure-dig deferral is superseded; the two are not combined. **Flag for reconciliation with the boss's model** — if both "dig main deep" and "interleave outline" are wanted, they can be layered later (e.g., spread as the primary difficulty effect + a secondary main-bias), but that is out of scope here.

## 6. Out of scope

- Combining dig + interleave into one knob (noted above).
- Any change to the capacity math, schema, export/import, or package version.
- Tuning the exact `flowMax`/penalty constants beyond what the tests in §4 require (TDD-driven).
