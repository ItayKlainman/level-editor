using System;
using System.Collections.Generic;
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
        // Pass allowedIds to restrict which palette entries are shown; null shows all.
        // Pass size > 0 to override the default 20px swatch size (e.g. for big brush pickers).
        public static string Draw(Rect rect, ColorPaletteAsset palette, string selectedId,
            ICollection<string> allowedIds = null, float size = 0f)
        {
            if (palette == null) return selectedId;

            float sz = size > 0f ? size : Size;

            string result = selectedId;
            float  x      = rect.x;
            float  y      = rect.y;

            foreach (var entry in palette.Entries)
            {
                if (allowedIds != null && !allowedIds.Contains(entry.Id)) continue;

                // Wrap to next row
                if (x + sz > rect.xMax + 0.5f)
                {
                    x  = rect.x;
                    y += sz + Gap;
                }
                if (y + sz > rect.yMax + 0.5f) break;

                bool isSelected = string.Equals(entry.Id, selectedId, StringComparison.Ordinal);
                bool isHovered  = new Rect(x, y, sz, sz).Contains(Event.current.mousePosition);

                // Border
                if (isSelected)
                    EditorGUI.DrawRect(new Rect(x - 2f, y - 2f, sz + 4f, sz + 4f), SelectBorder);
                else if (isHovered)
                    EditorGUI.DrawRect(new Rect(x - 1f, y - 1f, sz + 2f, sz + 2f), HoverBorder);

                var swatchRect = new Rect(x, y, sz, sz);
                EditorGUI.DrawRect(swatchRect, entry.Color);

                // Tooltip
                GUI.Label(swatchRect, new GUIContent(string.Empty, entry.DisplayName ?? entry.Id));

                if (Event.current.type == EventType.MouseDown && swatchRect.Contains(Event.current.mousePosition))
                {
                    result = entry.Id;
                    Event.current.Use();
                    GUI.changed = true;
                }

                x += sz + Gap;
            }

            return result;
        }

        // Height needed to fit all swatches within the given pixel width.
        // Pass allowedIds to count only a subset of palette entries; null counts all.
        // Pass size > 0 to override the default 20px swatch size.
        public static float MeasureHeight(ColorPaletteAsset palette, float width,
            ICollection<string> allowedIds = null, float size = 0f)
        {
            if (palette == null || palette.Entries.Count == 0) return 0f;
            float sz = size > 0f ? size : Size;
            int count = 0;
            foreach (var entry in palette.Entries)
                if (allowedIds == null || allowedIds.Contains(entry.Id)) count++;
            if (count == 0) return 0f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((width + Gap) / (sz + Gap)));
            int rows   = Mathf.CeilToInt(count / (float)perRow);
            return rows * sz + Mathf.Max(0, rows - 1) * Gap;
        }
    }
}
