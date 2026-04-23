using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Cells/Wall")]
    public sealed class YarnWallCellDefinition : CellTypeDefinition
    {
        private static readonly Color WallDark  = new Color(0.22f, 0.19f, 0.28f);
        private static readonly Color WallLight = new Color(0.32f, 0.28f, 0.40f);

        public override ICellData CreateDefault() => new YarnWallCell();

        public override void DrawCell(Rect rect, ICellData data)
        {
            EditorGUI.DrawRect(rect, WallDark);
            EditorGUI.DrawRect(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f), WallLight);
        }

        public override void DrawInspector(Rect rect, ref ICellData data) { }
    }
}
