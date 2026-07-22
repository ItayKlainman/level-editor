using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Serializable base so a GameProfile can reference a region-drag tool as a project
    // asset, mirroring CellFlagPainterAsset / CanvasOverlayAsset.
    public abstract class RegionToolAsset : ScriptableObject, IRegionTool
    {
        public abstract string ToolLabel { get; }
        public abstract string ToolTooltip { get; }
        public abstract void OnRegionSelected(int minX, int minY, int width, int height, LevelEditorSession session);
    }
}
