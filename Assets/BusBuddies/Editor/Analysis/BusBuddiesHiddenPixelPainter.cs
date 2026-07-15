using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Flag-paint tool for Hidden Pixels: toggles BBPixelCell.Hidden. No-op on empty
    // or non-pixel cells (an empty cell has nothing to conceal).
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Hidden Pixel Painter")]
    public sealed class BusBuddiesHiddenPixelPainter : CellFlagPainterAsset
    {
        public override string ToolLabel => "✦ Hide";
        public override string ToolTooltip => "Paint hidden pixels — click/drag colored cells to conceal them";

        public override bool IsFlagged(ICellData cell) => cell is BBPixelCell p && p.Hidden;

        public override void SetFlag(LevelEditorSession session, CellRef cell, bool value)
        {
            if (session?.Document?.Grid == null) return;
            if (session.Document.Grid.Get(cell.X, cell.Y) is BBPixelCell p)
                p.Hidden = value;
        }
    }
}
