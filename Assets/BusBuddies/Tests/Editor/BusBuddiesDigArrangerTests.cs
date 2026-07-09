using System.Collections.Generic;
using NUnit.Framework;
using Hoppa.BusBuddies;
using Hoppa.BusBuddies.Editor;

namespace Hoppa.BusBuddies.Editor.Tests
{
    // Task 6: the "dig" axis. Higher Difficulty buries main colors deeper; a main
    // bus of each color stays near the head at Diff5; Diff1 is round-robin; an
    // over-buried unsolvable arrangement relaxes to Solvable.
    public sealed class BusBuddiesDigArrangerTests
    {
        private static List<BusEntry> Buses(params (string id, int count)[] spec)
        {
            var list = new List<BusEntry>();
            foreach (var (id, count) in spec)
                for (int i = 0; i < count; i++)
                    list.Add(new BusEntry { ColorId = id, Capacity = 5 });
            return list;
        }

        // Mean fraction (0..1) of the buried index of main buses in a single-column
        // queue (index 0 = head, N-1 = most buried).
        private static float MeanMainFraction(BusQueueData q, ISet<string> main)
        {
            var col = q.Columns[0].Buses;
            int n = col.Count;
            if (n <= 1) return 0f;
            float sum = 0f; int m = 0;
            for (int i = 0; i < n; i++)
                if (main.Contains(col[i].ColorId)) { sum += i / (float)(n - 1); m++; }
            return m == 0 ? 0f : sum / m;
        }

        private static HashSet<string> Main(params string[] ids) => new HashSet<string>(ids);

        [Test]
        public void HigherDifficulty_BuriesMainColorsDeeper()
        {
            var buses = Buses(("M1", 3), ("M2", 3), ("B", 6));
            var main = Main("M1", "M2");

            var easy = BusBuddiesDigArranger.BuildArrangement(buses, main, 1, 1, null);
            var hard = BusBuddiesDigArranger.BuildArrangement(buses, main, 1, 5, null);

            Assert.Greater(MeanMainFraction(hard, main), MeanMainFraction(easy, main),
                "Diff5 must bury main colors deeper than Diff1");
        }

        [Test]
        public void Difficulty5_KeepsOneMainOfEachColorNearHead()
        {
            var buses = Buses(("M1", 4), ("M2", 4), ("B", 8));
            var main = Main("M1", "M2");
            var q = BusBuddiesDigArranger.BuildArrangement(buses, main, 1, 5, null);
            var col = q.Columns[0].Buses;
            int half = col.Count / 2;

            foreach (var id in main)
            {
                bool nearHead = false;
                for (int i = 0; i < half; i++) if (col[i].ColorId == id) { nearHead = true; break; }
                Assert.IsTrue(nearHead, $"main color {id} must have a bus in the head half at Diff5");
            }
        }

        [Test]
        public void Difficulty1_IsRoundRobin()
        {
            var buses = Buses(("A", 3), ("B", 3));
            var q = BusBuddiesDigArranger.BuildArrangement(buses, Main(), 1, 1, null);
            var col = q.Columns[0].Buses;
            // Sorted-color round-robin → A,B,A,B,A,B.
            string[] expected = { "A", "B", "A", "B", "A", "B" };
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], col[i].ColorId, $"position {i}");
        }

        [Test]
        public void OverBuried_Unsolvable_RelaxesToSolvable()
        {
            var buses = Buses(("M1", 3), ("M2", 3), ("B", 6));
            var main = Main("M1", "M2");

            // Fake validator: "solvable" only when main colors are not buried too deep.
            bool Solvable(BusQueueData q) => MeanMainFraction(q, main) <= 0.55f;

            var atDiff5 = BusBuddiesDigArranger.BuildArrangement(buses, main, 1, 5, null);
            Assert.IsFalse(Solvable(atDiff5), "Diff5 baseline should be over-buried (unsolvable)");

            var relaxed = BusBuddiesDigArranger.Arrange(buses, main, 1, 5, null, Solvable);
            Assert.IsTrue(Solvable(relaxed), "relaxation must reach a solvable arrangement");
        }

        [Test]
        public void Arrange_NoValidator_ReturnsRequestedDifficulty()
        {
            var buses = Buses(("M1", 2), ("B", 4));
            var main = Main("M1");
            var a = BusBuddiesDigArranger.Arrange(buses, main, 2, 4, null, null);
            int total = 0; foreach (var c in a.Columns) total += c.Buses.Count;
            Assert.AreEqual(6, total);
            Assert.AreEqual(2, a.Columns.Count);
        }
    }
}
