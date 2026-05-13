using System.Collections.Generic;
using System.IO;
using Hoppa.LevelEditor.Core;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public sealed class SummaryPanel : IEditorPanel
    {
        private const float HeaderH = 26f;
        private const float NotesH  = 48f;

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

            // Level ID derived from the saved filename; falls back to doc.LevelId for unsaved levels.
            string levelId = !string.IsNullOrEmpty(session.FilePath)
                ? Path.GetFileNameWithoutExtension(session.FilePath)
                : doc.LevelId;

            // Total editable rows contributed by exporters (e.g. Coins from YarnMasterLevelExporter).
            int totalExtraRows = 0;
            foreach (var exp in session.Profile.Exporters)
                if (exp != null) totalExtraRows += exp.ExtraSummaryRowCount;
            float extraEditH = totalExtraRows > 0 ? totalExtraRows * lh + 2f : 0f;

            // Bottom layout (positions, computed once):
            //   [Notes textarea NotesH] [Notes label lh] [gap 2] [APS lh] [gap 4] [exporter editable extraEditH]
            float notesTextY = rect.yMax - NotesH;
            float notesLblY  = notesTextY - lh - 2f;
            float apsY       = notesLblY - lh - 4f;
            float extraTop   = apsY - extraEditH;
            float countsMax  = extraTop - lh - 8f;

            // Ensure metadata exists (needed for APS + Notes below).
            var meta = doc.Metadata ?? (doc.Metadata = new LevelMetadata());

            // ── Accent key-metric rows ─────────────────────────────────
            var oldColor = GUI.contentColor;
            GUI.contentColor = LabelColor;
            Row(ref y, x, lw, lh, $"Schema   {doc.SchemaVersion}");
            Row(ref y, x, lw, lh, $"ID         {levelId}");
            Row(ref y, x, lw, lh, $"Grid       {grid.Width} × {grid.Height}");

            // Exporter info rows (Order, Layout, etc.)
            foreach (var exp in session.Profile.Exporters)
            {
                if (exp == null) continue;
                foreach (var (label, value) in exp.GetSummaryExtras(session))
                    Row(ref y, x, lw, lh, $"{label,-11}{value}");
            }
            GUI.contentColor = oldColor;

            // Separator
            EditorGUI.DrawRect(new Rect(x, y, lw, 1f), new Color(1f, 1f, 1f, 0.07f));
            y += 5f;

            // ── Cell counts ────────────────────────────────────────────
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
                if (y + lh > countsMax) break;
                string label = kv.Key;
                if (session.CellTypes.TryGetDefinition(kv.Key, out var def) &&
                    !string.IsNullOrEmpty(def.DisplayName))
                    label = def.DisplayName;
                GUI.Label(new Rect(x, y, lw - 30f, lh), label, EditorStyles.miniLabel);
                GUI.Label(new Rect(rect.xMax - 30f, y, 26f, lh), kv.Value.ToString(),
                    new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight });
                y += lh;
            }

            // ── Separator before bottom block ──────────────────────────
            if (extraTop > y + 4f)
                EditorGUI.DrawRect(new Rect(x, extraTop - 4f, lw, 1f), new Color(1f, 1f, 1f, 0.07f));

            // ── Exporter editable rows (e.g. Coins) ───────────────────
            if (totalExtraRows > 0)
            {
                float ey = extraTop;
                foreach (var exp in session.Profile.Exporters)
                {
                    if (exp == null || exp.ExtraSummaryRowCount == 0) continue;
                    float rowsH = exp.ExtraSummaryRowCount * lh;
                    exp.DrawExtraSummaryRows(new Rect(x, ey, lw, rowsH), session);
                    ey += rowsH;
                }
            }

            // ── APS field ─────────────────────────────────────────────
            GUI.contentColor = LabelColor;
            GUI.Label(new Rect(x, apsY, 44f, lh), "APS", EditorStyles.miniLabel);
            GUI.contentColor = oldColor;

            EditorGUI.BeginChangeCheck();
            float aps = EditorGUI.FloatField(
                new Rect(x + 46f, apsY, lw - 46f, lh), meta.Aps, EditorStyles.miniTextField);
            if (EditorGUI.EndChangeCheck())
            {
                meta.Aps = aps;
                session.MarkDirty();
            }

            // ── Notes label + textarea ────────────────────────────────
            GUI.contentColor = LabelColor;
            GUI.Label(new Rect(x, notesLblY, lw, lh), "Notes", EditorStyles.miniLabel);
            GUI.contentColor = oldColor;

            var notesRect = new Rect(x, notesTextY, lw, NotesH);
            EditorGUI.BeginChangeCheck();
            string notes = EditorGUI.TextArea(notesRect, meta.Notes ?? string.Empty, EditorStyles.miniTextField);
            if (EditorGUI.EndChangeCheck())
            {
                meta.Notes = notes;
                session.MarkDirty();
            }
        }

        private static void Row(ref float y, float x, float w, float lh, string text)
        {
            GUI.Label(new Rect(x, y, w, lh), text, EditorStyles.miniLabel);
            y += lh;
        }
    }
}
