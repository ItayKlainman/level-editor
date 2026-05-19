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

        // Approximate inner width of the BRUSH panel (PaletteW=195 minus side padding).
        // Used to estimate how tall the swatch grid will be — the brush panel is the
        // narrowest consumer of this inspector, so sizing for it also fits the popup.
        private const float BrushInnerWidth = 185f;

        // Persistent scroll position for the brush panel; popup never scrolls because
        // GridCellPopup sizes itself from InspectorPreferredHeight (so content fits).
        private Vector2 _scroll;

        public override float InspectorPreferredHeight
        {
            get
            {
                if (_palette == null) return 80f;
                // +6 buffer so the last row doesn't sit flush against the bottom.
                return ColorSwatchDrawer.MeasureHeight(_palette, BrushInnerWidth, size: BrushSwatchSize) + 6f;
            }
        }

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

            float contentH = ColorSwatchDrawer.MeasureHeight(_palette, rect.width, size: BrushSwatchSize);

            // Fits — draw inline (popup case).
            if (contentH <= rect.height + 0.5f)
            {
                wool.ColorId = ColorSwatchDrawer.Draw(rect, _palette, wool.ColorId, size: BrushSwatchSize);
                return;
            }

            // Doesn't fit — scroll (brush panel case). Reserve 14px for the scrollbar.
            float innerW   = rect.width - 14f;
            float scrollH  = ColorSwatchDrawer.MeasureHeight(_palette, innerW, size: BrushSwatchSize);
            var   viewRect = new Rect(0f, 0f, innerW, scrollH);
            _scroll = GUI.BeginScrollView(rect, _scroll, viewRect);
            wool.ColorId = ColorSwatchDrawer.Draw(viewRect, _palette, wool.ColorId, size: BrushSwatchSize);
            GUI.EndScrollView();
        }
    }
}
