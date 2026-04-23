using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Demo.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Demo/Empty Cell Definition")]
    public sealed class DemoEmptyCellDefinition : CellTypeDefinition
    {
        public override ICellData CreateDefault() => new DemoEmptyCell();

        public override void DrawCell(Rect rect, ICellData data)
        {
            // Draw a subtle crosshair to signal "empty slot"
            float cx = rect.x + rect.width  * 0.5f;
            float cy = rect.y + rect.height * 0.5f;
            var dot = new Color(0.36f, 0.38f, 0.43f, 0.75f);
            EditorGUI.DrawRect(new Rect(cx - 5f, cy - 0.5f, 10f, 1f), dot);
            EditorGUI.DrawRect(new Rect(cx - 0.5f, cy - 5f, 1f, 10f), dot);
        }

        public override void DrawInspector(Rect rect, ref ICellData data)
        {
            GUI.Label(rect, "(empty)", EditorStyles.centeredGreyMiniLabel);
        }
    }
}
