namespace Hoppa.LevelEditor.Core.Editor
{
    // A profile-supplied "region drag" tool: when GridEditTool.Region is active, the
    // grid canvas lets the user press-drag-release to sweep out a rectangle of cells,
    // and on mouse-up hands the resulting CELL rectangle to this tool. Layer 1 stays
    // generic — it knows "there is a region tool" and computes the rectangle, not what
    // the region means (e.g. Bus Buddies places a plate cover over it).
    //
    // The rectangle is given as its MIN corner (minX, minY) + size (width, height), in
    // grid coordinates (bottom-left origin, y=0 = bottom row), already clamped in-bounds
    // with width/height >= 1.
    public interface IRegionTool
    {
        // Tool button caption + tooltip shown in the TOOLS palette.
        string ToolLabel { get; }
        string ToolTooltip { get; }

        // Called once on mouse-up with the swept cell rectangle. Implementations mutate
        // through the session (PushUndoSnapshot / MarkDirty handled by the implementation).
        void OnRegionSelected(int minX, int minY, int width, int height, LevelEditorSession session);
    }
}
