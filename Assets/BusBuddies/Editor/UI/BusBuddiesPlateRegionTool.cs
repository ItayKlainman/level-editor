using Hoppa.LevelEditor.Core.Editor;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Layer-2 region-drag tool for Bus Buddies Plates. When the designer drags a
    // rectangle on the grid with the Plate tool active, Layer 1 hands the swept CELL
    // rectangle here and we add a plate (default amount) via the storage helper — as
    // long as it fits in-bounds and doesn't overlap an existing plate (mirrors the
    // no-overlap validation policy so a drag can't create an invalid plate).
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Plate Region Tool")]
    public sealed class BusBuddiesPlateRegionTool : RegionToolAsset
    {
        public override string ToolLabel => "Plate";
        public override string ToolTooltip => "Drag a rectangle to add a Plate cover over the pixels";

        public override void OnRegionSelected(int minX, int minY, int width, int height, LevelEditorSession session)
        {
            var doc = session?.Document;
            var grid = doc?.Grid;
            if (doc == null || grid == null) return;

            // A zero-drag (single-cell 1x1) is almost always a stray click, not an
            // intent to cover a single pixel — ignore it so clicking with the Plate
            // tool active doesn't scatter tiny plates. A deliberate 1x1 plate can
            // still be authored via the numeric "Plates" list in the More options panel.
            if (width <= 1 && height <= 1) return;

            if (!BusBuddiesPlateConfigs.CanPlace(grid, minX, minY, width, height,
                    BusBuddiesPlateConfigs.All(doc)))
                return;

            session.PushUndoSnapshot();
            BusBuddiesPlateConfigs.Add(doc, minX, minY, width, height, BusBuddiesPlateConfigs.DefaultAmount);
            session.MarkDirty();
        }
    }
}
