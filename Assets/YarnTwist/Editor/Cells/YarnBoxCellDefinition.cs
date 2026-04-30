using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Cells/Box")]
    public sealed class YarnBoxCellDefinition : CellTypeDefinition, ICellContextActions
    {
        [SerializeField] private ColorPaletteAsset _palette;

        private static readonly Color HiddenColor = new Color(0.40f, 0.32f, 0.52f);

        public override float InspectorPreferredHeight => 60f;

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

            float lh         = EditorGUIUtility.singleLineHeight + 2f;
            float swatchAreaH = rect.height - lh - 2f;
            var   swatchRect  = new Rect(rect.x, rect.y, rect.width, swatchAreaH);
            box.ColorId = ColorSwatchDrawer.Draw(swatchRect, _palette, box.ColorId);

            box.Hidden = EditorGUI.ToggleLeft(
                new Rect(rect.x, rect.y + swatchAreaH + 2f, rect.width, lh), "Hidden", box.Hidden);
        }

        public IEnumerable<CellContextAction> GetContextActions(ICellData cell, CellTypeRegistry registry)
        {
            if (!registry.TryGetDefinition("yt.arrowbox", out _)) yield break;

            var colorId = (cell as YarnBoxCell)?.ColorId ?? "pink";
            // Mutable holder — captures across OnGUI calls via closure
            var dir = new[] { YarnDirection.Right };

            yield return new CellContextAction(
                label: "→ Convert to Arrow Box",
                create: () => new YarnArrowBoxCell { ColorId = colorId, Direction = dir[0] },
                optionsHeight: 22f,
                drawOptions: rect =>
                {
                    GUI.Label(new Rect(rect.x, rect.y, 64f, 18f), "Direction", EditorStyles.miniLabel);
                    dir[0] = (YarnDirection)EditorGUI.EnumPopup(
                        new Rect(rect.x + 66f, rect.y, rect.width - 66f, 18f), dir[0]);
                });
        }
    }
}
