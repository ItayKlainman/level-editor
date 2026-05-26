# YarnTwist Spool Auto-fill + Win-path Analysis — Design Spec

**Date:** 2026-05-26
**Status:** Draft, pending user review

---

## Overview

A new authoring mode for the level editor: the designer paints the **grid** by hand (walls, boxes, arrow boxes, tunnels) and the editor **auto-completes the top section** (spool columns) and tells her **how many distinct win paths** the resulting puzzle admits. The win-path count gives her a concrete solvability/difficulty signal she can use to retune the grid or the difficulty knobs.

This is a complement to the existing `✨ Generate` flow (which generates the whole level from scratch). Use cases:

- Designer has a specific grid in mind, wants spools auto-filled correctly.
- Designer wants to test whether a hand-authored level is actually winnable, and how loose/tight the solution space is.

v1 ships **YarnTwist only**, on top of two new generic Layer 1 contracts so future games (YAK, next project) can plug in their own implementations without Layer 1 changes.

---

## Goals

1. **Auto-fill spools given a fixed grid** — output a top section that satisfies `YarnColorBalanceRule` by construction, with the difficulty controlled via existing Difficulty (1–10) + a Target Win-path band.
2. **Report win-path count for any complete level** — runnable standalone for hand-authored levels, no auto-fill required.
3. **Stay generic at Layer 1** — no YarnTwist-specific vocabulary (no "spool", no "yarn") in `Packages/com.hoppa.leveleditor.core/`. Generic concepts only: *analyze* and *complete*.
4. **No friction for the next game** — adding the feature to a future game = subclass two Layer 2 assets and wire two profile fields. Mirrors the existing generator pattern.

## Non-goals (v1)

- YAK implementation. Layer 1 contracts will support it cleanly when needed; no Layer 2 code ships in this initiative.
- Modelling hidden-flag information for "fair-given-only-visible-info" win counts. Hidden flags are placed but ignored by the analyzer's math (justification in §4).
- Per-level conveyor capacity override stored on `LevelDocument`. Capacity is operator-selectable in the panel (24 / 30 / Custom). The game itself selects capacity from level progression at runtime.
- Async / background-thread analysis with a cancel button. v1 is synchronous with hard timeouts.
- Migrating the existing `_generatorConfig` field off `GameProfile`. Called out as a follow-up cleanup (§11), not bundled.

---

## Decisions log

| # | Decision | Rationale |
|---|---|---|
| D1 | UI: side panel in normal Edit mode, not a separate fullscreen mode | Designer keeps her grid visible while tweaking; no mode switch. |
| D2 | Exact win-path count via DFS, capped at 10,000 + timeout | "Solvable yes/no" is too weak; Monte-Carlo gives a meaningless absolute number. Exact is the right tool. |
| D3 | Difficulty maps to a target win-path **band**; autofiller iterates until it lands inside | Matches the existing reroll-loop pattern in `YarnTwistLevelGenerator`. |
| D4 | v1 = YarnTwist only, with Layer 1 contracts | Same staging as the existing generator: v1 = YarnTwist; YAK adopts later. |
| D5 | Two **decomposed** Layer 1 contracts (`ILevelAnalyzer` + `ILevelCompleter`) rather than one combined "fill + analyze" surface | Analyzer is useful on its own (standalone "Analyze" button for hand-authored levels); the completer simply uses it internally. |
| D6 | `GameProfile` stays generic — no YarnTwist-specific field names ("spool", "autofill") at Layer 1. Game-specific config lives on the Layer 2 *asset* (the completer's own `[SerializeField]`), not on the profile | Golden rule: Layer 1 must work for the next game seamlessly. |
| D7 | `GameProfile` gains an `_extensions: List<ScriptableObject>` slot + `GetExtension<T>()` accessor | Forward-looking provision: when a future game legitimately needs game-specific data attached to the profile, it goes here rather than as a new typed field. Not used by this v1 feature. |
| D8 | Hidden flags ignored by analyzer; placed by completer | The set of winning sequences is mathematically identical with or without the hidden mask — hidden affects player *information*, not state transitions. |
| D9 | Conveyor capacity is operator-set in the panel (24 / 30 / Custom), not derived from level order | The level being edited may not be ordered yet. Explicit is safer. |
| D10 | Tunnels: each queue entry treated as one virtual box of that color | Matches runtime semantics (tunnel taps dequeue and spawn one box at a time). |
| D11 | Arrow boxes: locked until the box pointed-at by `Direction` is tapped | Matches runtime gating semantics (the arrow points at its prerequisite). |

---

## Architecture overview

```
┌─────────────────── Layer 1 (Packages/com.hoppa.leveleditor.core) ────────────────┐
│                                                                                  │
│   ILevelAnalyzer     ←── interface: Analyze(doc, profile, req) → AnalysisResult  │
│       ▲                                                                          │
│       │                                                                          │
│   LevelAnalyzerAsset (abstract ScriptableObject)                                 │
│                                                                                  │
│   ILevelCompleter    ←── interface: Complete(doc, profile, req) → CompResult     │
│       ▲                                                                          │
│       │                                                                          │
│   LevelCompleterAsset (abstract ScriptableObject)                                │
│                                                                                  │
│   GameProfile gains:                                                             │
│     _levelAnalyzer   : LevelAnalyzerAsset?                                       │
│     _levelCompleter  : LevelCompleterAsset?                                      │
│     _extensions      : List<ScriptableObject>  + GetExtension<T>()               │
│                                                                                  │
│   AutofillPanel (IMGUI side-panel; rendered iff profile.LevelAnalyzer != null)   │
│                                                                                  │
└──────────────────────────────────────────────────────────────────────────────────┘
                              │ (implements)
                              ▼
┌─────────────────── Layer 2 (Assets/YarnTwist) ─────────────────────────────────┐
│                                                                                │
│   YarnTwistLevelAnalyzer    : LevelAnalyzerAsset   ← win-path DFS simulator    │
│   YarnTwistSpoolAutofiller  : LevelCompleterAsset  ← reroll loop, uses ↑       │
│       └── [SerializeField] _config : YarnTwistSpoolAutofillConfig              │
│                                                                                │
│   YarnTwistSpoolAutofillConfig : ScriptableObject  ← Difficulty curves, caps   │
│                                                                                │
└────────────────────────────────────────────────────────────────────────────────┘
```

Game-specific tunings (the `YarnTwistSpoolAutofillConfig` asset) are owned by the **Layer 2 completer asset** — `[SerializeField] private YarnTwistSpoolAutofillConfig _config;`. The profile asset never references it directly, keeping `GameProfile` clean.

---

## Layer 1 contracts

### `ILevelAnalyzer` + `LevelAnalyzerAsset`

```csharp
public interface ILevelAnalyzer
{
    LevelAnalysisResult Analyze(LevelDocument doc, GameProfile profile, AnalysisRequest req);
}

public abstract class LevelAnalyzerAsset : ScriptableObject, ILevelAnalyzer
{
    public abstract LevelAnalysisResult Analyze(LevelDocument doc, GameProfile profile, AnalysisRequest req);
}
```

### `AnalysisRequest`

```csharp
public sealed class AnalysisRequest
{
    public AnalysisMode Mode = AnalysisMode.Count;   // Solvable | Count
    public int  WinPathCap        = 10_000;
    public long TimeoutMs         = 5_000;
    public int? ConveyorCapacityOverride;            // null = let analyzer decide
}

public enum AnalysisMode { Solvable, Count }
```

### `LevelAnalysisResult`

```csharp
public sealed class LevelAnalysisResult
{
    public bool   Solvable;
    public long   WinPathCount;       // exact, or capped
    public bool   CountWasCapped;     // true if hit WinPathCap or TimeoutMs
    public long   StatesExplored;
    public long   ElapsedMs;
    public string FailureReason;      // populated when Solvable=false
    public override string ToString() => /* one-line diagnostic */;
}
```

### `ILevelCompleter` + `LevelCompleterAsset`

```csharp
public interface ILevelCompleter
{
    LevelCompletionResult Complete(LevelDocument doc, GameProfile profile, CompletionRequest req);
}

public abstract class LevelCompleterAsset : ScriptableObject, ILevelCompleter
{
    public abstract LevelCompletionResult Complete(LevelDocument doc, GameProfile profile, CompletionRequest req);
}
```

### `CompletionRequest`

```csharp
public sealed class CompletionRequest
{
    public int    Difficulty = 5;            // 1..10
    public float? TargetAPS;
    public int    Seed;                       // 0 = random
    public int?   ConveyorCapacityOverride;
}
```

### `LevelCompletionResult`

```csharp
public sealed class LevelCompletionResult
{
    public JObject              TopSection;        // game-shaped JSON to overwrite doc.TopSection
    public LevelAnalysisResult  Analysis;          // analysis of the chosen fill
    public bool                 Succeeded;         // hit the Difficulty band?
    public int                  CandidatesTried;
    public int                  SeedUsed;
    public long                 ElapsedMs;
    public Dictionary<int, int> CandidatePathCountHistogram; // log-bucket → freq
    public string               FailureReason;
}
```

### `GameProfile` additions

```csharp
[SerializeField] private LevelAnalyzerAsset  _levelAnalyzer;
[SerializeField] private LevelCompleterAsset _levelCompleter;
[SerializeField] private List<ScriptableObject> _extensions = new();

public LevelAnalyzerAsset  LevelAnalyzer  => _levelAnalyzer;
public LevelCompleterAsset LevelCompleter => _levelCompleter;

public T GetExtension<T>() where T : ScriptableObject
{
    foreach (var e in _extensions) if (e is T t) return t;
    return null;
}
```

All three fields are optional. Existing profiles compile and run unchanged.

---

## Win-path simulator (`YarnTwistLevelAnalyzer`)

### Gameplay model encoded by the simulator

Validated against runtime code in `E:/Projects/Hoppa/YarnTwist/`:

- **Box**: holds 9 yarn balls of one color. Tap → all 9 released into the conveyor.
- **Arrow box**: same as box, but **locked** until the neighbor cell in `Direction` has been tapped (the arrow visually points at its prerequisite). Runtime source: `YATBoxPrefabComponent.cs:153-286`.
- **Tunnel**: queue of N color entries. Each tap dequeues the next entry and emits a box-equivalent (9 balls of that color). `OutputDirection` controls visual rotation only at runtime.
- **Conveyor**: a circulating belt with finite capacity. Lose if it ever exceeds capacity. Capacity is progression-based at runtime (24 for levels 1–15, 30 for 16+, per `YATSplineContainerComponent` prefab choice in `YATGameManagerComponent.cs:327-352`); operator-selectable in the panel.
- **Spools**: 4 fixed columns, only bottom-most spool in each column is matchable. When a ball whose color matches an active spool reaches it, the ball unwinds; when a spool has received 3 balls, it clears and the next spool drops down.
- **Win**: all boxes / tunnel queues exhausted + all spools cleared + conveyor empty.
- **Loss**: conveyor occupancy > capacity, OR no item tappable and not won.

### State per DFS node

| Field | Type | Notes |
|---|---|---|
| `BoxTapped` | `ulong` bitmask | one bit per box / arrow-box cell index; up to ~49 fits in 64 bits |
| `TunnelQueueIdx` | `int[numTunnels]` | current dequeue position per tunnel |
| `SpoolHead` | `int[4]` | index of bottom-most active spool per column |
| `SpoolFillCount` | `int[4]` | balls delivered to the active spool (0–2) |
| `ConveyorBag` | `int[colorCount]` | multiset of colors on the belt; sum = occupancy |

**Why a multiset (not an ordered queue):** the belt circulates continuously, so for solvability / path-counting only *which colors and how many* are on the belt matters, not their arrangement. This collapses the state space enormously and is correct under the game's "any matching ball eventually unwinds" rule.

### Tappability rules

- **Box**: `!BoxTapped[idx]`
- **Arrow box**: `!BoxTapped[idx]` AND `BoxTapped[neighborIdx in arrow.Direction]`
  - Edge case: if the neighbor isn't a tappable type (wall, off-grid, empty), the arrow box is permanently unreachable. Excluded from candidates; the existing `YarnArrowBoxTargetRule` should already reject such grids at edit time.
- **Tunnel**: `TunnelQueueIdx[idx] < tunnel.Queue.Count`

### Step semantics (on tap of item `i`)

1. Add 9 balls of `i.ColorId` to `ConveyorBag`.
2. **Match resolution** (fixed-point loop):
   ```text
   repeat until no change:
     for k in 0..3:
       while spoolHead[k] in range AND bag[spool[k].color] > 0:
         take = min(3 - fillCount[k], bag[spool[k].color])
         fillCount[k] += take; bag[color] -= take
         if fillCount[k] == 3: spoolHead[k]++; fillCount[k] = 0
   ```
   A single tap can clear the same column multiple times if successive spools match the incoming color or pre-existing belt residue. Left-to-right greedy is canonical (final state is identical under any deterministic order).
3. Check terminal conditions:
   - `sum(bag) > capacity` → **dead end** (return 0 paths)
   - all items consumed AND all spools cleared AND `sum(bag) == 0` → **WIN** (return 1)
   - no tappable item remains AND not won → **dead end** (return 0)
4. Else recurse over each currently-tappable item.

### Memoization

`Dictionary<StateKey, long>` keyed on `(BoxTapped, TunnelQueueIdx, SpoolHead, SpoolFillCount, sorted ConveyorBag)`. Value = paths-to-win from that state.

No cycle detection needed: every tap monotonically advances either `BoxTapped` or some `TunnelQueueIdx`, so the DAG is acyclic by construction.

### Cutoffs

- `WinPathCap` (default 10,000): once cumulative count reaches the cap, DFS bails and reports `CountWasCapped = true`.
- `TimeoutMs` (default 5,000 standalone; 500 per-candidate during autofill): `Stopwatch` checked at every recursion entry. Abort returns `pathsFoundSoFar` + `CountWasCapped = true`.
- `AnalysisMode.Solvable`: short-circuits at the first winning leaf — returns `Solvable=true/false` + `WinPathCount=0/1`. Available as a fast pre-filter; v1 panel always uses `Count` mode.

### Hidden flags

Hidden boxes (`YarnBoxCell.Hidden`) and hidden spools (`YarnSpoolData.Hidden`) are deserialized but **not consulted** by the analyzer. Justification: the set of winning sequences is mathematically identical with or without the hidden mask. Hidden affects player *information*, not game *state transitions*. Modelling "fair-given-visible-info" path counts is a v2 concern.

---

## Auto-fill loop (`YarnTwistSpoolAutofiller`)

```text
input:  doc with grid painted (top section ignored), profile, request
output: LevelCompletionResult

1. perColor = walk grid; bump 1 per Box / ArrowBox; bump 1 per Tunnel queue-entry
2. flatSpools = expand perColor → list of (count × 3) spools per color
3. hiddenN = round(flatSpools.Count × HiddenSpoolRatio.Evaluate(D) / 100)
4. target = WinPathTargetByDifficulty.Evaluate(D); tol = WinPathTolerance
   bandLow  = target * (1 - tol);  bandHigh = target * (1 + tol)
5. rng = new System.Random(seed); bestDist = inf; best = null
   for attempt in 0..config.MaxRerollAttempts:
       Shuffle(flatSpools, rng)
       MarkHidden(flatSpools, hiddenN, rng)
       columns = DistributeRoundRobin(flatSpools, 4)
       topSection = JObject.FromObject(YarnTopSectionData{columns})

       candDoc = doc with TopSection = topSection
       analysis = profile.LevelAnalyzer.Analyze(candDoc, profile, AnalysisRequest {
           Mode = Count,
           WinPathCap = config.WinPathCap,
           TimeoutMs  = config.PerCandidateTimeoutMs,
           ConveyorCapacityOverride = request.ConveyorCapacityOverride,
       })
       HistogramBucketBump(analysis.WinPathCount)

       if analysis.Solvable && analysis.WinPathCount in [bandLow, bandHigh]:
           return { topSection, analysis, Succeeded = true, ... }

       d = |analysis.WinPathCount - target|
       if d < bestDist: bestDist = d; best = (topSection, analysis)

       if Stopwatch.ElapsedMs > config.TotalTimeoutMs: break

6. return { best.topSection, best.analysis, Succeeded = false, ... }
```

### Notes

- Color balance is exact by construction (every grid item = 9 balls = 3 spools), so no need to gate via `LevelGeneratorRunner.Evaluate` like the full generator does.
- If `profile.LevelAnalyzer == null`, return `Succeeded=false, FailureReason="no analyzer wired"`.
- Hidden flags are placed but ignored by the analyzer's path-counting (per §4); the autofiller still places them so the *level* plays with the hidden challenge the designer wants.
- Reroll-on-failure still applies whatever the best candidate was (better than leaving garbage). Designer can Ctrl-Z if she doesn't like it.

### `YarnTwistSpoolAutofillConfig`

Owned by `YarnTwistSpoolAutofiller.asset` as `[SerializeField] private YarnTwistSpoolAutofillConfig _config;`.

| Field | Default | Purpose |
|---|---|---|
| `WinPathTargetByDifficulty: AnimationCurve` | D1=2000 ↘ D10=5 | target win-path count per Difficulty |
| `WinPathTolerance: float` | 0.5 | accept count within ±tolerance of target |
| `HiddenSpoolRatio: AnimationCurve` | D1=0% ↗ D10=40% | % of spools marked hidden |
| `MaxRerollAttempts: int` | 100 | upper bound on candidate count |
| `WinPathCap: int` | 10,000 | analyzer's DFS path-count cap |
| `PerCandidateTimeoutMs: int` | 500 | analyzer's per-candidate soft timeout |
| `TotalTimeoutMs: int` | 30,000 | outer loop hard timeout (bounds editor freeze) |
| `DefaultConveyorCapacity: int` | 24 | used when panel didn't override |

Standalone Analyze button uses constant defaults (`WinPathCap=10,000`, `TimeoutMs=5,000`); it works even when no completer is wired.

---

## UI — `AutofillPanel`

Lives in the right column of `LevelEditorWindow`, stacked under the existing Inspector / Validation / Summary panels. **Only rendered when `profile.LevelAnalyzer != null`.** ~180 px tall.

```text
┌─ Spool Analysis ─────────────────┐
│ Conveyor:    [24 ▾]              │
│ Difficulty:  ─────●─────  5      │
│ Seed:        [_______] 🎲 🔒     │
│                                  │
│ [ Analyze ]    [ Auto-fill ]     │
│                                  │
│ ── Last result ────────────────  │
│ Win paths:    247  ≥ band, ✅    │
│ Solvable:     yes                │
│ Candidates:   12 / 100           │
│ Elapsed:      438 ms             │
│ ▁▃▇█▅▂▁_ (path-count histogram)  │
└──────────────────────────────────┘
```

- **Conveyor**: enum dropdown { 24, 30, Custom… }. Custom shows an int field. Passed via `ConveyorCapacityOverride` to both Analyze and Auto-fill.
- **Difficulty / Seed**: same widget treatment as `GeneratorModePanel` (HorizontalSlider for Difficulty; IntField + 🎲 + 🔒 for Seed). Auto-fill only.
- **Analyze button**: runs `profile.LevelAnalyzer.Analyze(currentDoc, profile, req)`. Read-only — does **not** modify the document. Displays result.
- **Auto-fill button**: disabled if `profile.LevelCompleter == null`. On click:
  1. `session.PushUndoSnapshot()` — so designer can undo the spool replacement.
  2. `result = profile.LevelCompleter.Complete(currentDoc, profile, completionRequest)`.
  3. On success: `currentDoc.TopSection = result.TopSection`; `session.MarkDirty()`; `session.RunValidation()`.
  4. Display `result.Analysis` block + histogram.
  5. On `Succeeded=false`: show best-effort result with a "couldn't hit band — best candidate has N paths" note; still applies the top section (best-effort is usually better than the previous garbage). Designer can Ctrl-Z.
- **Histogram**: 8-bucket log-scale sparkline of `CandidatePathCountHistogram`, with a thin vertical marker at the target band. Polish — can ship without if scope is tight.

---

## Edge cases

| Scenario | Behavior |
|---|---|
| Grid is empty (no tappable items) | Analyzer returns `Solvable=true, WinPathCount=1`. Autofiller returns `Succeeded=true, TopSection=empty`. |
| Grid has only walls / empty cells | Same as above. |
| Arrow box's `Direction`-neighbor is wall / off-grid / empty | Analyzer skips that arrow box from candidates → likely unsolvable. Autofiller surfaces `FailureReason="grid contains unreachable arrow box at (x,y)"`. Best-effort top section still produced and applied. |
| Profile has `LevelAnalyzer` but no `LevelCompleter` | Panel renders; Analyze works; Auto-fill button disabled with tooltip. |
| Profile has neither | Panel doesn't render. |
| Designer presses Auto-fill, doesn't like result | Ctrl-Z restores prior top section (single undo entry). |
| Per-candidate timeout hit | Returns capped count + `CountWasCapped=true`. Autofiller treats partial count as the candidate's score. |
| Total timeout hit | Outer loop aborts with best-so-far. |

---

## Test plan

### `YarnTwistLevelAnalyzerTests.cs` (Edit Mode)

| # | Fixture | Assertion |
|---|---|---|
| 1 | 1 pink box; 3 pink spools | exactly 1 path |
| 2 | 1 pink box; 3 blue spools | 0 paths, Solvable=false, FailureReason names the unmatchable color |
| 3 | 5 pink boxes (45 balls); 3 pink spools (9 balls capacity); cap=24 | 0 paths (overflow inevitable) |
| 4 | Arrow box A points to box B; spools matched | exactly 1 path (B must precede A) |
| 5 | Tunnel queue=[pink, blue]; spools 3 pink + 3 blue | exactly 1 path (two tunnel taps in order) |
| 6 | 2 boxes (pink + blue) + matching spools, ample capacity | exactly 2 paths |
| 7 | Many independent matched boxes (high branching) | WinPathCount = cap, CountWasCapped = true |
| 8 | Same fixture twice | identical results (determinism) |
| 9 | Two columns both red FillCount=2; tap delivers 9 red | greedy resolution yields canonical post-state |

### `YarnTwistSpoolAutofillerTests.cs` (Edit Mode)

| # | Fixture | Assertion |
|---|---|---|
| 1 | Same grid + seed + D twice | byte-identical top section |
| 2 | Any seed | output passes `YarnColorBalanceRule` |
| 3 | D=1 vs D=10 over 10 seeds | mean WinPathCount strictly higher for D=1 |
| 4 | D varies; count `hidden=true` spools | matches `round(total × HiddenSpoolRatio.Evaluate(D)/100)` within ±1 |
| 5 | Borderline grid, cap=24 vs cap=30 | candidate succeeding at 30 fails (or differs) at 24 |
| 6 | Impossible target band (e.g. WinPathTarget=0 on solvable grid) | Returns best-so-far + `Succeeded=false` |
| 7 | Empty grid | `Succeeded=true`, empty TopSection |

### Manual verification (must run in Unity)

- Open Level Editor, select `YarnTwistProfile`; confirm "Spool Analysis" panel appears in the right column.
- Paint a small grid (e.g. 3 boxes of 2 colors); press **Analyze**; confirm win-path count + elapsed time displayed.
- Paint a 7×7 grid with mixed boxes, ~2 arrows, 1 tunnel; press **Auto-fill**; confirm top section populates with spool columns and rule errors clear.
- Sweep Difficulty 1 / 5 / 10; confirm hidden ratio in the resulting spools visibly tracks Difficulty.
- Lock seed 🔒, press Auto-fill twice; confirm identical top section.
- Press Ctrl-Z after Auto-fill; confirm prior top section restored.
- Switch profile to `YAKProfile`; confirm panel disappears (YAK has no analyzer wired).

---

## File list

### Layer 1 — `Packages/com.hoppa.leveleditor.core/Editor/Analysis/` (new)

```
ILevelAnalyzer.cs
LevelAnalyzerAsset.cs
AnalysisRequest.cs
LevelAnalysisResult.cs
ILevelCompleter.cs
LevelCompleterAsset.cs
CompletionRequest.cs
LevelCompletionResult.cs
AutofillPanel.cs
```

### Layer 1 — modified

- `GameProfile.cs` — add `_levelAnalyzer`, `_levelCompleter`, `_extensions` + `GetExtension<T>()`
- `LevelEditorWindow.cs` — conditionally slot `AutofillPanel` into the right column

### Layer 2 — `Assets/YarnTwist/Editor/Analysis/` (new)

```
YarnTwistLevelAnalyzer.cs
YarnTwistSpoolAutofiller.cs
YarnTwistSpoolAutofillConfig.cs
```

### Layer 2 — assets (new)

```
Assets/YarnTwist/Data/Config/YarnTwistLevelAnalyzer.asset
Assets/YarnTwist/Data/Config/YarnTwistSpoolAutofiller.asset
Assets/YarnTwist/Data/Config/YarnTwistSpoolAutofillConfig.asset
```

Modified:
- `Assets/YarnTwist/Data/Config/YarnTwistProfile.asset` — wires `_levelAnalyzer` + `_levelCompleter` refs.

### Tests — `Assets/YarnTwist/Tests/Editor/` (new)

```
YarnTwistLevelAnalyzerTests.cs
YarnTwistSpoolAutofillerTests.cs
```

Every new `.cs` file (Layer 1 and Layer 2) ships with its `.meta` file committed (per existing repo convention — see `feedback_no_absolute_paths_in_committed_assets` and the prior `MultiSelectPanel.cs.meta` incident).

---

## Out-of-scope follow-ups (not in v1)

1. **YAK Layer 2.** YAK has spool columns too; an analyzer/completer pair can ship later by subclassing the same two Layer 1 contracts. No Layer 1 changes needed at that point.
2. **Migrate `_generatorConfig` off `GameProfile`.** The existing `_levelGenerator` + `_generatorConfig` pair on `GameProfile` is the same anti-pattern this spec corrects. Cleanup: move `_generatorConfig` into a `[SerializeField]` on `YarnTwistLevelGenerator.asset`, expose via `public virtual ScriptableObject Config => null;` on `LevelGeneratorAsset`, and update `GeneratorModePanel` to read `profile.LevelGenerator.Config`. Small, mechanical, separate task.
3. **Async / cancellable analysis.** v1 is synchronous with hard timeouts. If designer feedback shows long puzzles freeze the editor noticeably, lift to a background `Task` with a cancel button.
4. **Hidden-aware path counting.** "Win paths discoverable with hidden info" requires modelling player belief states. v2 if anyone asks for it.
5. **Per-level conveyor capacity in `LevelDocument`.** If the game decides to support level-specific capacity overrides, surface a field on the YarnTwist game-data side and let the panel read it as a default.

---

## Tag / deploy notes

- Layer 1 changes deploy via the usual UPM tag bump in this repo + consumer `manifest.json` bump in YarnTwist. Likely `v0.5.17` after the existing `v0.5.16`.
- Layer 2 YarnTwist files live in this repo's `Assets/YarnTwist/` and must also be synced to the YarnTwist game repo at `Assets/_YAT/Scripts/Editor/...` (see `SESSION_NOTES.md` § "YarnTwist Layer 2 target paths").
