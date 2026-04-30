using System;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Reusable IMGUI color swatch row/grid picker.
    // Draws filled squares from a ColorPaletteAsset and returns the newly selected colorId.
    public static class ColorSwatchDrawer
    {
        public const float Size = 20f;
        public const float Gap  = 3f;

        private static readonly Color SelectBorder = new Color(1f, 1f, 1f, 0.90f);
        private static readonly Color HoverBorder  = new Color(1f, 1f, 1f, 0.35f);

        // Draws swatches wrapping into multiple rows as needed.
        // Returns the new selectedId; unchanged when nothing is clicked.
        public static string Draw(Rect rect, ColorPaletteAsset palette, string selectedId)
        {
            if (palette == null) return selectedId;

            string result = selectedId;
            float  x      = rect.x;
            float  y      = rect.y;

            foreach (var entry in palette.Entries)
            {
                // Wrap to next row
                if (x + Size > rect.xMax + 0.5f)
                {
                    x  = rect.x;
                    y += Size + Gap;
                }
                if (y + Size > rect.yMax + 0.5f) break;

                bool isSelected = string.Equals(entry.Id, selectedId, StringComparison.Ordinal);
                bool isHovered  = new Rect(x, y, Size, Size).Contains(Event.current.mousePosition);

                // Border
                if (isSelected)
                    EditorGUI.DrawRect(new Rect(x - 2f, y - 2f, Size + 4f, Size + 4f), SelectBorder);
                else if (isHovered)
                    EditorGUI.DrawRect(new Rect(x - 1f, y - 1f, Size + 2f, Size + 2f), HoverBorder);

                var swatchRect = new Rect(x, y, Size, Size);
                EditorGUI.DrawRect(swatchRect, entry.Color);

                // Tooltip
                GUI.Label(swatchRect, new GUIContent(string.Empty, entry.DisplayName ?? entry.Id));

                if (Event.current.type == EventType.MouseDown && swatchRect.Contains(Event.current.mousePosition))
                {
                    result = entry.Id;
                    Event.current.Use();
                    GUI.changed = true;
                }

                x += Size + Gap;
            }

            return result;
        }

        // Height needed to fit all swatches within the given pixel width.
        public static float MeasureHeight(ColorPaletteAsset palette, float width)
        {
            if (palette == null || palette.Entries.Count == 0) return 0f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((width + Gap) / (Size + Gap)));
            int rows   = Mathf.CeilToInt(palette.Entries.Count / (float)perRow);
            return rows * Size + Mathf.Max(0, rows - 1) * Gap;
        }
    }
}
