using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Cells/Empty")]
    public sealed class YarnEmptyCellDefinition : CellTypeDefinition
    {
        public override ICellData CreateDefault() => new YarnEmptyCell();

        public override void DrawCell(Rect rect, ICellData data)
            => EditorGUI.DrawRect(rect, new Color(0.15f, 0.16f, 0.19f));

        public override void DrawInspector(Rect rect, ref ICellData data) { }
    }
}
