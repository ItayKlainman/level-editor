using System.Collections.Generic;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    public sealed class YarnTopSectionPanel : TopSectionPanel
    {
        private const int   Columns = 4;
        private const float SpoolH  = 26f;  // height per spool row (swatch + toggle)
        private const float HeaderH = 22f;
        private const float ColLblH = 18f;
        private const float BtnH    = 20f;
        private const float ColPad  = 4f;

        private static readonly Color HeaderBg = new Color(0.17f, 0.19f, 0.26f);
        private static readonly Color Accent   = new Color(0.30f, 0.65f, 1.00f);
        private static readonly Color HiddenTint = new Color(0.35f, 0.28f, 0.45f, 0.65f);

        // Updated in OnGUI so PreferredHeight matches actual spool count next frame.
        private int _maxSpoolCount = 3;

        public override float PreferredHeight =>
            HeaderH + ColLblH + SpoolH * Mathf.Max(1, _maxSpoolCount) + BtnH + 8f;

        public override void OnGUI(Rect rect, LevelEditorSession session)
        {
            var palette = session.Profile.ColorPalette;
            var topData = session.Document.TopSection != null
                ? session.Document.TopSection.ToObject<YarnTopSectionData>() ?? new YarnTopSectionData()
                : new YarnTopSectionData();

            while (topData.Columns.Count < Columns)
                topData.Columns.Add(new YarnSpoolColumn());

            // Update cached spool count for next-frame height
            _maxSpoolCount = 0;
            foreach (var col in topData.Columns)
                if (col?.Spools != null) _maxSpoolCount = Mathf.Max(_maxSpoolCount, col.Spools.Count);

            // Header
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, HeaderH), HeaderBg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), Accent);
            GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, 200f, HeaderH - 2f),
                "Spool Columns", EditorStyles.boldLabel);

            float colW = (rect.width - ColPad * (Columns - 1)) / Columns;

            EditorGUI.BeginChangeCheck();

            for (int c = 0; c < Columns; c++)
            {
                float cx  = rect.x + c * (colW + ColPad);
                float cy  = rect.y + HeaderH + 2f;
                var   col = topData.Columns[c];

                GUI.Label(new Rect(cx, cy, colW, ColLblH),
                    $"Col {c + 1}", EditorStyles.centeredGreyMiniLabel);
                cy += ColLblH;

                // Spools drawn bottom-to-top (index 0 = bottom of column)
                for (int s = col.Spools.Count - 1; s >= 0; s--)
                {
                    var   spool    = col.Spools[s];
                    float rowTop   = cy;
                    cy += SpoolH;

                    const float ToggleW = 18f;
                    float sz = ColorSwatchDrawer.Size;  // 20px

                    // ── Single color swatch (click to cycle) ──────────────
                    var swatchRect = new Rect(cx + 2f, rowTop + 3f, sz, sz);
                    bool isHovered = swatchRect.Contains(Event.current.mousePosition);

                    // Hover outline — drawn first so swatch sits on top
                    if (isHovered)
                        EditorGUI.DrawRect(
                            new Rect(swatchRect.x - 1f, swatchRect.y - 1f, sz + 2f, sz + 2f),
                            new Color(1f, 1f, 1f, 0.40f));

                    Color swatchColor = Color.grey;
                    if (palette != null) palette.TryGetColor(spool.ColorId, out swatchColor);
                    EditorGUI.DrawRect(swatchRect, swatchColor);

                    if (spool.Hidden)
                        EditorGUI.DrawRect(swatchRect, HiddenTint);

                    GUI.Label(swatchRect, new GUIContent(string.Empty, $"Click to cycle color\n{spool.ColorId}"));

                    if (Event.current.type == EventType.MouseDown && isHovered && palette != null)
                    {
                        var ids = new List<string>(palette.ColorIds);
                        if (ids.Count > 0)
                        {
                            int ci = ids.IndexOf(spool.ColorId);
                            spool.ColorId = ids[(ci + 1) % ids.Count];
                            Event.current.Use();
                        }
                    }

                    // ── Color label ───────────────────────────────────────
                    float labelX = cx + 2f + sz + 4f;
                    float labelW = colW - ToggleW - sz - 10f;
                    GUI.Label(new Rect(labelX, rowTop + 4f, labelW, sz), spool.ColorId, EditorStyles.miniLabel);

                    // ── Hidden toggle ─────────────────────────────────────
                    spool.Hidden = EditorGUI.Toggle(
                        new Rect(cx + colW - ToggleW, rowTop + 4f, ToggleW, ToggleW), spool.Hidden);
                }

                // +/− buttons
                float btnY = cy + 2f;
                if (GUI.Button(new Rect(cx + 2f, btnY, colW / 2f - 3f, BtnH), "+", EditorStyles.miniButton))
                {
                    session.PushUndoSnapshot();
                    string firstId = "pink";
                    if (palette != null) foreach (var id in palette.ColorIds) { firstId = id; break; }
                    col.Spools.Add(new YarnSpoolData { ColorId = firstId });
                }
                if (col.Spools.Count > 0 &&
                    GUI.Button(new Rect(cx + colW / 2f + 1f, btnY, colW / 2f - 3f, BtnH), "−", EditorStyles.miniButton))
                {
                    session.PushUndoSnapshot();
                    col.Spools.RemoveAt(col.Spools.Count - 1);
                }
            }

            if (EditorGUI.EndChangeCheck())
                session.Document.TopSection = JObject.FromObject(topData);
        }
    }
}
