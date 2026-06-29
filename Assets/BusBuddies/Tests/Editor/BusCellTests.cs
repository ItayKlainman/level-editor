using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.BusBuddies;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusCellTests
    {
        [Test]
        public void PixelCell_HasTypeIdAndColor()
        {
            var c = new BBPixelCell { ColorId = "red" };
            Assert.AreEqual("bb.pixel", c.CellTypeId);
            Assert.AreEqual("red", c.ColorId);
            Assert.IsInstanceOf<IColoredCell>(c);
            Assert.IsInstanceOf<ICellData>(c);
        }

        [Test]
        public void EmptyCell_HasTypeId()
        {
            var e = new BBEmptyCell();
            Assert.AreEqual("bb.empty", e.CellTypeId);
            Assert.IsInstanceOf<ICellData>(e);
        }
    }
}
