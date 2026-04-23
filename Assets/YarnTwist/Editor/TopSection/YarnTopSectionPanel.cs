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
        private const float SpoolH  = 24f;
        private const float HeaderH = 22f;
        private const float ColPad  = 4f;

        private static readonly Color HeaderBg = new Color(0.17f, 0.19f, 0.26f);
        private static readonly Color Accent   = new Color(0.30f, 0.65f, 1.00f);
        private static readonly Color HiddenBg = new Color(0.35f, 0.28f, 0.45f);

        public override float PreferredHeight => HeaderH + 18f + SpoolH * 9f + 24f;

        public override void OnGUI(Rect rect, LevelEditorSession session)
        {
            var palette = session.Profile.ColorPalette;
            var topData = session.Document.TopSection != null
                ? session.Document.TopSection.ToObject<YarnTopSectionData>() ?? new YarnTopSectionData()
                : new YarnTopSectionData();

            while (topData.Columns.Count < Columns)
                topData.Columns.Add(new YarnSpoolColumn());

            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, HeaderH), HeaderBg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), Accent);
            GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, 200f, HeaderH - 2f),
                "Spool Columns", EditorStyles.boldLabel);

            float colW = (rect.width - ColPad * (Columns - 1)) / Columns;

            EditorGUI.BeginChangeCheck();

            for (int c = 0; c < Columns; c++)
            {
                float cx = rect.x + c * (colW + ColPad);
                float cy = rect.y + HeaderH + 2f;
                var col = topData.Columns[c];

                GUI.Label(new Rect(cx, cy, colW, 16f), $"Col {c + 1}", EditorStyles.centeredGreyMiniLabel);
                cy += 18f;

                for (int s = col.Spools.Count - 1; s >= 0; s--)
                {
                    var spool = col.Spools[s];
                    var row   = new Rect(cx + 2f, cy, colW - 4f, SpoolH - 2f);
                    cy += SpoolH;

                    Color bg = HiddenBg;
                    if (!spool.Hidden && palette != null) palette.TryGetColor(spool.ColorId, out bg);
                    EditorGUI.DrawRect(new Rect(row.x, row.y + 3f, 16f, 16f), bg);

                    if (palette != null)
                    {
                        var colorIds = new List<string>(palette.ColorIds);
                        int idx = Mathf.Max(0, colorIds.IndexOf(spool.ColorId));
                        float popW = row.width - 20f - 22f;
                        int newIdx = EditorGUI.Popup(new Rect(row.x + 18f, row.y + 2f, popW, SpoolH - 6f),
                            idx, colorIds.ToArray());
                        if (newIdx >= 0 && newIdx < colorIds.Count) spool.ColorId = colorIds[newIdx];
                    }

                    spool.Hidden = EditorGUI.Toggle(new Rect(row.xMax - 20f, row.y + 3f, 18f, 16f), spool.Hidden);
                }

                float btnY = cy + 2f;
                if (GUI.Button(new Rect(cx + 2f, btnY, colW / 2f - 3f, 18f), "+", EditorStyles.miniButton))
                {
                    session.PushUndoSnapshot();
                    string firstId = "pink";
                    if (palette != null) foreach (var id in palette.ColorIds) { firstId = id; break; }
                    col.Spools.Add(new YarnSpoolData { ColorId = firstId });
                }
                if (col.Spools.Count > 0 &&
                    GUI.Button(new Rect(cx + colW / 2f + 1f, btnY, colW / 2f - 3f, 18f), "−", EditorStyles.miniButton))
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
