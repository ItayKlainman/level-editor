using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Cells/Box")]
    public sealed class YarnBoxCellDefinition : CellTypeDefinition
    {
        [SerializeField] private ColorPaletteAsset _palette;

        private static readonly Color HiddenColor = new Color(0.40f, 0.32f, 0.52f);

        public override ICellData CreateDefault() => new YarnBoxCell();

        public override void DrawCell(Rect rect, ICellData data)
        {
            if (data is not YarnBoxCell box) return;

            if (box.Hidden)
            {
                EditorGUI.DrawRect(rect, HiddenColor);
                GUI.Label(rect, "?", new GUIStyle(EditorStyles.boldLabel)
                    { alignment = TextAnchor.MiddleCenter, fontSize = 14, normal = { textColor = Color.white } });
                return;
            }

            var color = new Color(0.4f, 0.4f, 0.4f);
            if (_palette != null && _palette.TryGetColor(box.ColorId, out var c)) color = c;
            EditorGUI.DrawRect(rect, color);
        }

        public override void DrawInspector(Rect rect, ref ICellData data)
        {
            if (data is not YarnBoxCell box) return;
            float half = rect.width * 0.5f;

            if (_palette != null)
            {
                var ids = new List<string>(_palette.ColorIds);
                int idx = Mathf.Max(0, ids.IndexOf(box.ColorId));
                int newIdx = EditorGUI.Popup(new Rect(rect.x, rect.y, half - 2f, rect.height), idx, ids.ToArray());
                if (newIdx != idx) box.ColorId = ids[newIdx];
            }

            box.Hidden = EditorGUI.ToggleLeft(new Rect(rect.x + half, rect.y, half, rect.height), "Hidden", box.Hidden);
        }
    }
}
