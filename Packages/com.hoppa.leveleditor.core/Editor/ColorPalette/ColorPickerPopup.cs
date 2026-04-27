using System;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public sealed class ColorPickerPopup : PopupWindowContent
    {
        private const float Padding = 8f;
        private const float Width   = 160f;

        private readonly ColorPaletteAsset _palette;
        private readonly string            _currentId;
        private readonly Action<string>    _onSelected;

        public ColorPickerPopup(ColorPaletteAsset palette, string currentId, Action<string> onSelected)
        {
            _palette    = palette;
            _currentId  = currentId;
            _onSelected = onSelected;
        }

        public override Vector2 GetWindowSize()
        {
            float innerW = Width - Padding * 2f;
            float h      = ColorSwatchDrawer.MeasureHeight(_palette, innerW);
            return new Vector2(Width, Mathf.Max(32f, h + Padding * 2f));
        }

        public override void OnGUI(Rect rect)
        {
            var inner  = new Rect(rect.x + Padding, rect.y + Padding,
                                  rect.width - Padding * 2f, rect.height - Padding * 2f);
            var newId = ColorSwatchDrawer.Draw(inner, _palette, _currentId);
            if (!string.Equals(newId, _currentId, StringComparison.Ordinal))
            {
                _onSelected(newId);
                editorWindow.Close();
            }
        }
    }
}
