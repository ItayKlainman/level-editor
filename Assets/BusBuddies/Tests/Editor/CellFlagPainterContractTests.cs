using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class CellFlagPainterContractTests
    {
        // Minimal concrete painter over a fake cell, proving the base type + contract compile
        // and that SetFlag/IsFlagged operate through the interface.
        private sealed class FakeCell : ICellData
        {
            public string CellTypeId => "fake";
            public bool Flag;
        }

        private sealed class FakePainter : CellFlagPainterAsset
        {
            public override string ToolLabel => "Flag";
            public override string ToolTooltip => "Toggle a flag";
            public override bool IsFlagged(ICellData cell) => cell is FakeCell f && f.Flag;
            public override void SetFlag(LevelEditorSession session, CellRef cell, bool value)
            {
                if (session.Document.Grid.Get(cell.X, cell.Y) is FakeCell f) f.Flag = value;
            }
        }

        [Test]
        public void SetFlag_TogglesUnderlyingCell()
        {
            var grid = new GridData<ICellData>(2, 2);
            grid.Set(0, 0, new FakeCell());
            var doc = new LevelDocument { Grid = grid };
            var session = new LevelEditorSession(ScriptableObject.CreateInstance<GameProfile>(), doc);

            var painter = ScriptableObject.CreateInstance<FakePainter>();
            Assert.IsFalse(painter.IsFlagged(grid.Get(0, 0)));
            painter.SetFlag(session, new CellRef(0, 0), true);
            Assert.IsTrue(painter.IsFlagged(grid.Get(0, 0)));

            Assert.AreEqual(GridEditTool.Hide, System.Enum.Parse(typeof(GridEditTool), "Hide"));
        }
    }
}
