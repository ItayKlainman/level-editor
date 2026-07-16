using System;
using System.Collections.Generic;
using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.BusBuddies;
using Hoppa.BusBuddies.Editor;
using Hoppa.BusBuddies.Sim;

namespace Hoppa.BusBuddies.Editor.Tests
{
    // BusBuddiesConstructiveArranger.CapColorRuns — the boss's "no more than 3 of the
    // same color in a row per column" rule. Fix is move-first / merge-fallback, and
    // must ALWAYS keep the level solvable and per-color pixel totals intact.
    public sealed class BusBuddiesColorRunCapTests
    {
        // Longest same-color run across all columns.
        private static int LongestRun(BusQueueData q)
        {
            int worst = 0;
            foreach (var col in q.Columns)
            {
                int run = 0; string prev = null;
                foreach (var b in col.Buses)
                {
                    if (string.Equals(b?.ColorId, prev)) run++; else { run = 1; prev = b?.ColorId; }
                    if (run > worst) worst = run;
                }
            }
            return worst;
        }

        private static bool Solvable(GridData<ICellData> grid, BusQueueData q, int slots)
            => new BusSolver(200_000, 2_000).Solve(BusLevelModel.Build(grid, q, slots)).Outcome
               == BusSolver.Outcome.Solvable;

        // color -> total capacity across all columns.
        private static Dictionary<string, int> ColorTotals(BusQueueData q)
        {
            var m = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var col in q.Columns)
                foreach (var b in col.Buses)
                { m.TryGetValue(b.ColorId, out var n); m[b.ColorId] = n + b.Capacity; }
            return m;
        }

        private static void AssertTotalsPreserved(BusQueueData before, BusQueueData after)
        {
            var a = ColorTotals(before);
            var b = ColorTotals(after);
            CollectionAssert.AreEquivalent(a.Keys, b.Keys, "color set changed");
            foreach (var kv in a)
                Assert.AreEqual(kv.Value, b[kv.Key], $"color {kv.Key} pixel total changed");
        }

        // A 1-row strip is fully border-accessible: every cell is reachable from the start.
        private static GridData<ICellData> Strip(params string[] colors)
        {
            var g = new GridData<ICellData>(colors.Length, 1);
            for (int x = 0; x < colors.Length; x++) g.Set(x, 0, new BBPixelCell { ColorId = colors[x] });
            return g;
        }

        // Mono-color column of 5 → no other column to move into, so it MERGES down to a
        // 3-run of bigger buses (the boss's "one of 50 instead of two of 25"), staying
        // solvable with the color total intact.
        [Test]
        public void CapColorRuns_MonoColorColumn_MergesToAtMost3()
        {
            var g = Strip("B", "B", "B", "B", "B");
            var q = new BusQueueData();
            var col = new BusColumn();
            for (int k = 0; k < 5; k++) col.Buses.Add(new BusEntry { ColorId = "B", Capacity = 1 });
            q.Columns.Add(col);

            Assert.AreEqual(5, LongestRun(q), "precondition: a 5-run");
            var capped = BusBuddiesConstructiveArranger.CapColorRuns(q, g, activeSlots: 5, maxRun: 3);

            Assert.LessOrEqual(LongestRun(capped), 3, "no column may keep more than 3 same-color in a row");
            Assert.IsTrue(Solvable(g, capped, 5), "capped queue must stay solvable");
            AssertTotalsPreserved(q, capped);
            Assert.Less(capped.Columns[0].Buses.Count, 5, "merging reduced the bus count");
        }

        // A 4-run beside a column ending in a different color → MOVE-first relocates one
        // bus (no merge needed), so the column keeps its bus sizes.
        [Test]
        public void CapColorRuns_MovesBusToCompatibleColumn()
        {
            var g = Strip("B", "B", "B", "B", "R");
            var q = new BusQueueData();
            var c0 = new BusColumn();
            for (int k = 0; k < 4; k++) c0.Buses.Add(new BusEntry { ColorId = "B", Capacity = 1 });
            var c1 = new BusColumn();
            c1.Buses.Add(new BusEntry { ColorId = "R", Capacity = 1 });
            q.Columns.Add(c0); q.Columns.Add(c1);

            Assert.AreEqual(4, LongestRun(q));
            var capped = BusBuddiesConstructiveArranger.CapColorRuns(q, g, activeSlots: 5, maxRun: 3);

            Assert.LessOrEqual(LongestRun(capped), 3);
            Assert.IsTrue(Solvable(g, capped, 5));
            AssertTotalsPreserved(q, capped);
            // Move (not merge) → total bus count is unchanged (5), just redistributed.
            int total = 0; foreach (var col in capped.Columns) total += col.Buses.Count;
            Assert.AreEqual(5, total, "a move preserves the bus count (no merge needed here)");
        }

        // An already-compliant queue is returned unchanged (no needless merges/moves).
        [Test]
        public void CapColorRuns_CompliantQueue_Unchanged()
        {
            var g = Strip("B", "B", "R", "R", "B");
            var q = new BusQueueData();
            var c0 = new BusColumn();
            c0.Buses.Add(new BusEntry { ColorId = "B", Capacity = 1 });
            c0.Buses.Add(new BusEntry { ColorId = "B", Capacity = 1 });
            c0.Buses.Add(new BusEntry { ColorId = "R", Capacity = 1 });
            var c1 = new BusColumn();
            c1.Buses.Add(new BusEntry { ColorId = "R", Capacity = 1 });
            c1.Buses.Add(new BusEntry { ColorId = "B", Capacity = 1 });
            q.Columns.Add(c0); q.Columns.Add(c1);

            var capped = BusBuddiesConstructiveArranger.CapColorRuns(q, g, activeSlots: 5, maxRun: 3);

            Assert.AreEqual(3, capped.Columns[0].Buses.Count);
            Assert.AreEqual(2, capped.Columns[1].Buses.Count);
            AssertTotalsPreserved(q, capped);
            Assert.LessOrEqual(LongestRun(capped), 3);
        }

        // End-to-end on a solid single-color block arranged round-robin: every column
        // comes out as one long same-color run, and CapColorRuns must tame all of them
        // while keeping the board solvable and balanced.
        [Test]
        public void CapColorRuns_OnArrangedSolidBlock_TamesAllRuns()
        {
            // 6x6 solid "B" block inside an 8x8 padded grid.
            var g = new GridData<ICellData>(8, 8);
            for (int i = 0; i < g.Cells.Length; i++) g.Cells[i] = new BBEmptyCell();
            for (int y = 1; y <= 6; y++)
            for (int x = 1; x <= 6; x++)
                g.Set(x, y, new BBPixelCell { ColorId = "B" });

            var buses = new List<BusEntry>();
            for (int k = 0; k < 12; k++) buses.Add(new BusEntry { ColorId = "B", Capacity = 3 }); // 12*3 = 36
            var main = new HashSet<string>(StringComparer.Ordinal) { "B" };

            var arr = BusBuddiesConstructiveArranger.Arrange(
                g, buses, columns: 3, activeSlots: 5, difficulty: 1, mainColors: main, rng: new System.Random(1));
            Assert.IsTrue(arr.Solvable, "precondition: solid block arranges solvable");
            Assert.Greater(LongestRun(arr.Queue), 3, "precondition: round-robin over one color makes long runs");

            var capped = BusBuddiesConstructiveArranger.CapColorRuns(arr.Queue, g, activeSlots: 5, maxRun: 3);

            Assert.LessOrEqual(LongestRun(capped), 3, "all runs tamed to <= 3");
            Assert.IsTrue(Solvable(g, capped, 5), "capped queue must stay solvable");
            AssertTotalsPreserved(arr.Queue, capped);
        }
    }
}
