# Bus Buddies — Solvable-by-Construction Auto-fill

**Date:** 2026-07-14
**Status:** Design — awaiting review
**Owner:** editor-core (Layer 2 / Bus Buddies)

## Problem

The difficulty-panel auto-fill produces **unsolvable levels** at real grid sizes (30×30, 40×40).
A designer's cupcake picture is ringed by a `brownverydark` outline; the outline's buses sit at the
buried end of the queues, so in the no-gravity flood-accessibility model the enclosing ring can never
be peeled and the interior is never reachable → deadlock.

### Root cause (verified by reading the pipeline)

1. **Exact solver is skipped at real sizes.** `BusBuddiesAnalyzerConfig.SmallGridThreshold = 64` → the
   sound DFS solver only runs at ≤ 64 cells. At 900 / 1600 cells it never runs
   (`BusBuddiesAnalyzer.cs:69`).
2. **The only remaining gate is a myopic bot.** `BusAveragePlayer` (ε-greedy, lookahead 1) reuses the
   *correct* `BusSimState`, so its wins are genuine — but it cannot **find** the winning line on
   picture levels (peel the ring first, in order). On a buried-outline arrangement it clogs all active
   slots and loses → win-rate 0 → analyzer returns `Unknown`, not `Solvable`.
3. **The dig-arranger ignores peel order.** `BusBuddiesDigArranger` buries large-share "main" colors and
   round-robin-interleaves the rest, with no awareness that border-accessible colors must be released
   first. A big enclosing color is classified *main* and pushed to the tail — the exact opposite of
   what accessibility requires.
4. **Failures ship silently.** When no attempt passes, the autofiller writes the best-effort
   (unsolvable) arrangement and the panel shows only a soft grey "best-effort" status line
   (`BusBuddiesDifficultyPanel.cs:129`).

The underlying simulator is **correct** (border-flood accessibility, nearest-to-hole targeting,
deadlock detection, sound DFS). The fix builds a guarantee on top of it rather than changing it.

## Goals / Non-goals

**Goals**
- Auto-fill is **incapable of emitting an unsolvable level** for any color-balanced picture, at any
  grid size, deterministically (same settings + seed → same queue).
- Honor the six difficulty knobs (chunks / deviation / columns / difficulty-dig / no-1-bus /
  round-to-5) as far as possible **without** ever sacrificing solvability.
- Stay fast enough for batch generation (no Monte-Carlo gate, no per-candidate budget storms).
- If a result is ever not solvable (e.g. a truly impossible hand-authored grid), the panel makes it
  **unmissable**, not a grey aside.

**Non-goals**
- No change to the runtime sim's accessibility / targeting rules.
- No game-side (BusBuddies project) changes.
- Hidden-bus / connected-bus difficulty modeling stays out of scope (deferred features).

## Design

### 1. Constructive peel-order scheduler (replaces the random search + dig-arranger)

Capacity math (chunks → avg, deviation → window, no-1-bus, round-to-5) is unchanged: it still produces
a **balanced** multiset of buses per color. What changes is how those buses are **ordered** into columns.

Derive a globally valid release order by simulating a peel against the real N-slot `BusSimState`:

```
state = new BusSimState(model with the candidate buses)   // empty active row
pool  = all buses, grouped by color
order = []                                                 // global head→tail order
while pool not empty:
    ready = colors in pool that currently have an accessible block in `state`
    pick color C from `ready`         // difficulty-aware tie-break (see §2)
    pull one C bus into a free active slot; ResolveReleases() to quiescence
    order.append(that bus)
    (a bus that empties frees its slot, exactly as the game does)
```

Because accessibility peels inward from the border and the picture is connected + balanced, `ready` is
non-empty at every step until the board is clear — so the loop always completes with a **winning
line**. Distribute `order` round-robin across the N columns; within each column the buses stay in
global order, so the next bus in the sequence is always at *some* column's head → the player can
reproduce the winning line. The arrangement is solvable **by construction**.

### 2. Difficulty "dig" within the solvable envelope

The greedy peel above is the *easiest* solvable order. To express the Difficulty knob (bury main
colors deeper), bias the tie-break in `pick color C`: when several colors are `ready`, prefer
**background** colors first and defer **main** colors to the latest step at which they are still
`ready`, scaled by Difficulty (1 = neutral round-robin, 5 = defer main as long as accessibility
allows). Deferral is bounded by `ready`, so it can never bury a color past the point it is needed —
solvability is preserved automatically. No relaxation ladder, no re-search.

### 3. Exact replay verifier (replaces the bot as the gate)

After building an arrangement, **replay `order` through `BusSimState` and assert `IsWin()`**. This is
exact, deterministic, and O(moves × recompute-access) — no node budget, no rollouts. `Succeeded` is set
from this replay, not from the average player.

- The average player still runs for the **measured APS read-out** only (unchanged reporting).
- The old attempt-loop / `BusBuddiesDigArranger` / `BusBuddiesColorRoles`-driven burial search are
  retired from the solvability path (kept only if other callers need them; otherwise removed).

### 4. Panel hardening (defense in depth)

If `!Succeeded` (should now be impossible for a balanced grid), the panel surfaces a **prominent
warning** (colored `HelpBox`, not a grey status string) naming the reason, and clearly distinguishes
"solvable" from "could not make solvable" so a designer never ships a bad level unaware.

## Components & boundaries

- `BusBuddiesConstructiveArranger` (new, editor) — pure function:
  `(grid model, buses, columns, difficulty, seed) → (BusQueueData, bool solvable)`. Depends only on the
  runtime `BusSimState`. Independently unit-testable headless.
- `BusBuddiesAutofiller` — calls the constructive arranger, sets `Succeeded`/`Analysis` from the exact
  replay + APS read-out. Loses the Monte-Carlo gate.
- `BusBuddiesDifficultyPanel` — prominent solvable/unsolvable feedback.
- `BusBuddiesAnalyzer` / sim — unchanged.

## Testing (TDD)

1. **Reproduction (red first):** ring grid (outline color enclosing an interior color) + a buried-outline
   arrangement → assert the sim reports it unsolvable and the *old* path would ship it. Then the new
   arranger produces a `Succeeded` arrangement whose replay wins.
2. Constructive arranger returns a replay-winning arrangement for: single-ring picture, nested rings
   (3 concentric colors), donut (interior hole), multi-column (1..5), tiny grids.
3. Determinism: same seed → identical queue.
4. Difficulty dig: higher Difficulty defers main colors later **and** still replays to a win.
5. Balance preserved: per-color capacity still equals per-color block count.
6. Degenerate: empty grid → empty queue, `Succeeded`; single color; a genuinely unbalanced hand grid →
   `!Succeeded` with a clear reason (panel warning path).

## Risks

- **Narrow-frontier pathologies** where greedy stalls: the replay verifier is the backstop — we only
  accept arrangements the sim actually wins; a fallback tie-break retry covers the rare stall.
- **Difficulty expressiveness** is now bounded by accessibility (can't bury a ring). This is correct —
  the old freedom is exactly what produced unsolvable levels — but communicate it in the panel so
  designers understand why a very-high dig on a heavily-ringed picture looks tamer.
