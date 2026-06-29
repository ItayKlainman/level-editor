using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Palette-swatch brush for the coloured Pixel Block. The grid canvas calls
    // DrawCell to render each painted pixel and DrawInspector for the brush picker.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Cells/Pixel")]
    public sealed class BBPixelCellDefinition : CellTypeDefinition
    {
        [SerializeField] private ColorPaletteAsset _palette;

        private static readonly Color UnknownColor = new Color(0.4f, 0.4f, 0.4f);

        // Larger swatches so the brush picker is easy to click during configuration.
        private const float BrushSwatchSize = 32f;
        // Approximate inner width of the BRUSH panel (narrowest consumer).
        private const float BrushInnerWidth = 185f;

        private Vector2 _scroll;

        public override float InspectorPreferredHeight
        {
            get
            {
                if (_palette == null) return 80f;
                return ColorSwatchDrawer.MeasureHeight(_palette, BrushInnerWidth, size: BrushSwatchSize) + 6f;
            }
        }

        public override ICellData CreateDefault() => new BBPixelCell();

        public override void DrawCell(Rect rect, ICellData data)
        {
            if (data is not BBPixelCell pixel) return;
            var color = UnknownColor;
            if (_palette != null) _palette.TryGetColor(pixel.ColorId, out color);
            EditorGUI.DrawRect(rect, color);
        }

        public override void DrawInspector(Rect rect, ref ICellData data)
        {
            if (data is not BBPixelCell pixel) return;

            float contentH = ColorSwatchDrawer.MeasureHeight(_palette, rect.width, size: BrushSwatchSize);

            // Fits — draw inline (popup case).
            if (contentH <= rect.height + 0.5f)
            {
                pixel.ColorId = ColorSwatchDrawer.Draw(rect, _palette, pixel.ColorId, size: BrushSwatchSize);
                return;
            }

            // Doesn't fit — scroll (brush panel case). Reserve 14px for the scrollbar.
            float innerW   = rect.width - 14f;
            float scrollH  = ColorSwatchDrawer.MeasureHeight(_palette, innerW, size: BrushSwatchSize);
            var   viewRect = new Rect(0f, 0f, innerW, scrollH);
            _scroll = GUI.BeginScrollView(rect, _scroll, viewRect);
            pixel.ColorId = ColorSwatchDrawer.Draw(viewRect, _palette, pixel.ColorId, size: BrushSwatchSize);
            GUI.EndScrollView();
        }
    }
}
