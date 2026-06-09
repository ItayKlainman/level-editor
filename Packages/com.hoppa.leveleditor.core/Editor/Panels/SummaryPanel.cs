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

            // Editable rows contributed by exporters (e.g. Coins from YarnMasterLevelExporter).
            int totalExtraRows = 0;
            foreach (var exp in session.Profile.Exporters)
                if (exp != null) totalExtraRows += exp.ExtraSummaryRowCount;
            float extraEditH = totalExtraRows > 0 ? totalExtraRows * lh + 2f : 0f;

            // Bottom block (anchored to the panel bottom):
            //   [exporter editable rows (Coins)] [Notes label] [Notes textarea]
            float notesTextY = rect.yMax - NotesH;
            float notesLblY  = notesTextY - lh - 2f;
            float extraTop   = notesLblY - extraEditH - 4f;

            var meta = doc.Metadata ?? (doc.Metadata = new LevelMetadata());

            // ── Key-metric rows (ID, Grid only) ────────────────────────
            var oldColor = GUI.contentColor;
            GUI.contentColor = LabelColor;
            Row(ref y, x, lw, lh, $"ID         {levelId}");
            Row(ref y, x, lw, lh, $"Grid       {grid.Width} × {grid.Height}");
            GUI.contentColor = oldColor;

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
