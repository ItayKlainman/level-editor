using System.Collections.Generic;
using NUnit.Framework;

namespace Hoppa.YarnTwist.Editor.Tests
{
    public class YarnLevelOrderPanelTests
    {
        [Test]
        public void MoveEntry_TopToMiddle_LandsAtTargetIndex()
        {
            var list = new List<string> { "A", "B", "C", "D", "E" };
            bool moved = YarnLevelOrderPanel.MoveEntry(list, 0, 3);
            Assert.IsTrue(moved);
            CollectionAssert.AreEqual(new[] { "B", "C", "D", "A", "E" }, list);
        }

        [Test]
        public void MoveEntry_MiddleToTop()
        {
            var list = new List<string> { "A", "B", "C", "D" };
            Assert.IsTrue(YarnLevelOrderPanel.MoveEntry(list, 2, 0));
            CollectionAssert.AreEqual(new[] { "C", "A", "B", "D" }, list);
        }

        [Test]
        public void MoveEntry_ToBottom()
        {
            var list = new List<string> { "A", "B", "C", "D" };
            Assert.IsTrue(YarnLevelOrderPanel.MoveEntry(list, 0, 3));
            CollectionAssert.AreEqual(new[] { "B", "C", "D", "A" }, list);
        }

        [Test]
        public void MoveEntry_NoOp_ReturnsFalse_LeavesListUnchanged()
        {
            var list = new List<string> { "A", "B", "C" };
            Assert.IsFalse(YarnLevelOrderPanel.MoveEntry(list, 1, 1));
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, list);
        }

        [Test]
        public void MoveEntry_OutOfRange_ReturnsFalse_LeavesListUnchanged()
        {
            var list = new List<string> { "A", "B", "C" };
            Assert.IsFalse(YarnLevelOrderPanel.MoveEntry(list, 0, 9));
            Assert.IsFalse(YarnLevelOrderPanel.MoveEntry(list, -1, 1));
            Assert.IsFalse(YarnLevelOrderPanel.MoveEntry(list, 5, 1));
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, list);
        }
    }
}
