using System.Linq;
using Hoppa.BusBuddies.Editor;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusBuddiesPlateConfigsTests
    {
        private static LevelDocument Doc() =>
            new LevelDocument { SchemaVersion = "busbuddies.v1", GameData = new JObject() };

        private static GridData<ICellData> Grid(int w, int h) => new GridData<ICellData>(w, h);

        [Test]
        public void Default_NoPlates()
        {
            var doc = Doc();
            Assert.IsEmpty(BusBuddiesPlateConfigs.All(doc));
            Assert.IsFalse(BusBuddiesPlateConfigs.PlateAt(doc, new CellRef(0, 0), out _));
        }

        [Test]
        public void Add_StoresRectAndAmount_RoundTrips()
        {
            var doc = Doc();
            BusBuddiesPlateConfigs.Add(doc, 5, 7, 10, 5, 80);

            var all = BusBuddiesPlateConfigs.All(doc);
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual(5, all[0].X);
            Assert.AreEqual(7, all[0].Y);
            Assert.AreEqual(10, all[0].W);
            Assert.AreEqual(5, all[0].H);
            Assert.AreEqual(80, all[0].Amount);

            // Stored shape matches the documented JArray of {x,y,w,h,amount}.
            var arr = (JArray)doc.GameData["plateConfigs"];
            Assert.AreEqual(5,  (int)arr[0]["x"]);
            Assert.AreEqual(7,  (int)arr[0]["y"]);
            Assert.AreEqual(10, (int)arr[0]["w"]);
            Assert.AreEqual(5,  (int)arr[0]["h"]);
            Assert.AreEqual(80, (int)arr[0]["amount"]);
        }

        [Test]
        public void Add_ClampsSizeAndAmountToAtLeastOne()
        {
            var doc = Doc();
            BusBuddiesPlateConfigs.Add(doc, 0, 0, 0, -3, 0);
            var p = BusBuddiesPlateConfigs.All(doc)[0];
            Assert.AreEqual(1, p.W);
            Assert.AreEqual(1, p.H);
            Assert.AreEqual(1, p.Amount);
        }

        [Test]
        public void Remove_ByIndex()
        {
            var doc = Doc();
            BusBuddiesPlateConfigs.Add(doc, 0, 0, 2, 2, 5);
            BusBuddiesPlateConfigs.Add(doc, 5, 5, 2, 2, 6);
            BusBuddiesPlateConfigs.Remove(doc, 0);

            var all = BusBuddiesPlateConfigs.All(doc);
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual(5, all[0].X);
        }

        [Test]
        public void SetRect_And_SetAmount_UpdateInPlace_WithClamp()
        {
            var doc = Doc();
            BusBuddiesPlateConfigs.Add(doc, 0, 0, 2, 2, 5);
            BusBuddiesPlateConfigs.SetRect(doc, 0, 3, 4, 6, 0);
            BusBuddiesPlateConfigs.SetAmount(doc, 0, 0);

            var p = BusBuddiesPlateConfigs.All(doc)[0];
            Assert.AreEqual(3, p.X);
            Assert.AreEqual(4, p.Y);
            Assert.AreEqual(6, p.W);
            Assert.AreEqual(1, p.H, "height clamps to >= 1");
            Assert.AreEqual(1, p.Amount, "amount clamps to >= 1");
        }

        [Test]
        public void PlateAt_FindsCoveringPlate()
        {
            var doc = Doc();
            BusBuddiesPlateConfigs.Add(doc, 2, 2, 3, 3, 5); // covers x 2..4, y 2..4

            Assert.IsTrue(BusBuddiesPlateConfigs.PlateAt(doc, new CellRef(4, 4), out var p));
            Assert.AreEqual(2, p.X);
            Assert.IsFalse(BusBuddiesPlateConfigs.PlateAt(doc, new CellRef(5, 4), out _), "just outside the rect");
            Assert.IsFalse(BusBuddiesPlateConfigs.PlateAt(doc, new CellRef(1, 2), out _), "left of the rect");
        }

        [Test]
        public void CoveredCells_EnumeratesFullRect()
        {
            var p = new BusPlateConfig { X = 1, Y = 1, W = 2, H = 3, Amount = 5 };
            var cells = BusBuddiesPlateConfigs.CoveredCells(p).ToList();
            Assert.AreEqual(6, cells.Count);
            CollectionAssert.Contains(cells, new CellRef(1, 1));
            CollectionAssert.Contains(cells, new CellRef(2, 3));
        }

        [Test]
        public void CanPlace_InBounds_True()
        {
            var grid = Grid(10, 10);
            Assert.IsTrue(BusBuddiesPlateConfigs.CanPlace(grid, 0, 0, 3, 3, null));
            Assert.IsTrue(BusBuddiesPlateConfigs.CanPlace(grid, 7, 7, 3, 3, null)); // touches the far edge
        }

        [Test]
        public void CanPlace_OutOfBounds_False()
        {
            var grid = Grid(10, 10);
            Assert.IsFalse(BusBuddiesPlateConfigs.CanPlace(grid, 8, 8, 3, 3, null), "extends past the top-right");
            Assert.IsFalse(BusBuddiesPlateConfigs.CanPlace(grid, -1, 0, 2, 2, null), "negative origin");
            Assert.IsFalse(BusBuddiesPlateConfigs.CanPlace(grid, 0, 0, 0, 2, null), "zero width");
        }

        [Test]
        public void CanPlace_Overlap_False()
        {
            var grid = Grid(10, 10);
            var existing = new[] { new BusPlateConfig { X = 2, Y = 2, W = 3, H = 3, Amount = 5 } };

            Assert.IsFalse(BusBuddiesPlateConfigs.CanPlace(grid, 4, 4, 3, 3, existing), "overlaps corner");
            Assert.IsTrue(BusBuddiesPlateConfigs.CanPlace(grid, 5, 2, 3, 3, existing), "sits flush to the right, no overlap");
            Assert.IsTrue(BusBuddiesPlateConfigs.CanPlace(grid, 2, 5, 3, 3, existing), "sits flush above, no overlap");
        }
    }
}
