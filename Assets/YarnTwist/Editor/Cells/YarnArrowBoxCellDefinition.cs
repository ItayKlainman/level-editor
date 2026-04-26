using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Cells/Arrow Box")]
    public sealed class YarnArrowBoxCellDefinition : CellTypeDefinition
    {
        [SerializeField] private ColorPaletteAsset _palette;

        public override ICellData CreateDefault() => new YarnArrowBoxCell();

        public override void DrawCell(Rect rect, ICellData data)
        {
            if (data is not YarnArrowBoxCell arrow) return;
            var color = new Color(0.4f, 0.4f, 0.4f);
            if (_palette != null && _palette.TryGetColor(arrow.ColorId, out var c)) color = c;
            EditorGUI.DrawRect(rect, color);

            string ch = arrow.Direction switch
            {
                YarnDirection.Up    => "↑", YarnDirection.Down  => "↓",
                YarnDirection.Left  => "←", YarnDirection.Right => "→",
                _ => "?"
            };
            GUI.Label(rect, ch, new GUIStyle(EditorStyles.boldLabel)
                { alignment = TextAnchor.MiddleCenter, fontSize = 14, normal = { textColor = Color.white } });
        }

        public override void DrawInspector(Rect rect, ref ICellData data)
        {
            if (data is not YarnArrowBoxCell arrow) return;

            float lh         = EditorGUIUtility.singleLineHeight + 2f;
            float swatchAreaH = rect.height - lh - 2f;
            var   swatchRect  = new Rect(rect.x, rect.y, rect.width, swatchAreaH);
            arrow.ColorId = ColorSwatchDrawer.Draw(swatchRect, _palette, arrow.ColorId);

            arrow.Direction = (YarnDirection)EditorGUI.EnumPopup(
                new Rect(rect.x, rect.y + swatchAreaH + 2f, rect.width, lh), arrow.Direction);
        }
    }
}
