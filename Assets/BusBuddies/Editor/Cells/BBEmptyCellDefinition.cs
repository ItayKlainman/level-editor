using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // "No block" cell. Subtle checker so designers see which cells are unpainted.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Cells/Empty")]
    public sealed class BBEmptyCellDefinition : CellTypeDefinition
    {
        private static readonly Color DarkA = new Color(0.16f, 0.17f, 0.20f);
        private static readonly Color DarkB = new Color(0.13f, 0.14f, 0.17f);

        public override ICellData CreateDefault() => new BBEmptyCell();

        public override void DrawCell(Rect rect, ICellData data)
        {
            EditorGUI.DrawRect(rect, DarkA);
            float halfW = rect.width  * 0.5f;
            float halfH = rect.height * 0.5f;
            EditorGUI.DrawRect(new Rect(rect.x,         rect.y,         halfW, halfH), DarkB);
            EditorGUI.DrawRect(new Rect(rect.x + halfW, rect.y + halfH, halfW, halfH), DarkB);
        }

        public override void DrawInspector(Rect rect, ref ICellData data) { }
    }
}
