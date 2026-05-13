using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public abstract class LevelExporterAsset : ScriptableObject, ILevelExporter
    {
        public abstract string Name { get; }
        public abstract bool Export(LevelDocument document, CellTypeRegistry cellTypes, string jsonFilePath);

        // Read-only info rows displayed in the Summary panel (e.g. Order, Layout).
        public virtual IEnumerable<(string label, string value)> GetSummaryExtras(LevelEditorSession session)
            => System.Array.Empty<(string, string)>();

        // Number of editable rows this exporter draws below the info rows in the Summary panel.
        public virtual int ExtraSummaryRowCount => 0;

        // Draw editable fields into the rect reserved by ExtraSummaryRowCount.
        public virtual void DrawExtraSummaryRows(Rect rect, LevelEditorSession session) { }
    }
}
