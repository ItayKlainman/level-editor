using NUnit.Framework;
using Hoppa.BusBuddies.Sim;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusAccessibilityTests
    {
        [Test]
        public void LockedDiagonal_OrthogonallyEnclosedBlockIsNotAccessible()
        {
            // 3x3: center (1,1) block A, its 4 orthogonal neighbours block A,
            // the 4 corners (diagonals) empty. Center must be LOCKED.
            //   y2: [-1, A, -1]
            //   y1: [ A, A,  A]
            //   y0: [-1, A, -1]
            var grid = new[] { -1, 0, -1,  0, 0, 0,  -1, 0, -1 };
            var m = BusLevelModel.FromArrays(grid, 3, 3,
                busColors: new int[0][], busCaps: new int[0][],
                activeSlots: 5, numColors: 1, colorNames: new[] { "A" });
            var s = new BusSimState(m);

            Assert.IsFalse(s.IsAccessible(1, 1), "center enclosed by 4 orthogonal blocks must be locked");
            Assert.IsTrue(s.IsAccessible(1, 0), "edge arm block is accessible (touches outside border)");
            Assert.IsTrue(s.IsAccessible(0, 1), "edge arm block is accessible");
        }

        [Test]
        public void InteriorPocket_DonutLiningIsNotAccessible()
        {
            // 5x5 all blocks except the centre (2,2) empty -> an isolated pocket.
            // The 4 interior blocks lining the pocket have no border-connected empty
            // neighbour and are not on the edge -> NOT accessible. Edge blocks are.
            var grid = new int[25];
            for (int i = 0; i < 25; i++) grid[i] = 0;
            grid[2 * 5 + 2] = -1; // (2,2) empty
            var m = BusLevelModel.FromArrays(grid, 5, 5,
                busColors: new int[0][], busCaps: new int[0][],
                activeSlots: 5, numColors: 1, colorNames: new[] { "A" });
            var s = new BusSimState(m);

            Assert.IsFalse(s.IsAccessible(2, 1), "pocket-lining interior block must be locked");
            Assert.IsFalse(s.IsAccessible(1, 2), "pocket-lining interior block must be locked");
            Assert.IsTrue(s.IsAccessible(0, 0), "corner edge block is accessible");
        }

        [Test]
        public void SimpleStrip_AllEdgeBlocksAccessible()
        {
            // 2x1: [A, B] both on the edge -> both accessible.
            var m = BusLevelModel.FromArrays(new[] { 0, 1 }, 2, 1,
                busColors: new int[0][], busCaps: new int[0][],
                activeSlots: 5, numColors: 2, colorNames: new[] { "A", "B" });
            var s = new BusSimState(m);

            Assert.IsTrue(s.IsAccessible(0, 0));
            Assert.IsTrue(s.IsAccessible(1, 0));
        }
    }
}
