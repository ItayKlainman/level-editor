using System.Collections.Generic;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Cell definition implements this to expose "Convert to…" actions in the right-click popup.
    // Each action may include a setup section (e.g. direction picker) drawn above its button.
    public interface ICellContextActions
    {
        IEnumerable<CellContextAction> GetContextActions(ICellData cell, CellTypeRegistry registry);
    }
}
