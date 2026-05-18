using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YAK.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/YAK/Cells/Wool")]
    public sealed class YAKWoolCellDefinition : CellTypeDefinition
    {
        [SerializeField] private ColorPaletteAsset _palette;

        private static readonly Color UnknownColor = new Color(0.4f, 0.4f, 0.4f);

        // Larger swatches so the brush picker is easier to click during configuration.
        private const float BrushSwatchSize = 32f;

        public override float InspectorPreferredHeight => 140f;

        public override ICellData CreateDefault() => new YAKWoolCell();

        public override void DrawCell(Rect rect, ICellData data)
        {
            if (data is not YAKWoolCell wool) return;
            var color = UnknownColor;
            if (_palette != null) _palette.TryGetColor(wool.ColorId, out color);
            EditorGUI.DrawRect(rect, color);
        }

        public override void DrawInspector(Rect rect, ref ICellData data)
        {
            if (data is not YAKWoolCell wool) return;
            wool.ColorId = ColorSwatchDrawer.Draw(rect, _palette, wool.ColorId, size: BrushSwatchSize);
        }
    }
}
