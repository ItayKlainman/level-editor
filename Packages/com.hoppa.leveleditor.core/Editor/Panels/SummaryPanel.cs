using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public sealed class SummaryPanel : IEditorPanel
    {
        private const float HeaderH = 26f;

        private static readonly Color HeaderBg  = new Color(0.17f, 0.21f, 0.33f);
        private static readonly Color Accent     = new Color(0.30f, 0.65f, 1.00f);
        private static readonly Color LabelColor = new Color(0.55f, 0.68f, 0.85f);

        public void OnGUI(Rect rect, LevelEditorSession session)
        {
            // ── Header ────────────────────────────────────────────────
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, HeaderH), HeaderBg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), Accent);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 5f, rect.width - 16f, HeaderH - 10f),
                "SUMMARY", EditorStyles.boldLabel);

            if (session?.Document == null)
            {
                GUI.Label(new Rect(rect.x, rect.y + HeaderH, rect.width, rect.height - HeaderH),
                    "No level.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var doc  = session.Document;
            var grid = doc.Grid;
            float x  = rect.x + 8f;
            float y  = rect.y + HeaderH + 6f;
            float lh = EditorGUIUtility.singleLineHeight + 2f;
            float lw = rect.width - 16f;

            // Accent-coloured key metrics
            var oldColor = GUI.contentColor;
            GUI.contentColor = LabelColor;
            Row(ref y, x, lw, lh, $"Schema   {doc.SchemaVersion}");
            Row(ref y, x, lw, lh, $"ID         {doc.LevelId}");
            Row(ref y, x, lw, lh, $"Grid       {grid.Width} × {grid.Height}");
            GUI.contentColor = oldColor;

            // Separator
            EditorGUI.DrawRect(new Rect(x, y, lw, 1f), new Color(1f, 1f, 1f, 0.07f));
            y += 5f;

            // Cell counts
            var counts = new Dictionary<string, int>();
            for (int cy = 0; cy < grid.Height; cy++)
            for (int cx2 = 0; cx2 < grid.Width; cx2++)
            {
                var cell = grid.Get(cx2, cy);
                if (cell == null) continue;
                counts.TryGetValue(cell.CellTypeId, out int n);
                counts[cell.CellTypeId] = n + 1;
            }

            foreach (var kv in counts)
            {
                if (y + lh > rect.yMax) break;
                GUI.Label(new Rect(x, y, lw - 30f, lh), kv.Key, EditorStyles.miniLabel);
                GUI.Label(new Rect(rect.xMax - 30f, y, 26f, lh), kv.Value.ToString(),
                    new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight });
                y += lh;
            }
        }

        private static void Row(ref float y, float x, float w, float lh, string text)
        {
            GUI.Label(new Rect(x, y, w, lh), text, EditorStyles.miniLabel);
            y += lh;
        }
    }
}
