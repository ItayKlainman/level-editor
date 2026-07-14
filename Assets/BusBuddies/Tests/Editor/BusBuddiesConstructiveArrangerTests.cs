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
    }
}
