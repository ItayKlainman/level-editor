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

        // New-semantics (2026-07-15): Difficulty is the interleave/juggle knob, not a
        // main-color "dig" deferral (superseded — see design 2026-07-15 §5). The knob no
        // longer moves the FIRST pull off the highest-flow accessible color; that first pull
        // stays flow-greedy at every difficulty. On a real picture the background/outline is
        // the accessible bulk (most flow), so it is still pulled first even at difficulty 5 —
        // interleave only kicks in AFTER, once a comparable-flow color opens. This asserts
        // that head-of-queue property with the background as the bulk.
        [Test]
        public void Arrange_HighDifficulty_DefersMainAfterBackground()
        {
            // 6x1 open strip (all border-accessible): 2 "M" (main subject) + 4 "B"
            // (background bulk, the higher-flow color).
            var g = new GridData<ICellData>(6, 1);
            g.Set(0, 0, new BBPixelCell { ColorId = "M" });
            g.Set(1, 0, new BBPixelCell { ColorId = "M" });
            g.Set(2, 0, new BBPixelCell { ColorId = "B" });
            g.Set(3, 0, new BBPixelCell { ColorId = "B" });
            g.Set(4, 0, new BBPixelCell { ColorId = "B" });
            g.Set(5, 0, new BBPixelCell { ColorId = "B" });
            var buses = new List<BusEntry>
            {
                new BusEntry { ColorId = "M", Capacity = 2 },
                new BusEntry { ColorId = "B", Capacity = 4 },
            };
            var main = new HashSet<string>(StringComparer.Ordinal) { "M" };
            var res = BusBuddiesConstructiveArranger.Arrange(g, buses, 1, 5, 5, main, new System.Random(1));
            Assert.IsTrue(res.Solvable);
            // Single column: head (Buses[0]) is pulled first. The accessible background bulk
            // (higher flow) leads at difficulty 5; interleave happens after the head.
            Assert.AreEqual("B", res.Queue.Columns[0].Buses[0].ColorId, "high difficulty still front-loads the accessible background bulk");
        }

        // Flatten a queue into round-robin PLAY order (real step i pulls column i%columns):
        // depth 0 across all columns, then depth 1, etc. Returns the ColorId sequence.
        private static List<string> PlayOrderColors(BusQueueData q, int columns)
        {
            var colors = new List<string>();
            int maxDepth = 0;
            foreach (var col in q.Columns) maxDepth = Math.Max(maxDepth, col.Buses.Count);
            for (int d = 0; d < maxDepth; d++)
                for (int c = 0; c < columns; c++)
                    if (c < q.Columns.Count && d < q.Columns[c].Buses.Count)
                        colors.Add(q.Columns[c].Buses[d].ColorId);
            return colors;
        }

        // How many interior buses appear BEFORE the last outline bus in play order — the
        // interleave metric. 0 = outline fully front-loaded (flow-greedy); higher = the peel
        // switches into the interior before the outline is exhausted (juggling more colors).
        private static int InteriorBeforeLastOutline(
            BusQueueData q, int columns, string interiorId, string outlineId)
        {
            var colors = PlayOrderColors(q, columns);
            int lastOutline = colors.LastIndexOf(outlineId);
            if (lastOutline < 0) return 0;
            int count = 0;
            for (int i = 0; i < lastOutline; i++)
                if (colors[i] == interiorId) count++;
            return count;
        }

        // Enclosed-scatter picture: a 9x9 solid outline "O" block (border-accessible, peels
        // ring-by-ring inward) with a lattice of isolated interior "I" singletons. The
        // interior stays low-flow (a couple cells reachable at a time) while the outline flow
        // dominates — so flow-greedy (difficulty 1) drains the whole outline first, but the
        // difficulty-5 spread penalty forces the peel to switch into the ready interior early.
        private static GridData<ICellData> ScatterEnclosedGrid()
        {
            const int N = 9;
            var g = new GridData<ICellData>(N + 2, N + 2);
            for (int y = 1; y <= N; y++)
            for (int x = 1; x <= N; x++)
                g.Set(x, y, new BBPixelCell { ColorId = "O" });
            for (int y = 3; y <= N - 2; y += 3)
            for (int x = 3; x <= N - 2; x += 3)
                g.Set(x, y, new BBPixelCell { ColorId = "I" }); // 4 isolated interior cells
            return g;
        }

        private static List<BusEntry> ScatterEnclosedBuses(GridData<ICellData> g)
        {
            int oc = 0, ic = 0;
            foreach (var cell in g.Cells)
                if (cell is IColoredCell c)
                { if (c.ColorId == "O") oc++; else if (c.ColorId == "I") ic++; }
            var buses = new List<BusEntry>();
            int r = oc; while (r > 0) { int cap = Math.Min(8, r); buses.Add(new BusEntry { ColorId = "O", Capacity = cap }); r -= cap; }
            r = ic; while (r > 0) { int cap = Math.Min(1, r); buses.Add(new BusEntry { ColorId = "I", Capacity = cap }); r -= cap; }
            return buses;
        }

        // The proof the knob works: difficulty 5 interleaves interior with the remaining
        // outline (more interior-before-last-outline) than difficulty 1, which front-loads
        // the whole outline. Same buses, same seed — only difficulty differs.
        [Test]
        public void Difficulty_ChangesArrangement_MoreInterleaveAtHighDifficulty()
        {
            var g = ScatterEnclosedGrid();
            var main = new HashSet<string>(StringComparer.Ordinal) { "I", "O" };

            var easy = BusBuddiesConstructiveArranger.Arrange(
                g, ScatterEnclosedBuses(g), columns: 3, activeSlots: 5, difficulty: 1, mainColors: main, rng: new System.Random(1));
            var hard = BusBuddiesConstructiveArranger.Arrange(
                g, ScatterEnclosedBuses(g), columns: 3, activeSlots: 5, difficulty: 5, mainColors: main, rng: new System.Random(1));

            Assert.IsTrue(easy.Solvable, "difficulty 1 must stay solvable");
            Assert.IsTrue(hard.Solvable, "difficulty 5 must stay solvable");

            int easyInterleave = InteriorBeforeLastOutline(easy.Queue, 3, "I", "O");
            int hardInterleave = InteriorBeforeLastOutline(hard.Queue, 3, "I", "O");
            Assert.Greater(hardInterleave, easyInterleave,
                $"difficulty 5 must interleave more than difficulty 1 (easy={easyInterleave}, hard={hardInterleave})");
        }

        // No regression on easy: difficulty 1 front-loads the whole outline (flow-greedy),
        // so no interior bus precedes the last outline bus.
        [Test]
        public void Arrange_Difficulty1_MatchesFlowGreedy_Baseline()
        {
            var g = ScatterEnclosedGrid();
            var main = new HashSet<string>(StringComparer.Ordinal) { "I", "O" };
            var res = BusBuddiesConstructiveArranger.Arrange(
                g, ScatterEnclosedBuses(g), columns: 3, activeSlots: 5, difficulty: 1, mainColors: main, rng: new System.Random(1));
            Assert.IsTrue(res.Solvable);
            Assert.AreEqual(0, InteriorBeforeLastOutline(res.Queue, 3, "I", "O"),
                "difficulty 1 must front-load the outline (no interior before the last outline bus)");
        }

        private static string Serialize(BusQueueData q)
            => Newtonsoft.Json.Linq.JObject.FromObject(q).ToString(Newtonsoft.Json.Formatting.None);

        // A dithered filled picture (like real image output): a big "blue" body with
        // several SCATTERED interior colors, each an isolated singleton surrounded by
        // blue so it only becomes reachable deep into the peel. This is the whale case
        // the designer hit: a naive "pull any color that has one accessible cell" greedy
        // grabs all the scattered-color buses at once, clogs the 5 Active slots with
        // half-empty buses, starves the peeling color, and stalls -> falls back to an
        // unsolvable queue. The level IS solvable (peel all blue first, then the interior),
        // so the arranger must find it.
        private static GridData<ICellData> DitheredGrid(out Dictionary<string, int> perColor)
        {
            const int W = 18, H = 18;
            var g = new GridData<ICellData>(W, H);
            for (int y = 1; y <= 16; y++)
            for (int x = 1; x <= 16; x++)
                g.Set(x, y, new BBPixelCell { ColorId = "blue" });

            string[] interior = { "ocean", "sky", "turq", "magenta", "gold" };
            int k = 0;
            for (int y = 4; y <= 13; y += 3)
            for (int x = 4; x <= 13; x += 3)
                g.Set(x, y, new BBPixelCell { ColorId = interior[k++ % interior.Length] });

            perColor = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var cell in g.Cells)
                if (cell is IColoredCell c && !string.IsNullOrEmpty(c.ColorId))
                { perColor.TryGetValue(c.ColorId, out var n); perColor[c.ColorId] = n + 1; }
            return g;
        }

        [Test]
        public void Arrange_DitheredFilledPicture_StaysSolvable_AllSeeds()
        {
            var g = DitheredGrid(out var perColor);
            // Big buses (cap 20) — the whale used caps of 20-25, which is what makes a
            // scattered-color bus linger half-empty and clog the Active Row.
            var baseBuses = new List<BusEntry>();
            foreach (var kv in perColor)
            {
                int rem = kv.Value;
                while (rem > 0) { int cap = Math.Min(20, rem); baseBuses.Add(new BusEntry { ColorId = kv.Key, Capacity = cap }); rem -= cap; }
            }
            var main = new HashSet<string>(perColor.Keys, StringComparer.Ordinal); // all "main", difficulty maxed = worst case

            for (int seed = 1; seed <= 25; seed++)
            {
                var res = BusBuddiesConstructiveArranger.Arrange(
                    g, baseBuses, columns: 3, activeSlots: 5, difficulty: 5, mainColors: main, rng: new System.Random(seed));
                Assert.IsTrue(res.Solvable, $"seed {seed}: dithered filled picture must arrange solvable (scattered-color clog)");
            }
        }

        // Round-to-5 semantics after a difficulty-5 (interleaved) arrange must stay solvable:
        // the within-color round-to-head sort, guarded by exact replay, never breaks the
        // solvable-by-construction queue even on the dithered clog case at max difficulty.
        [Test]
        public void Arrange_ThenSortRoundToHead_StaysSolvable_Difficulty5()
        {
            var g = DitheredGrid(out var perColor);
            var baseBuses = new List<BusEntry>();
            foreach (var kv in perColor)
            {
                int rem = kv.Value;
                while (rem > 0) { int cap = Math.Min(20, rem); baseBuses.Add(new BusEntry { ColorId = kv.Key, Capacity = cap }); rem -= cap; }
            }
            var main = new HashSet<string>(perColor.Keys, StringComparer.Ordinal);

            for (int seed = 1; seed <= 10; seed++)
            {
                var res = BusBuddiesConstructiveArranger.Arrange(
                    g, baseBuses, columns: 3, activeSlots: 5, difficulty: 5, mainColors: main, rng: new System.Random(seed));
                Assert.IsTrue(res.Solvable, $"seed {seed}: precondition arranged queue is solvable");
                var sorted = BusBuddiesConstructiveArranger.SortRoundToHead(res.Queue, g, columns: 3, activeSlots: 5);
                Assert.IsTrue(
                    BusBuddiesConstructiveArranger.IsSolvable(g, sorted, columns: 3, activeSlots: 5),
                    $"seed {seed}: round-to-head sort must keep the difficulty-5 queue solvable");
            }
        }
    }
}
