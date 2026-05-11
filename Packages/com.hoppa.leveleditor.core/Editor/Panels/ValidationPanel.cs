using Hoppa.LevelEditor.Core;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public sealed class ValidationPanel
    {
        private Vector2 _scroll;

        private const float HeaderH  = 26f;
        private const float RowHeight = 20f;
        private const float RowPad   = 2f;

        private static readonly Color ErrorBg    = new Color(0.55f, 0.13f, 0.13f, 0.45f);
        private static readonly Color WarningBg  = new Color(0.55f, 0.38f, 0.05f, 0.45f);
        private static readonly Color InfoBg     = new Color(0.13f, 0.32f, 0.52f, 0.45f);

        private static readonly Color HeaderOk   = new Color(0.12f, 0.40f, 0.22f);
        private static readonly Color HeaderWarn = new Color(0.50f, 0.30f, 0.04f);
        private static readonly Color HeaderErr  = new Color(0.54f, 0.11f, 0.11f);
        private static readonly Color HeaderLine = new Color(1.00f, 1.00f, 1.00f, 0.12f);
        private static readonly Color Accent     = new Color(0.30f, 0.65f, 1.00f);

        public CellRef? OnGUI(Rect rect, ValidationReport report)
        {
            bool hasErrors = report?.HasErrors == true;
            bool hasWarnings = false;
            if (report != null && !hasErrors)
                foreach (var entry in report.Entries)
                    if (entry.Severity == ValidationSeverity.Warning) { hasWarnings = true; break; }

            // ── Header ────────────────────────────────────────────────
            var headerBg = hasErrors ? HeaderErr : hasWarnings ? HeaderWarn : HeaderOk;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, HeaderH), headerBg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + HeaderH - 1f, rect.width, 1f), HeaderLine);

            string icon  = hasErrors ? "X" : hasWarnings ? "!" : "OK";
            var iconRect = new Rect(rect.x + 6f, rect.y + 4f, 22f, HeaderH - 8f);
            EditorGUI.DrawRect(iconRect, new Color(1f, 1f, 1f, 0.15f));
            GUI.Label(iconRect, icon, new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize  = 9,
            });
            GUI.Label(new Rect(rect.x + 34f, rect.y + 4f, rect.width - 40f, HeaderH - 8f),
                "VALIDATION", EditorStyles.boldLabel);

            // ── Content ───────────────────────────────────────────────
            var content = new Rect(rect.x, rect.y + HeaderH, rect.width, rect.height - HeaderH);

            if (report == null)
            {
                GUI.Label(content, "No validation data.", EditorStyles.centeredGreyMiniLabel);
                return null;
            }
            if (report.Entries.Count == 0)
            {
                EditorGUI.DrawRect(content, new Color(0.10f, 0.20f, 0.12f, 0.20f));
                GUI.Label(content, "No issues found.", EditorStyles.centeredGreyMiniLabel);
                return null;
            }

            float contentH = report.Entries.Count * (RowHeight + RowPad) + RowPad;
            var viewRect   = new Rect(0, 0, content.width - 14f, contentH);
            _scroll = GUI.BeginScrollView(content, _scroll, viewRect);

            CellRef? clicked = null;
            float y = RowPad;
            foreach (var entry in report.Entries)
            {
                var rowRect = new Rect(0, y, viewRect.width, RowHeight);
                DrawRow(rowRect, entry);

                if (entry.OffendingCell.HasValue
                    && Event.current.type == EventType.MouseDown
                    && rowRect.Contains(Event.current.mousePosition))
                {
                    clicked = entry.OffendingCell;
                    Event.current.Use();
                }
                y += RowHeight + RowPad;
            }

            GUI.EndScrollView();
            return clicked;
        }

        private static void DrawRow(Rect rect, ValidationEntry entry)
        {
            var bg = entry.Severity switch
            {
                ValidationSeverity.Error   => ErrorBg,
                ValidationSeverity.Warning => WarningBg,
                _                          => InfoBg,
            };
            EditorGUI.DrawRect(rect, bg);
            // Severity accent bar
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height),
                entry.Severity == ValidationSeverity.Error   ? new Color(1f, 0.3f, 0.3f) :
                entry.Severity == ValidationSeverity.Warning ? new Color(1f, 0.7f, 0.1f) :
                                                               new Color(0.3f, 0.7f, 1f));

            float msgX = rect.x + 6f;
            if (entry.Swatch.HasValue)
            {
                const float SwatchSize = 12f;
                float sy = rect.y + (rect.height - SwatchSize) * 0.5f;
                var swatchRect = new Rect(msgX, sy, SwatchSize, SwatchSize);
                // Thin dark border for contrast against any background
                EditorGUI.DrawRect(new Rect(swatchRect.x - 1f, swatchRect.y - 1f, SwatchSize + 2f, SwatchSize + 2f),
                    new Color(0f, 0f, 0f, 0.6f));
                EditorGUI.DrawRect(swatchRect, entry.Swatch.Value);
                msgX += SwatchSize + 4f;
            }

            GUI.Label(new Rect(msgX, rect.y + 2f, rect.xMax - msgX - 4f, rect.height - 2f),
                entry.Message, EditorStyles.miniLabel);
        }
    }
}
