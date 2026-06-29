using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.BusBuddies;
using Hoppa.BusBuddies.Sim;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusLevelModelTests
    {
        [Test]
        public void FromArrays_CountsBlocksBusesPassengers()
        {
            // grid 3x1: [A, empty, B]  -> 2 blocks
            var m = BusLevelModel.FromArrays(
                grid: new[] { 0, -1, 1 }, w: 3, h: 1,
                busColors: new[] { new[] { 0 }, new[] { 1 } },
                busCaps:   new[] { new[] { 2 }, new[] { 1 } },
                activeSlots: 5, numColors: 2, colorNames: new[] { "A", "B" });

            Assert.AreEqual(3, m.W);
            Assert.AreEqual(1, m.H);
            Assert.AreEqual(5, m.ActiveSlots);
            Assert.AreEqual(2, m.TotalBlocks);
            Assert.AreEqual(2, m.Columns);
            Assert.AreEqual(2, m.TotalBuses);
            Assert.AreEqual(3, m.TotalPassengers);
            Assert.AreEqual(-1, m.BusConnected[0][0]);
        }

        [Test]
        public void Build_FromGrid_InternsColorsAndCounts()
        {
            // 2x2: y0=[A,B], y1=[B,A]  -> 2 A, 2 B
            var grid = new GridData<ICellData>(2, 2);
            grid.Set(0, 0, new BBPixelCell { ColorId = "A" });
            grid.Set(1, 0, new BBPixelCell { ColorId = "B" });
            grid.Set(0, 1, new BBPixelCell { ColorId = "B" });
            grid.Set(1, 1, new BBPixelCell { ColorId = "A" });

            var q = new BusQueueData();
            var c0 = new BusColumn(); c0.Buses.Add(new BusEntry { ColorId = "A", Capacity = 2 });
            var c1 = new BusColumn(); c1.Buses.Add(new BusEntry { ColorId = "B", Capacity = 2 });
            q.Columns.Add(c0); q.Columns.Add(c1);

            var m = BusLevelModel.Build(grid, q, activeSlots: 3);

            Assert.AreEqual(4, m.TotalBlocks);
            Assert.AreEqual(2, m.NumColors);
            Assert.AreEqual(3, m.ActiveSlots);
            // index (1,1) = 1*2+1 = 3 must be a real colour index (>=0)
            Assert.GreaterOrEqual(m.Grid[3], 0);
            Assert.IsTrue(m.IsColorBalanced());
        }

        [Test]
        public void IsColorBalanced_RejectsMismatch()
        {
            // 3 A blocks but only 2 capacity of A -> unbalanced
            var m = BusLevelModel.FromArrays(
                grid: new[] { 0, 0, 0 }, w: 3, h: 1,
                busColors: new[] { new[] { 0 } },
                busCaps:   new[] { new[] { 2 } },
                activeSlots: 1, numColors: 1, colorNames: new[] { "A" });

            Assert.IsFalse(m.IsColorBalanced());
        }
    }
}
