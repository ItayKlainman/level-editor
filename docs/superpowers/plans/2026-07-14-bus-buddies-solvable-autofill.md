# Bus Buddies Solvable-by-Construction Auto-fill — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Bus Buddies auto-fill incapable of producing an unsolvable level by building the bus queue from a simulated border-inward peel and verifying it by exact replay.

**Architecture:** A new pure-static `BusBuddiesConstructiveArranger` (editor) drives the runtime `BusSimState` over a one-bus-per-column scratch model to derive a winning release order, distributes it round-robin across the real columns, and confirms it by exact replay. The autofiller calls it instead of the random dig-search + Monte-Carlo gate; the average-player still runs but only for the measured-APS read-out. The panel shows a loud solvable/unsolvable `HelpBox`.

**Tech Stack:** Unity C# (Editor + Runtime asmdefs), NUnit EditMode tests, Newtonsoft.Json, the existing `Hoppa.BusBuddies.Sim` simulator.

## Global Constraints

- Determinism: same settings + seed → identical queue (existing test `Autofill_Deterministic_ForFixedSeed` must stay green).
- Head semantics: `BusColumn.Buses[0]` is the tappable HEAD (pulled first).
- No changes to the runtime sim's accessibility / nearest-to-hole / deadlock rules.
- No BusBuddies **game project** changes.
- Balance invariant preserved: per-color bus capacity sums to per-color block count (partitioning is unchanged).
- Keep `BusBuddiesDigArranger` and `BusBuddiesColorRoles` classes intact (other tests depend on them); only stop *calling* the dig-arranger from the autofiller's solvability path.
- Namespaces: arranger in `Hoppa.BusBuddies.Editor`; tests in `Hoppa.BusBuddies.Editor.Tests`.

---

### Task 1: Constructive arranger — solvable-by-construction core (flow-preserving)

Builds the arranger that guarantees a solvable queue, ignoring the Difficulty knob for now (Task 2 adds the dig bias). Also lands the reproduction tests proving the failure mode.

**Files:**
- Create: `Assets/BusBuddies/Editor/Analysis/BusBuddiesConstructiveArranger.cs`
- Test: `Assets/BusBuddies/Tests/Editor/BusBuddiesConstructiveArrangerTests.cs`

**Interfaces:**
- Consumes: `Hoppa.BusBuddies.Sim.BusLevelModel.Build(GridData<ICellData>, BusQueueData, int)`, `BusSimState(BusLevelModel)` with public `Cell`, `M`, `IsAccessible(int,int)`, `CanPull(int)`, `FreeSlot()`, `ApplyMove(int)`, `IsWin()`; data types `BusEntry`, `BusColumn`, `BusQueueData`.
- Produces: `BusBuddiesConstructiveArranger.Arrange(GridData<ICellData> grid, IReadOnlyList<BusEntry> buses, int columns, int activeSlots, int difficulty, ISet<string> mainColors, System.Random rng) → Result { BusQueueData Queue; bool Solvable; }`

- [ ] **Step 1: Write the failing tests**

Create `Assets/BusBuddies/Tests/Editor/BusBuddiesConstructiveArrangerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.BusBuddies;
using Hoppa.BusBuddies.Editor;
using Hoppa.BusBuddies.Sim;

namespace Hoppa.BusBuddies.Editor.Tests
{
    // Solvable-by-construction arranger. Ring grid: 7x7 empty margin, 5x5 object at
    // [1..5] with an "O" outer ring (16 blocks) enclosing a 3x3 "I" interior (9 blocks).
    // Flood-accessibility means O must be peeled before any I is reachable.
    public sealed class BusBuddiesConstructiveArrangerTests
    {
        private static GridData<ICellData> RingGrid(out int oBlocks, out int iBlocks)
        {
            var g = new GridData<ICellData>(7, 7);
            oBlocks = 0; iBlocks = 0;
            for (int y = 1; y <= 5; y++)
            for (int x = 1; x <= 5; x++)
            {
                bool inner = x >= 2 && x <= 4 && y >= 2 && y <= 4;
                g.Set(x, y, new BBPixelCell { ColorId = inner ? "I" : "O" });
                if (inner) iBlocks++; else oBlocks++;
            }
            return g;
        }

        private static void AssertBalanced(GridData<ICellData> grid, BusQueueData q)
        {
            var blocks = new Dictionary<string, int>();
            foreach (var cell in grid.Cells)
                if (cell is IColoredCell c && !string.IsNullOrEmpty(c.ColorId))
                { blocks.TryGetValue(c.ColorId, out var n); blocks[c.ColorId] = n + 1; }
            var caps = new Dictionary<string, int>();
            foreach (var col in q.Columns)
                foreach (var b in col.Buses)
                { caps.TryGetValue(b.ColorId, out var n); caps[b.ColorId] = n + b.Capacity; }
            foreach (var kv in blocks)
            { caps.TryGetValue(kv.Key, out var cap); Assert.AreEqual(kv.Value, cap, $"color {kv.Key} unbalanced"); }
        }

        // Reproduction: interior buses ahead of the outline clog all 5 Active slots
        // before the outline is reachable -> genuine deadlock (the shipped bug).
        [Test]
        public void Reproduction_InteriorBuriedAheadOfOutline_Deadlocks()
        {
            var g = RingGrid(out _, out _);
            var q = new BusQueueData();
            var col = new BusColumn();
            for (int k = 0; k < 9; k++) col.Buses.Add(new BusEntry { ColorId = "I", Capacity = 1 });
            col.Buses.Add(new BusEntry { ColorId = "O", Capacity = 16 });
            q.Columns.Add(col);
            var s = new BusSimState(BusLevelModel.Build(g, q, 5));
            for (int k = 0; k < 5; k++) s.ApplyMove(0); // 5 enclosed I buses fill the row
            Assert.IsTrue(s.IsDeadlock(), "enclosed interior buses clog the Active Row");
            Assert.IsFalse(s.HasLegalMove());
        }

        [Test]
        public void Arrange_RingGrid_ProducesSolvableAndBalanced()
        {
            var g = RingGrid(out int o, out int i);
            Assert.AreEqual(16, o); Assert.AreEqual(9, i);
            var buses = new List<BusEntry>();
            for (int k = 0; k < 9; k++) buses.Add(new BusEntry { ColorId = "I", Capacity = 1 }); // worst case
            buses.Add(new BusEntry { ColorId = "O", Capacity = 8 });
            buses.Add(new BusEntry { ColorId = "O", Capacity = 8 });
            var main = new HashSet<string>(StringComparer.Ordinal) { "I", "O" };

            var res = BusBuddiesConstructiveArranger.Arrange(
                g, buses, columns: 3, activeSlots: 5, difficulty: 1, mainColors: main, rng: new System.Random(1));

            Assert.IsTrue(res.Solvable, "arranger must produce a solvable order for a ringed picture");
            AssertBalanced(g, res.Queue);
        }

        [Test]
        public void Arrange_EmptyBuses_IsTriviallySolvable()
        {
            var g = new GridData<ICellData>(2, 2);
            for (int k = 0; k < g.Cells.Length; k++) g.Cells[k] = new BBEmptyCell();
            var res = BusBuddiesConstructiveArranger.Arrange(
                g, new List<BusEntry>(), columns: 3, activeSlots: 5, difficulty: 3, mainColors: null, rng: new System.Random(1));
            Assert.IsTrue(res.Solvable);
            Assert.AreEqual(3, res.Queue.Columns.Count);
        }

        // Honest failure: buses that cannot clear the board (unbalanced) -> not solvable.
        [Test]
        public void Arrange_Unbalanced_ReportsNotSolvable()
        {
            var g = RingGrid(out _, out _);
            var buses = new List<BusEntry>
            {
                new BusEntry { ColorId = "O", Capacity = 8 }, // only 8 of 16 O; 0 of 9 I
            };
            var res = BusBuddiesConstructiveArranger.Arrange(
                g, buses, columns: 2, activeSlots: 5, difficulty: 1, mainColors: null, rng: new System.Random(1));
            Assert.IsFalse(res.Solvable);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run (MCP): `tests-run` EditMode, filter class `BusBuddiesConstructiveArrangerTests`.
Expected: FAIL to compile — `BusBuddiesConstructiveArranger` does not exist.

- [ ] **Step 3: Write the arranger (flow-preserving)**

Create `Assets/BusBuddies/Editor/Analysis/BusBuddiesConstructiveArranger.cs`:

```csharp
using System;
using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Hoppa.BusBuddies.Sim;

namespace Hoppa.BusBuddies.Editor
{
    // Builds a solvable-by-construction bus queue. Simulates a border-inward peel
    // against the real N-slot sim using a one-bus-per-column scratch model, then
    // distributes that winning order round-robin across the requested columns
    // (within-column order preserved, so the next needed bus is always at some
    // column's head). Verified by exact replay. Deterministic given `rng`.
    public static class BusBuddiesConstructiveArranger
    {
        public struct Result
        {
            public BusQueueData Queue;
            public bool Solvable;
        }

        public static Result Arrange(
            GridData<ICellData> grid,
            IReadOnlyList<BusEntry> buses,
            int columns, int activeSlots, int difficulty,
            ISet<string> mainColors, System.Random rng)
        {
            columns = Math.Max(1, columns);
            rng ??= new System.Random(0);
            var queue = new BusQueueData();
            for (int i = 0; i < columns; i++) queue.Columns.Add(new BusColumn());
            if (buses == null || buses.Count == 0)
                return new Result { Queue = queue, Solvable = true };

            // Scratch model: one bus per column -> the scheduler may pick ANY bus next;
            // the Active-Row slot count is still enforced.
            var scratchQueue = new BusQueueData();
            foreach (var b in buses)
            {
                var col = new BusColumn();
                col.Buses.Add(b);
                scratchQueue.Columns.Add(col);
            }
            var model = BusLevelModel.Build(grid, scratchQueue, Math.Max(1, activeSlots));

            var order = Schedule(model, mainColors, difficulty, rng);
            if (order == null)
                return new Result { Queue = queue, Solvable = false };

            for (int i = 0; i < order.Count; i++)
                queue.Columns[i % columns].Buses.Add(buses[order[i]]);

            bool solvable = ReplayWins(grid, queue, columns, activeSlots);
            return new Result { Queue = queue, Solvable = solvable };
        }

        // Greedy peel. Returns scratch-column indices (== bus identities) in head->tail
        // order, or null if it stalls before the board is cleared.
        private static List<int> Schedule(
            BusLevelModel model, ISet<string> mainColors, int difficulty, System.Random rng)
        {
            var s = new BusSimState(model);
            var order = new List<int>(model.Columns);
            int guard = model.Columns + 2;
            for (int step = 0; step < guard; step++)
            {
                if (s.IsWin()) return order;
                if (s.FreeSlot() < 0) return null;
                int chosen = PickColumn(s, model, mainColors, difficulty, rng);
                if (chosen < 0) return null;
                s.ApplyMove(chosen);
                order.Add(chosen);
            }
            return s.IsWin() ? order : null;
        }

        // Flow-preserving choice: among pullable columns whose bus color currently has
        // an accessible block, prefer the color with the MOST accessible blocks (most
        // likely to empty and free a slot). Difficulty/mainColors unused here (Task 2).
        private static int PickColumn(
            BusSimState s, BusLevelModel model, ISet<string> mainColors, int difficulty, System.Random rng)
        {
            var accCount = AccessibleCountByColor(s);
            int best = -1; long bestKey = long.MinValue;
            for (int col = 0; col < model.Columns; col++)
            {
                if (!s.CanPull(col)) continue;
                int color = model.BusColor[col][0];
                if (!accCount.TryGetValue(color, out var flow) || flow <= 0) continue;
                long score = (long)flow * 1000 + rng.Next(0, 1000);
                if (score > bestKey) { bestKey = score; best = col; }
            }
            return best;
        }

        // color index -> number of currently-accessible remaining blocks of that color.
        private static Dictionary<int, int> AccessibleCountByColor(BusSimState s)
        {
            var map = new Dictionary<int, int>();
            int w = s.M.W;
            for (int i = 0; i < s.Cell.Length; i++)
            {
                int c = s.Cell[i];
                if (c < 0) continue;
                if (s.IsAccessible(i % w, i / w))
                { map.TryGetValue(c, out var n); map[c] = n + 1; }
            }
            return map;
        }

        // Exact replay of the round-robin pull sequence through the real arrangement.
        // Reproduces the derived order (real step i pulls column i%columns), so it wins
        // iff the scratch schedule won; also guards the distribution logic.
        private static bool ReplayWins(
            GridData<ICellData> grid, BusQueueData queue, int columns, int activeSlots)
        {
            var model = BusLevelModel.Build(grid, queue, Math.Max(1, activeSlots));
            var s = new BusSimState(model);
            int total = 0;
            foreach (var col in queue.Columns) total += col.Buses.Count;
            for (int i = 0; i < total; i++)
            {
                int col = i % columns;
                if (!s.CanPull(col) || s.FreeSlot() < 0) return false;
                s.ApplyMove(col);
            }
            return s.IsWin();
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run (MCP): `tests-run` EditMode, filter class `BusBuddiesConstructiveArrangerTests`.
Expected: PASS (4 tests). If `Arrange_RingGrid...` fails, the scheduler stalled — confirm `Schedule` returns on `IsWin()` and that `AccessibleCountByColor` reads `s.Cell`/`IsAccessible` correctly.

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Editor/Analysis/BusBuddiesConstructiveArranger.cs Assets/BusBuddies/Editor/Analysis/BusBuddiesConstructiveArranger.cs.meta Assets/BusBuddies/Tests/Editor/BusBuddiesConstructiveArrangerTests.cs Assets/BusBuddies/Tests/Editor/BusBuddiesConstructiveArrangerTests.cs.meta
git commit -m "feat(busbuddies): constructive solvable-by-construction bus arranger"
```

---

### Task 2: Difficulty "dig" bias within the solvable envelope + determinism

Adds the Difficulty knob back in as a tie-break that defers `main` colors *among ready candidates only*, so it can never bury a color into unsolvability.

**Files:**
- Modify: `Assets/BusBuddies/Editor/Analysis/BusBuddiesConstructiveArranger.cs` (replace `PickColumn`)
- Test: `Assets/BusBuddies/Tests/Editor/BusBuddiesConstructiveArrangerTests.cs` (add tests)

**Interfaces:**
- Consumes: `BusLevelModel.ColorNames` (color index → colorId), plus everything from Task 1.
- Produces: no signature change; `Arrange` now honors `difficulty` + `mainColors`.

- [ ] **Step 1: Write the failing tests**

Append these tests to `BusBuddiesConstructiveArrangerTests.cs` (inside the class):

```csharp
        [Test]
        public void Arrange_Deterministic_ForFixedSeed()
        {
            var g = RingGrid(out _, out _);
            List<BusEntry> Buses()
            {
                var b = new List<BusEntry>();
                for (int k = 0; k < 3; k++) b.Add(new BusEntry { ColorId = "I", Capacity = 3 });
                b.Add(new BusEntry { ColorId = "O", Capacity = 8 });
                b.Add(new BusEntry { ColorId = "O", Capacity = 8 });
                return b;
            }
            var main = new HashSet<string>(StringComparer.Ordinal) { "I" };
            var a = BusBuddiesConstructiveArranger.Arrange(g, Buses(), 3, 5, 5, main, new System.Random(99));
            var b2 = BusBuddiesConstructiveArranger.Arrange(g, Buses(), 3, 5, 5, main, new System.Random(99));
            Assert.AreEqual(Serialize(a.Queue), Serialize(b2.Queue), "same seed -> identical queue");
        }

        // Solvability is preserved at EVERY difficulty (the core guarantee).
        [Test]
        public void Arrange_AllDifficulties_StaySolvable()
        {
            var g = RingGrid(out _, out _);
            var main = new HashSet<string>(StringComparer.Ordinal) { "I", "O" };
            for (int d = 1; d <= 5; d++)
            {
                var buses = new List<BusEntry>();
                for (int k = 0; k < 9; k++) buses.Add(new BusEntry { ColorId = "I", Capacity = 1 });
                buses.Add(new BusEntry { ColorId = "O", Capacity = 8 });
                buses.Add(new BusEntry { ColorId = "O", Capacity = 8 });
                var res = BusBuddiesConstructiveArranger.Arrange(g, buses, 3, 5, d, main, new System.Random(d));
                Assert.IsTrue(res.Solvable, $"difficulty {d} must stay solvable");
            }
        }

        // With a freely-accessible background blob, high difficulty defers the MAIN
        // color: the background bus is pulled (head-most) before the main color.
        [Test]
        public void Arrange_HighDifficulty_DefersMainAfterBackground()
        {
            // 5x1 open strip (all border-accessible): 3 "M" (main) + 2 "B" (background).
            var g = new GridData<ICellData>(5, 1);
            g.Set(0, 0, new BBPixelCell { ColorId = "M" });
            g.Set(1, 0, new BBPixelCell { ColorId = "M" });
            g.Set(2, 0, new BBPixelCell { ColorId = "M" });
            g.Set(3, 0, new BBPixelCell { ColorId = "B" });
            g.Set(4, 0, new BBPixelCell { ColorId = "B" });
            var buses = new List<BusEntry>
            {
                new BusEntry { ColorId = "M", Capacity = 3 },
                new BusEntry { ColorId = "B", Capacity = 2 },
            };
            var main = new HashSet<string>(StringComparer.Ordinal) { "M" };
            var res = BusBuddiesConstructiveArranger.Arrange(g, buses, 1, 5, 5, main, new System.Random(1));
            Assert.IsTrue(res.Solvable);
            // Single column: head (Buses[0]) is pulled first. High difficulty defers M,
            // so B should be at the head.
            Assert.AreEqual("B", res.Queue.Columns[0].Buses[0].ColorId, "high difficulty pulls background before main");
        }

        private static string Serialize(BusQueueData q)
            => Newtonsoft.Json.Linq.JObject.FromObject(q).ToString(Newtonsoft.Json.Formatting.None);
```

Add `using Newtonsoft.Json.Linq;` is not required because `Serialize` uses the fully-qualified name.

- [ ] **Step 2: Run the tests to verify they fail**

Run (MCP): `tests-run` EditMode, filter class `BusBuddiesConstructiveArrangerTests`.
Expected: `Arrange_HighDifficulty_DefersMainAfterBackground` FAILS (flow-preserving ignores difficulty; M has more accessible blocks so it is pulled first). The other new tests may already pass.

- [ ] **Step 3: Replace `PickColumn` with the difficulty-biased version**

In `BusBuddiesConstructiveArranger.cs`, replace the whole `PickColumn` method with:

```csharp
        // Choose the next scratch column to pull, among pullable columns whose bus
        // color currently has an accessible block. Difficulty scales how strongly MAIN
        // colors are deferred (1 = neutral flow-preserving; 5 = defer main as late as
        // accessibility allows). Flow (accessible-block count) breaks ties so slots keep
        // emptying. Deferral only ranks among READY colors -> solvability is preserved.
        private static int PickColumn(
            BusSimState s, BusLevelModel model, ISet<string> mainColors, int difficulty, System.Random rng)
        {
            var accCount = AccessibleCountByColor(s);
            float f = (Math.Clamp(difficulty, 1, 5) - 1) / 4f; // 0..1
            int best = -1; long bestKey = long.MinValue;
            for (int col = 0; col < model.Columns; col++)
            {
                if (!s.CanPull(col)) continue;
                int color = model.BusColor[col][0];
                if (!accCount.TryGetValue(color, out var flow) || flow <= 0) continue;

                bool isMain = mainColors != null && color >= 0 && model.ColorNames != null
                              && color < model.ColorNames.Length && mainColors.Contains(model.ColorNames[color]);
                // Rank: background above main (weighted by difficulty), then flow, then rng.
                long rank = isMain ? -(long)(f * 1_000_000f) : 0L;
                long score = rank + flow * 1000 + rng.Next(0, 1000);
                if (score > bestKey) { bestKey = score; best = col; }
            }
            return best;
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run (MCP): `tests-run` EditMode, filter class `BusBuddiesConstructiveArrangerTests`.
Expected: PASS (all 7 tests). Note: at difficulty 5, `f*1_000_000` (=1,000,000) dominates the `flow*1000` term, so a background color outranks a main color even when the main color has more accessible blocks.

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Editor/Analysis/BusBuddiesConstructiveArranger.cs Assets/BusBuddies/Tests/Editor/BusBuddiesConstructiveArrangerTests.cs
git commit -m "feat(busbuddies): difficulty-dig bias within the solvable envelope"
```

---

### Task 3: Wire the autofiller to the constructive arranger (drop the Monte-Carlo gate)

Replaces the random dig-search in `BusBuddiesAutofiller.Complete` with the constructive arranger. The average player still runs, but only to populate the measured-APS read-out.

**Files:**
- Modify: `Assets/BusBuddies/Editor/Analysis/BusBuddiesAutofiller.cs` (the attempt loop, lines ~96–164)
- Test: `Assets/BusBuddies/Tests/Editor/BusBuddiesAutofillTests.cs` (add a ring-grid integration test)

**Interfaces:**
- Consumes: `BusBuddiesConstructiveArranger.Arrange(...)` (Task 1/2); existing `analyzer.Analyze`, `ShallowCopyWithTop`, `BusBuddiesCapacityMath.PartitionColor`, `BusBuddiesColorRoles.ClassifyMain`.
- Produces: `LevelCompletionResult` with `Succeeded` set from the constructive replay (not the bot).

- [ ] **Step 1: Write the failing test**

Append to `BusBuddiesAutofillTests.cs` (inside the class), reusing its existing `MakeProfile` / `MakeAutofiller` / `Doc` / `SetField` helpers:

```csharp
        // Regression for the shipped bug: a ringed picture (outline enclosing an
        // interior) must auto-fill to a SOLVABLE queue even when both colors qualify as
        // "main" and difficulty is maxed.
        [Test]
        public void Autofill_RingGrid_IsSolvable()
        {
            var profile = MakeProfile();
            var af = MakeAutofiller(min: 1, max: 12, avg: 4, colMin: 1, colMax: 3, tol: 50f);

            // 7x7 empty margin; 5x5 object: "O" ring (16) around 3x3 "I" (9).
            var grid = new GridData<ICellData>(7, 7);
            for (int y = 1; y <= 5; y++)
            for (int x = 1; x <= 5; x++)
            {
                bool inner = x >= 2 && x <= 4 && y >= 2 && y <= 4;
                grid.Set(x, y, new BBPixelCell { ColorId = inner ? "I" : "O" });
            }
            var doc = Doc(grid, conveyor: 5);
            doc.GameData["difficulty"] = 5; // max dig

            var res = af.Complete(doc, profile, new CompletionRequest { Seed = 5 });

            Assert.IsNotNull(res.TopSection);
            Assert.IsTrue(res.Succeeded, "ring grid must auto-fill solvable: " + res.FailureReason);
            AssertBalanced(res.TopSection, new Dictionary<string, int> { { "O", 16 }, { "I", 9 } });
        }
```

- [ ] **Step 2: Run the test to verify it fails**

Run (MCP): `tests-run` EditMode, filter class `BusBuddiesAutofillTests`, method `Autofill_RingGrid_IsSolvable`.
Expected: FAIL — the current random dig-search + bot gate leaves `Succeeded == false` (or an unsolvable arrangement) for this ringed grid.

- [ ] **Step 3: Replace the attempt loop in `BusBuddiesAutofiller.Complete`**

In `BusBuddiesAutofiller.cs`, replace everything from the `// ── MED-2: widened search.` comment block through the `return Done(result, sw);` at the end of `Complete` (the block that builds `bestArrangement`/`bestAnalysis` via `BusBuddiesDigArranger`) with:

```csharp
            // ── Constructive, solvable-by-construction arrangement ──
            // Build the queue by simulating a border-inward peel (BusBuddiesConstructiveArranger),
            // which guarantees a winning release order and verifies it by exact replay.
            // Partition is reseeded per attempt only as a robustness retry.
            BusQueueData bestQueue = null;
            bool solvable = false;
            int attempts = Math.Max(1, cfg.MaxAttempts);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                if (sw.ElapsedMilliseconds > cfg.TotalTimeoutMs) break;
                int attemptSeed = rootRng.Next(1, int.MaxValue);
                var ar = new System.Random(attemptSeed);

                var buses = new List<BusEntry>();
                foreach (var id in colorIds)
                    foreach (var cap in BusBuddiesCapacityMath.PartitionColor(
                                 perColor[id], min, max, avg, settings.NoSingleBusColor, settings.RoundToFive, ar))
                        buses.Add(new BusEntry { ColorId = id, Capacity = cap, Hidden = false });

                var arr = BusBuddiesConstructiveArranger.Arrange(
                    doc.Grid, buses, columns, slots, settings.Difficulty, mainColors, ar);
                result.CandidatesTried++;
                if (arr.Solvable) { bestQueue = arr.Queue; solvable = true; break; }
                if (bestQueue == null) bestQueue = arr.Queue; // honest fallback
            }

            result.TopSection = JObject.FromObject(bestQueue);
            result.Succeeded  = solvable;

            // Measured APS read-out (NOT a gate) from the analyzer on the chosen queue.
            var apsDoc = ShallowCopyWithTop(doc, result.TopSection);
            result.Analysis = analyzer.Analyze(apsDoc, profile, new AnalysisRequest
            {
                ConveyorCapacityOverride = slots,
                RolloutCount = cfg.SearchRolloutCount,
                NodeBudget   = cfg.SearchNodeBudget,
                TimeoutMs    = cfg.SearchTimeoutMs,
                Seed         = seed,
            });

            if (!solvable)
                result.FailureReason =
                    "could not construct a solvable arrangement — the grid may be color-unbalanced " +
                    "or contain a fully-enclosed region no bus order can reach";
            return Done(result, sw);
```

Confirm `using System;` and `using System.Collections.Generic;` are present at the top of the file (they are). The `Validate` local function and the `BusBuddiesDigArranger.Arrange` call are removed by this replacement.

- [ ] **Step 4: Run the tests to verify they pass**

Run (MCP): `tests-run` EditMode, filter class `BusBuddiesAutofillTests`.
Expected: PASS — the new `Autofill_RingGrid_IsSolvable` plus all pre-existing autofill tests (balanced / empty / deterministic / no-analyzer) stay green.

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Editor/Analysis/BusBuddiesAutofiller.cs Assets/BusBuddies/Tests/Editor/BusBuddiesAutofillTests.cs
git commit -m "feat(busbuddies): autofiller uses constructive arranger; bot demoted to APS read-out"
```

---

### Task 4: Panel — prominent solvable / unsolvable feedback

Replaces the soft grey status line with a colored `HelpBox` so a designer can never miss an unsolvable result.

**Files:**
- Modify: `Assets/BusBuddies/Editor/UI/BusBuddiesDifficultyPanel.cs`

**Interfaces:**
- Consumes: `LevelCompletionResult.Succeeded` / `.FailureReason` (unchanged).
- Produces: no public API change (UI only).

- [ ] **Step 1: Add a status-severity field**

In `BusBuddiesDifficultyPanel.cs`, next to the existing `private string _status;` field, add:

```csharp
        private UnityEditor.MessageType _statusType = UnityEditor.MessageType.None;
```

- [ ] **Step 2: Set severity where `_status` is assigned**

In `OnAutofill`, replace the success/failure status lines:

```csharp
                    _status = res.Succeeded ? "Auto-filled (solvable)." : "Auto-filled, best-effort: " + res.FailureReason;
```
with:
```csharp
                    _status = res.Succeeded ? "Auto-filled — solvable ✓" : "Auto-fill could NOT make this level solvable:\n" + res.FailureReason;
                    _statusType = res.Succeeded ? UnityEditor.MessageType.Info : UnityEditor.MessageType.Error;
```

and in the `else` branch (no top section), after `_status = "Auto-fill failed: " + ...;` add:
```csharp
                    _statusType = UnityEditor.MessageType.Error;
```

and in the `catch` block, after `_status = "Auto-fill failed: " + ex.Message;` add:
```csharp
                _statusType = UnityEditor.MessageType.Error;
```

In `OnApply`, after `_status = "Settings applied to level.";` add:
```csharp
            _statusType = UnityEditor.MessageType.Info;
```

- [ ] **Step 3: Render the status as a HelpBox**

In `OnGUI`, replace:

```csharp
            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.LabelField(_status, EditorStyles.wordWrappedMiniLabel);
```
with:
```csharp
            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status,
                    _statusType == UnityEditor.MessageType.None ? UnityEditor.MessageType.Info : _statusType);
```

- [ ] **Step 4: Compile-verify**

Run (MCP): `assets-refresh`, then `console-get-logs` (filter errors).
Expected: no compile errors. (This is UI — no unit test; visual confirmation is a lead step.)

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Editor/UI/BusBuddiesDifficultyPanel.cs
git commit -m "feat(busbuddies): panel shows a prominent solvable/unsolvable HelpBox"
```

---

### Task 5: Full-suite regression + spec close-out

**Files:** none (verification only)

- [ ] **Step 1: Run the full EditMode suite**

Run (MCP): `tests-run` EditMode (no filter).
Expected: all BusBuddies tests green; only the one pre-existing unrelated YAK prompt-wording failure remains (per recent board entries). Zero new failures.

- [ ] **Step 2: Sanity — confirm the dig-arranger path is no longer the solvability gate**

Grep to confirm `BusBuddiesDigArranger` is not referenced by `BusBuddiesAutofiller.cs`:

Run: `grep -n "BusBuddiesDigArranger" Assets/BusBuddies/Editor/Analysis/BusBuddiesAutofiller.cs`
Expected: no matches (the class and its own tests remain, but the autofiller no longer calls it).

- [ ] **Step 3: Commit any final notes** (if the spec needs a status flip)

```bash
git add docs/superpowers/specs/2026-07-14-bus-buddies-solvable-autofill-design.md
git commit -m "docs(busbuddies): mark solvable-autofill spec implemented"
```

## Self-Review

- **Spec coverage:** §Design.1 constructive scheduler → Task 1; §Design.2 difficulty within envelope → Task 2; §Design.3 exact replay verifier → Task 1 (`ReplayWins`) + Task 3 (Succeeded from it); §Design.4 panel hardening → Task 4; testing §1–6 → Tasks 1–3 + 5. All covered.
- **Placeholder scan:** every code step contains full code; no TBD/TODO.
- **Type consistency:** `Arrange(...)` signature and `Result { Queue, Solvable }` are identical across Tasks 1–3; `PickColumn` signature unchanged between Task 1 and Task 2; autofiller consumes the same names.
