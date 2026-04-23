using System.Collections.Generic;
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
            float half = rect.width * 0.5f;

            if (_palette != null)
            {
                var ids = new List<string>(_palette.ColorIds);
                int idx = Mathf.Max(0, ids.IndexOf(arrow.ColorId));
                int newIdx = EditorGUI.Popup(new Rect(rect.x, rect.y, half - 2f, rect.height), idx, ids.ToArray());
                if (newIdx != idx) arrow.ColorId = ids[newIdx];
            }

            arrow.Direction = (YarnDirection)EditorGUI.EnumPopup(
                new Rect(rect.x + half, rect.y, half, rect.height), arrow.Direction);
        }
    }
}
