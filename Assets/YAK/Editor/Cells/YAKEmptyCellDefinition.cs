using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YAK.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/YAK/Cells/Empty")]
    public sealed class YAKEmptyCellDefinition : CellTypeDefinition
    {
        private static readonly Color DarkA = new Color(0.16f, 0.17f, 0.20f);
        private static readonly Color DarkB = new Color(0.13f, 0.14f, 0.17f);

        public override ICellData CreateDefault() => new YAKEmptyCell();

        public override void DrawCell(Rect rect, ICellData data)
        {
            // Subtle checker pattern so designers can see which cells are unpainted.
            EditorGUI.DrawRect(rect, DarkA);
            float halfW = rect.width  * 0.5f;
            float halfH = rect.height * 0.5f;
            EditorGUI.DrawRect(new Rect(rect.x,         rect.y,         halfW, halfH), DarkB);
            EditorGUI.DrawRect(new Rect(rect.x + halfW, rect.y + halfH, halfW, halfH), DarkB);
        }

        public override void DrawInspector(Rect rect, ref ICellData data) { }
    }
}
