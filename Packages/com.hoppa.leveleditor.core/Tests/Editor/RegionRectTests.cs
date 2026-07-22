using NUnit.Framework;

namespace Hoppa.LevelEditor.Core.Editor.Tests
{
    // Pure geometry for the generic Layer-1 region-drag tool: two corner cells → an
    // inclusive (minX, minY, width, height) rectangle, direction-independent.
    public sealed class RegionRectTests
    {
        [Test]
        public void SingleCell_IsOneByOne()
        {
            var (x, y, w, h) = GridCanvasPanel.RegionRect(new CellRef(3, 4), new CellRef(3, 4));
            Assert.AreEqual((3, 4, 1, 1), (x, y, w, h));
        }

        [Test]
        public void BottomLeftToTopRight()
        {
            var (x, y, w, h) = GridCanvasPanel.RegionRect(new CellRef(2, 1), new CellRef(5, 6));
            Assert.AreEqual((2, 1, 4, 6), (x, y, w, h));
        }

        [Test]
        public void DirectionIndependent_TopRightToBottomLeft_SameRect()
        {
            var a = GridCanvasPanel.RegionRect(new CellRef(2, 1), new CellRef(5, 6));
            var b = GridCanvasPanel.RegionRect(new CellRef(5, 6), new CellRef(2, 1));
            Assert.AreEqual(a, b);
        }

        [Test]
        public void MixedDiagonal_UsesMinCornerAndSpan()
        {
            var (x, y, w, h) = GridCanvasPanel.RegionRect(new CellRef(5, 1), new CellRef(2, 4));
            Assert.AreEqual((2, 1, 4, 4), (x, y, w, h));
        }
    }
}
