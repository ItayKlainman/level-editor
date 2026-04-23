using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Demo.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Demo/Box Cell Definition")]
    public sealed class DemoBoxCellDefinition : CellTypeDefinition
    {
        [SerializeField] private ColorPaletteAsset _palette;

        public override ICellData CreateDefault() => new DemoBoxCell();

        public override void DrawCell(Rect rect, ICellData data)
        {
            var color = new Color(0.4f, 0.4f, 0.4f);
            if (data is DemoBoxCell box && _palette != null && _palette.TryGetColor(box.ColorId, out var c))
                color = c;
            EditorGUI.DrawRect(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f), color);
        }

        public override void DrawInspector(Rect rect, ref ICellData data)
        {
            if (data is not DemoBoxCell box) return;
            if (_palette == null) { GUI.Label(rect, "No palette assigned", EditorStyles.miniLabel); return; }

            var ids    = new List<string>(_palette.ColorIds);
            int idx    = ids.IndexOf(box.ColorId);
            int newIdx = EditorGUI.Popup(rect, Mathf.Max(0, idx), ids.ToArray());
            if (newIdx != idx && newIdx >= 0) box.ColorId = ids[newIdx];
        }
    }
}
