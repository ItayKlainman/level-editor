namespace Hoppa.LevelEditor.Core.Editor
{
    // A profile-supplied "flag paint" tool: when GridEditTool.Hide is active, the grid
    // canvas delegates click/drag to this painter to toggle a boolean flag on a cell
    // (e.g. Bus Buddies' hidden-pixel flag) WITHOUT replacing the cell or its color.
    // Layer-1 stays generic — it knows "there is a flag tool", not what the flag means.
    public interface ICellFlagPainter
    {
        // Tool button caption + tooltip shown in the TOOLS palette.
        string ToolLabel { get; }
        string ToolTooltip { get; }

        // Current flag value on a cell (used to decide a stroke's paint-vs-erase direction).
        bool IsFlagged(ICellData cell);

        // Set the flag to `value` on the cell at `cell`. Implementations mutate through
        // the session (so undo/dirty are handled by the caller's snapshot).
        void SetFlag(LevelEditorSession session, CellRef cell, bool value);
    }
}
