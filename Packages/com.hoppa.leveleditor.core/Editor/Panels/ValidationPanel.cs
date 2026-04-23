using Hoppa.LevelEditor.Core;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public sealed class ValidationPanel
    {
        private Vector2 _scroll;

        private static readonly Color ErrorBg    = new Color(0.55f, 0.15f, 0.15f, 0.4f);
        private static readonly Color WarningBg  = new Color(0.55f, 0.45f, 0.05f, 0.4f);
        private static readonly Color InfoBg     = new Color(0.15f, 0.35f, 0.55f, 0.4f);

        private const float RowHeight = 20f;
        private const float Padding   = 2f;

        // Renders the validation report. Returns a CellRef if the user clicked
        // an entry with an offending cell — the host window should navigate there.
        public CellRef? OnGUI(Rect rect, ValidationReport report)
        {
            if (report == null)
            {
                GUI.Label(rect, "No validation data.", EditorStyles.centeredGreyMiniLabel);
                return null;
            }

            if (report.Entries.Count == 0)
            {
                GUI.Label(rect, "No issues found.", EditorStyles.centeredGreyMiniLabel);
                return null;
            }

            float contentH = report.Entries.Count * (RowHeight + Padding);
            var viewRect   = new Rect(0, 0, rect.width - 16, contentH);
            _scroll = GUI.BeginScrollView(rect, _scroll, viewRect);

            CellRef? clicked = null;
            float y = 0;
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

                y += RowHeight + Padding;
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

            var label = $"[{entry.Severity}] {entry.Message}";
            GUI.Label(new Rect(rect.x + 4, rect.y + 2, rect.width - 8, rect.height - 2),
                label, EditorStyles.miniLabel);
        }
    }
}
