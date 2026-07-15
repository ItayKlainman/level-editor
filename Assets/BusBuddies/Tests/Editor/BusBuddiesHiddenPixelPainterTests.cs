using Hoppa.BusBuddies.Editor;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusBuddiesHiddenPixelPainterTests
    {
        private static LevelEditorSession MakeSession(GridData<ICellData> grid)
        {
            var doc = new LevelDocument { Grid = grid };
            return new LevelEditorSession(ScriptableObject.CreateInstance<GameProfile>(), doc);
        }

        [Test]
        public void SetFlag_HidesAndUnhides_PixelCell()
        {
            var grid = new GridData<ICellData>(2, 1);
            grid.Set(0, 0, new BBPixelCell { ColorId = "red" });
            var session = MakeSession(grid);
            var painter = ScriptableObject.CreateInstance<BusBuddiesHiddenPixelPainter>();

            painter.SetFlag(session, new CellRef(0, 0), true);
            Assert.IsTrue(((BBPixelCell)grid.Get(0, 0)).Hidden);
            Assert.IsTrue(painter.IsFlagged(grid.Get(0, 0)));

            painter.SetFlag(session, new CellRef(0, 0), false);
            Assert.IsFalse(((BBPixelCell)grid.Get(0, 0)).Hidden);
        }

        [Test]
        public void SetFlag_NoOp_OnEmptyCell()
        {
            var grid = new GridData<ICellData>(1, 1);
            grid.Set(0, 0, new BBEmptyCell());
            var session = MakeSession(grid);
            var painter = ScriptableObject.CreateInstance<BusBuddiesHiddenPixelPainter>();

            Assert.DoesNotThrow(() => painter.SetFlag(session, new CellRef(0, 0), true));
            Assert.IsFalse(painter.IsFlagged(grid.Get(0, 0)));
        }
    }
}
