using Hoppa.LevelEditor.Core;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public sealed class CellInspectorPanel : IEditorPanel
    {
        private const float HeaderH   = 26f;
        private const float TypeNameH = 18f;

        private static readonly Color HeaderBg  = new Color(0.17f, 0.21f, 0.33f);
        private static readonly Color Accent     = new Color(0.30f, 0.65f, 1.00f);
        private static readonly Color LabelColor = new Color(0.55f, 0.68f, 0.85f);

        public void OnGUI(Rect rect, LevelEditorSession session)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, HeaderH), HeaderBg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), Accent);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 5f, rect.width - 16f, HeaderH - 10f),
                "CELL INSPECTOR", EditorStyles.boldLabel);

            var body = new Rect(rect.x, rect.y + HeaderH, rect.width, rect.height - HeaderH);

            if (session == null || !session.SelectedCell.HasValue)
            {
                GUI.Label(body, "Select a cell", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var cellRef = session.SelectedCell.Value;
            var cell    = session.Document.Grid.Get(cellRef.X, cellRef.Y);
            if (cell == null)
            {
                GUI.Label(body, "Empty", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            if (!session.CellTypes.TryGetDefinition(cell.CellTypeId, out var definition))
            {
                GUI.Label(body, cell.CellTypeId, EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // Type name label
            var old = GUI.contentColor;
            GUI.contentColor = LabelColor;
            GUI.Label(new Rect(rect.x + 8f, body.y + 3f, rect.width - 16f, TypeNameH),
                definition.DisplayName, EditorStyles.miniLabel);
            GUI.contentColor = old;

            // Inspector content
            var inspectorRect = new Rect(
                rect.x + 4f,
                body.y + TypeNameH + 4f,
                rect.width - 8f,
                body.height - TypeNameH - 8f);

            EditorGUI.BeginChangeCheck();
            definition.DrawInspector(inspectorRect, ref cell);
            if (EditorGUI.EndChangeCheck())
            {
                session.Document.Grid.Set(cellRef.X, cellRef.Y, cell);
                session.MarkDirty();
                (definition as CellTypeDefinition)?.OnAfterInspectorChanged(cellRef.X, cellRef.Y, session);
                session.RunValidation();
            }
        }
    }
}
