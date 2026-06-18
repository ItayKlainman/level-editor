using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Hoppa.YarnTwist.Editor;

namespace Hoppa.YarnTwist.Editor.Tests
{
    // Covers the pure data⇄visual row mapping used by YarnTopSectionPanel when the
    // Layer-1 SpoolsBelowGrid flag flips the spool panel below the grid. The mapping
    // is VISUAL ONLY: a no-op render must never mutate the serialized Spools order.
    public class YarnTopSectionReverseTests
    {
        [Test]
        public void DataToVisual_Default_PutsDataIndex0AtBottom()
        {
            // reverse = false (classic layout): visual = count-1-data
            const int count = 4;
            Assert.AreEqual(3, YarnTopSectionPanel.DataToVisual(0, count, false)); // data 0 → bottom row (visual 3)
            Assert.AreEqual(0, YarnTopSectionPanel.DataToVisual(3, count, false)); // data 3 → top row (visual 0)
        }

        [Test]
        public void DataToVisual_Reversed_PutsDataIndex0AtTop()
        {
            // reverse = true (spools-below-grid layout): visual = data
            const int count = 4;
            Assert.AreEqual(0, YarnTopSectionPanel.DataToVisual(0, count, true)); // data 0 → top row (visual 0)
            Assert.AreEqual(3, YarnTopSectionPanel.DataToVisual(3, count, true)); // data 3 → bottom row (visual 3)
        }

        [Test]
        public void DataToVisual_VisualToData_RoundTrip_BothModes()
        {
            foreach (int count in new[] { 1, 2, 3, 5, 8 })
            foreach (bool reverse in new[] { false, true })
            {
                for (int s = 0; s < count; s++)
                {
                    int v = YarnTopSectionPanel.DataToVisual(s, count, reverse);
                    Assert.GreaterOrEqual(v, 0, $"visual idx in range (count={count}, reverse={reverse})");
                    Assert.Less(v, count, $"visual idx in range (count={count}, reverse={reverse})");
                    int back = YarnTopSectionPanel.VisualToData(v, count, reverse);
                    Assert.AreEqual(s, back, $"round-trip data→visual→data (count={count}, reverse={reverse}, s={s})");
                }

                // Mapping is a bijection: visual indices cover 0..count-1 exactly once.
                var visuals = Enumerable.Range(0, count)
                    .Select(s => YarnTopSectionPanel.DataToVisual(s, count, reverse))
                    .OrderBy(x => x)
                    .ToArray();
                CollectionAssert.AreEqual(Enumerable.Range(0, count).ToArray(), visuals,
                    $"visual indices are a permutation (count={count}, reverse={reverse})");
            }
        }

        [Test]
        public void MoveVisual_Reversed_MovingBottomToTop_PutsItAtDataIndex0()
        {
            // Data list: index 0..3. In reverse layout data index 0 is the TOP visual row.
            // Move the BOTTOM visual row (visual 3) to the TOP (visual 0): the moved item
            // must become data index 0.
            var spools = MakeSpools("A", "B", "C", "D"); // data: A=0 B=1 C=2 D=3
            // reverse=true → visual: A(0) top, B(1), C(2), D(3) bottom.
            YarnTopSectionPanel.MoveVisual(spools, vSrc: 3, vTgt: 0, reverse: true);
            // D moved to the top visual row ⇒ data index 0 is D.
            Assert.AreEqual("D", spools[0].ColorId);
            CollectionAssert.AreEqual(new[] { "D", "A", "B", "C" }, spools.Select(s => s.ColorId).ToArray());
        }

        [Test]
        public void MoveVisual_Default_MovingTopToBottom_Matches_ClassicMapping()
        {
            // Classic layout (reverse=false): data 0 at bottom, so visual 0 = data count-1.
            var spools = MakeSpools("A", "B", "C", "D"); // data: A=0 B=1 C=2 D=3
            // visual: D(0) top, C(1), B(2), A(3) bottom. Move top visual (D) below A (vTgt=count=4).
            YarnTopSectionPanel.MoveVisual(spools, vSrc: 0, vTgt: 4, reverse: false);
            // Visual order becomes C, B, A, D (D now bottom). Convert back to data (bottom=index0):
            // data: D(0), A(1), B(2), C(3).
            CollectionAssert.AreEqual(new[] { "D", "A", "B", "C" }, spools.Select(s => s.ColorId).ToArray());
        }

        [Test]
        public void MoveVisual_NoOp_DoesNotReorderData()
        {
            // vTgt == vSrc and vTgt == vSrc+1 are treated as no-ops by the caller; the core
            // itself, asked to "move" to the same slot, must leave the data list intact.
            foreach (bool reverse in new[] { false, true })
            {
                var spools = MakeSpools("A", "B", "C");
                YarnTopSectionPanel.MoveVisual(spools, vSrc: 1, vTgt: 1, reverse: reverse);
                CollectionAssert.AreEqual(new[] { "A", "B", "C" }, spools.Select(s => s.ColorId).ToArray(),
                    $"no-op move must not reorder data (reverse={reverse})");
            }
        }

        private static List<YarnSpoolData> MakeSpools(params string[] ids) =>
            ids.Select(id => new YarnSpoolData { ColorId = id }).ToList();
    }
}
