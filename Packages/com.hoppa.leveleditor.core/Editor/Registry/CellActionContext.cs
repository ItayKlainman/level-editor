using Hoppa.LevelEditor.Core;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Context handed to ICellContextActions when a cell's right-click popup is built.
    // Carries the clicked cell plus its grid position and the live session, so an action
    // can inspect neighbours (e.g. to offer "connect to the box above") and apply
    // multi-cell mutations rather than only replacing the clicked cell.
    public readonly struct CellActionContext
    {
        public readonly ICellData          Cell;
        public readonly CellTypeRegistry   Registry;
        public readonly LevelEditorSession Session;
        public readonly CellRef            CellRef;

        public CellActionContext(ICellData cell, CellTypeRegistry registry,
            LevelEditorSession session, CellRef cellRef)
        {
            Cell     = cell;
            Registry = registry;
            Session  = session;
            CellRef  = cellRef;
        }
    }
}
