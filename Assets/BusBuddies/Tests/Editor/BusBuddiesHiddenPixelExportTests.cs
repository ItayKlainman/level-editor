using System.Linq;
using Hoppa.BusBuddies.Editor;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusBuddiesHiddenPixelExportTests
    {
        [Test]
        public void BuildHiddenPixels_UsesXTimesWidthPlusY_ForHiddenCellsOnly()
        {
            // 3x2 grid. Hide the cell at (x=2, y=1) and (x=0, y=0).
            var grid = new GridData<ICellData>(3, 2);
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 3; x++)
                    grid.Set(x, y, new BBPixelCell { ColorId = "red" });
            ((BBPixelCell)grid.Get(2, 1)).Hidden = true;
            ((BBPixelCell)grid.Get(0, 0)).Hidden = true;

            var arr = BusBuddiesGameLevelExporter.BuildHiddenPixels(grid);
            var vals = arr.Select(t => (int)t).OrderBy(v => v).ToArray();

            // x*width+y : (2,1) -> 2*3+1=7 ; (0,0) -> 0
            CollectionAssert.AreEqual(new[] { 0, 7 }, vals);
        }

        [Test]
        public void BuildHiddenPixels_Empty_WhenNoneHidden()
        {
            var grid = new GridData<ICellData>(2, 2);
            for (int i = 0; i < 4; i++) grid.Cells[i] = new BBPixelCell { ColorId = "blue" };
            Assert.AreEqual(0, BusBuddiesGameLevelExporter.BuildHiddenPixels(grid).Count);
        }
    }
}
