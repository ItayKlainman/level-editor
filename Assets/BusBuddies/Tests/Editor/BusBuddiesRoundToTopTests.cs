using System;
using System.Collections.Generic;
using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.BusBuddies;
using Hoppa.BusBuddies.Editor;
using Hoppa.BusBuddies.Sim;

namespace Hoppa.BusBuddies.Editor.Tests
{
    // Round-to-top bus ordering: with Round-to-5 on, round (multiple-of-5) buses are
    // moved toward each column's head and remainders to the tail — but only when the
    // move keeps the level solvable (guarded by exact replay).
    public sealed class BusBuddiesRoundToTopTests
    {
        // 7x7 empty margin, 5x5 object at [1..5]: "O" outer ring (16) enclosing a 3x3
        // "I" interior (9). Flood-accessibility means O must be peeled before any I is
        // reachable. (Local copy of the arranger-test helper.)
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

        private static List<int> Caps(BusColumn col)
        {
            var l = new List<int>();
            foreach (var b in col.Buses) l.Add(b.Capacity);
            return l;
        }

        // 1. Pure ordering primitive: round-first, stable within each partition.
        [Test]
        public void StableRoundFirst_PartitionsRoundThenRemainder_Stable()
        {
            List<BusEntry> Make(params int[] caps)
            {
                var l = new List<BusEntry>();
                foreach (var c in caps) l.Add(new BusEntry { ColorId = "x", Capacity = c });
                return l;
            }

            var sorted = BusBuddiesConstructiveArranger.StableRoundFirst(Make(7, 10, 5, 3, 20));
            CollectionAssert.AreEqual(new[] { 10, 5, 20, 7, 3 }, sorted.ConvertAll(b => b.Capacity));

            // all-remainder unchanged
            var allRem = BusBuddiesConstructiveArranger.StableRoundFirst(Make(7, 3));
            CollectionAssert.AreEqual(new[] { 7, 3 }, allRem.ConvertAll(b => b.Capacity));

            // all-round unchanged
            var allRound = BusBuddiesConstructiveArranger.StableRoundFirst(Make(10, 20));
            CollectionAssert.AreEqual(new[] { 10, 20 }, allRound.ConvertAll(b => b.Capacity));
        }

        // 1b. Within-color contract: round-first is applied per color, preserving the set of
        // positions each color occupies (so an inter-color interleave pattern is untouched).
        [Test]
        public void StableRoundFirst_PreservesColorPositions_OrdersRoundFirstWithinColor()
        {
            List<BusEntry> Make(params (string color, int cap)[] items)
            {
                var l = new List<BusEntry>();
                foreach (var it in items) l.Add(new BusEntry { ColorId = it.color, Capacity = it.cap });
                return l;
            }
            List<string> Colors(List<BusEntry> l) => l.ConvertAll(b => b.ColorId);
            List<int> Caps2(List<BusEntry> l) => l.ConvertAll(b => b.Capacity);

            // [O20, I5, O25, I3]: each color already round-first in its own slots -> unchanged,
            // interleave pattern intact.
            var a = BusBuddiesConstructiveArranger.StableRoundFirst(
                Make(("O", 20), ("I", 5), ("O", 25), ("I", 3)));
            CollectionAssert.AreEqual(new[] { "O", "I", "O", "I" }, Colors(a), "color positions preserved");
            CollectionAssert.AreEqual(new[] { 20, 5, 25, 3 }, Caps2(a), "already round-first within each color");

            // [O3, O20] (same color): round-first within O -> [O20, O3].
            var b = BusBuddiesConstructiveArranger.StableRoundFirst(Make(("O", 3), ("O", 20)));
            CollectionAssert.AreEqual(new[] { 20, 3 }, Caps2(b), "round bus surfaces within its color");

            // Mixed interleaved column: O slots (0,2) and I slots (1,3) each reorder within
            // themselves; O remainder O7 sinks below round O10, I stays. Positions preserved.
            var c = BusBuddiesConstructiveArranger.StableRoundFirst(
                Make(("O", 7), ("I", 5), ("O", 10), ("I", 4)));
            CollectionAssert.AreEqual(new[] { "O", "I", "O", "I" }, Colors(c), "inter-color pattern preserved");
            CollectionAssert.AreEqual(new[] { 10, 5, 7, 4 }, Caps2(c), "round-first within each color's own slots");
        }

        // 2. Invariant: sorting an arranged (solvable) queue never breaks solvability.
        [Test]
        public void SortRoundToHead_KeepsSolvable_OnArrangedQueue()
        {
            var g = RingGrid(out _, out _);
            var buses = new List<BusEntry>();
            for (int k = 0; k < 9; k++) buses.Add(new BusEntry { ColorId = "I", Capacity = 1 });
            buses.Add(new BusEntry { ColorId = "O", Capacity = 8 });
            buses.Add(new BusEntry { ColorId = "O", Capacity = 8 });
            var main = new HashSet<string>(StringComparer.Ordinal) { "I", "O" };

            var arr = BusBuddiesConstructiveArranger.Arrange(
                g, buses, columns: 3, activeSlots: 5, difficulty: 1, mainColors: main, rng: new System.Random(1));
            Assert.IsTrue(arr.Solvable, "precondition: arranged queue is solvable");

            var sorted = BusBuddiesConstructiveArranger.SortRoundToHead(arr.Queue, g, columns: 3, activeSlots: 5);
            Assert.IsTrue(
                BusBuddiesConstructiveArranger.IsSolvable(g, sorted, columns: 3, activeSlots: 5),
                "round-to-head sort must never break solvability");
        }

        // 3. Applied-and-kept: open single-color board where any order wins, so the
        // round bus (cap 5) is surfaced to the head and the remainder (cap 3) sinks.
        [Test]
        public void SortRoundToHead_PutsRoundBusAtHead_WhenSafe()
        {
            var g = new GridData<ICellData>(8, 1);
            for (int x = 0; x < 8; x++) g.Set(x, 0, new BBPixelCell { ColorId = "blue" });

            var q = new BusQueueData();
            var col = new BusColumn();
            col.Buses.Add(new BusEntry { ColorId = "blue", Capacity = 3 });
            col.Buses.Add(new BusEntry { ColorId = "blue", Capacity = 5 });
            q.Columns.Add(col);

            var sorted = BusBuddiesConstructiveArranger.SortRoundToHead(q, g, columns: 1, activeSlots: 5);

            Assert.AreEqual(5, sorted.Columns[0].Buses[0].Capacity, "round bus (cap 5) belongs at the head");
            Assert.AreEqual(3, sorted.Columns[0].Buses[1].Capacity, "remainder (cap 3) sinks to the tail");
            Assert.IsTrue(
                BusBuddiesConstructiveArranger.IsSolvable(g, sorted, columns: 1, activeSlots: 5),
                "result stays solvable");
        }

        // 4. Guard: when the round-first sort would surface the enclosed interior ahead
        // of the outer ring and deadlock, that column reverts to its original order.
        // Single column, 1 Active slot: the original [O8,O8,I5,I4] peels the ring then
        // the interior; the round-first sort [I5,O8,O8,I4] pulls I5 into the only slot
        // where it can never fill (interior unreachable) -> permanent deadlock -> revert.
        [Test]
        public void SortRoundToHead_RevertsColumn_WhenSortBreaksSolvability()
        {
            var g = RingGrid(out int o, out int i);
            Assert.AreEqual(16, o); Assert.AreEqual(9, i);

            var q = new BusQueueData();
            var col = new BusColumn();
            col.Buses.Add(new BusEntry { ColorId = "O", Capacity = 8 });
            col.Buses.Add(new BusEntry { ColorId = "O", Capacity = 8 });
            col.Buses.Add(new BusEntry { ColorId = "I", Capacity = 5 }); // round
            col.Buses.Add(new BusEntry { ColorId = "I", Capacity = 4 }); // remainder
            q.Columns.Add(col);

            // Precondition: this original order IS solvable with a single Active slot.
            Assert.IsTrue(
                BusBuddiesConstructiveArranger.IsSolvable(g, q, columns: 1, activeSlots: 1),
                "precondition: the O-first order is solvable");

            var sorted = BusBuddiesConstructiveArranger.SortRoundToHead(q, g, columns: 1, activeSlots: 1);

            // The round-first sort would deadlock, so the column must have reverted:
            // head stays "O", interior not surfaced, and it stays solvable.
            Assert.AreEqual("O", sorted.Columns[0].Buses[0].ColorId, "column reverts: outer ring stays at head");
            CollectionAssert.AreEqual(new[] { 8, 8, 5, 4 }, Caps(sorted.Columns[0]), "original order preserved");
            Assert.IsTrue(
                BusBuddiesConstructiveArranger.IsSolvable(g, sorted, columns: 1, activeSlots: 1),
                "reverted result stays solvable");
        }
    }
}
