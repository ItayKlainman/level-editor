using System.Collections.Generic;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Cell definition implements this to expose right-click popup actions.
    // Each action either converts the clicked cell to a new cell (CellContextAction.Create)
    // or applies a free-form session mutation that may touch several cells
    // (CellContextAction.Apply). The CellActionContext gives the action the clicked cell's
    // grid position and the session, so it can inspect neighbours and decide which actions
    // to offer (e.g. only directions where a valid neighbour exists).
    public interface ICellContextActions
    {
        IEnumerable<CellContextAction> GetContextActions(CellActionContext context);
    }
}
