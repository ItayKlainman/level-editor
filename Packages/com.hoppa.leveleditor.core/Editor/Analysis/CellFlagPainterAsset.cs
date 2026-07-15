using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Serializable base so a GameProfile can reference a flag painter as a project asset,
    // mirroring LevelCompleterAsset / LevelAnalyzerAsset.
    public abstract class CellFlagPainterAsset : ScriptableObject, ICellFlagPainter
    {
        public abstract string ToolLabel { get; }
        public abstract string ToolTooltip { get; }
        public abstract bool IsFlagged(ICellData cell);
        public abstract void SetFlag(LevelEditorSession session, CellRef cell, bool value);
    }
}
