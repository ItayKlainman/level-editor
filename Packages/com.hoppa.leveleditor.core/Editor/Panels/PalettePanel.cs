using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public sealed class PalettePanel : IEditorPanel
    {
        private Vector2 _scroll;

        private const float RowH    = 36f;
        private const float RowGap  = 2f;
        private const float SwatchS = 22f;
        private const float Padding = 6f;
        private const float HeaderH = 22f;
        private const float GroupGap = 10f;
        private const float BrushH  = 96f;
        private const float BrushHH = 22f;

        private static readonly Color SelectedBg     = new Color(0.22f, 0.50f, 0.92f, 0.28f);
        private static readonly Color SelectedAccent = new Color(0.35f, 0.68f, 1.00f, 1.00f);
        private static readonly Color RowBg          = new Color(0.19f, 0.20f, 0.24f, 1.00f);
        private static readonly Color HeaderBg       = new Color(0.16f, 0.18f, 0.24f, 1.00f);
        private static readonly Color HeaderAccent   = new Color(0.35f, 0.68f, 1.00f, 0.90f);
        private static readonly Color SwatchBg       = new Color(0.13f, 0.14f, 0.16f, 1.00f);
        private static readonly Color Divider        = new Color(1.00f, 1.00f, 1.00f, 0.04f);
        private static readonly Color BrushPanelBg  = new Color(0.14f, 0.16f, 0.20f, 1.00f);

        public void OnGUI(Rect rect, LevelEditorSession session)
        {
            if (session == null)
            {
                GUI.Label(rect, "No session.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // Split: cell list (top) + brush config (bottom fixed height)
            var listRect  = new Rect(rect.x, rect.y, rect.width, rect.height - BrushH);
            var brushRect = new Rect(rect.x, rect.yMax - BrushH, rect.width, BrushH);

            DrawCellList(listRect, session);
            DrawBrushPanel(brushRect, session);
        }

        private void DrawCellList(Rect rect, LevelEditorSession session)
        {
            var groups    = BuildGroups(session);
            float contentH = MeasureHeight(groups);
            var viewRect   = new Rect(0f, 0f, rect.width - 14f, contentH);
            _scroll = GUI.BeginScrollView(rect, _scroll, viewRect);

            float y  = Padding;
            float vw = viewRect.width;

            foreach (var kv in groups)
            {
                EditorGUI.DrawRect(new Rect(0f, y, vw, 2f), HeaderAccent);
                EditorGUI.DrawRect(new Rect(0f, y + 2f, vw, HeaderH), HeaderBg);
                GUI.Label(new Rect(Padding, y + 4f, vw - Padding, HeaderH - 4f),
                    kv.Key.ToUpper(), EditorStyles.miniLabel);
                y += 2f + HeaderH + RowGap;

                foreach (var def in kv.Value)
                {
                    bool isActive = session.ActiveCellType == def;
                    var rowRect   = new Rect(0f, y, vw, RowH);

                    EditorGUI.DrawRect(rowRect, RowBg);
                    if (isActive)
                    {
                        EditorGUI.DrawRect(rowRect, SelectedBg);
                        EditorGUI.DrawRect(new Rect(0f, y, 3f, RowH), SelectedAccent);
                    }

                    EditorGUI.DrawRect(new Rect(Padding, y + RowH - 1f, vw - Padding * 2f, 1f), Divider);

                    float swatchX  = (isActive ? 7f : 4f) + Padding;
                    float swatchY  = y + (RowH - SwatchS) * 0.5f;
                    var swatchRect = new Rect(swatchX, swatchY, SwatchS, SwatchS);
                    EditorGUI.DrawRect(swatchRect, SwatchBg);
                    if (def.Icon != null)
                        GUI.DrawTexture(swatchRect, def.Icon, ScaleMode.ScaleToFit);
                    else
                        def.DrawCell(swatchRect, def.CreateDefault());

                    float labelX = swatchX + SwatchS + 8f;
                    float labelW = rowRect.xMax - labelX - Padding;
                    GUI.Label(new Rect(labelX, y, labelW, RowH),
                        new GUIContent(def.DisplayName,
                            $"Click to select  |  LMB: paint  |  RMB: erase\nType ID: {def.TypeId}"),
                        isActive ? EditorStyles.boldLabel : EditorStyles.label);

                    if (Event.current.type == EventType.MouseDown
                        && rowRect.Contains(Event.current.mousePosition))
                    {
                        session.ActiveCellType = def;
                        session.BrushTemplate  = def.CreateDefault();
                        Event.current.Use();
                        GUI.changed = true;
                    }

                    y += RowH + RowGap;
                }

                y += GroupGap;
            }

            GUI.EndScrollView();
        }

        private static void DrawBrushPanel(Rect rect, LevelEditorSession session)
        {
            // Background + divider at top
            EditorGUI.DrawRect(rect, BrushPanelBg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), HeaderAccent);

            // Header
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 1f, rect.width, BrushHH), HeaderBg);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, BrushHH - 4f),
                "BRUSH", EditorStyles.miniLabel);

            var def = session.ActiveCellType;
            if (def == null)
            {
                GUI.Label(new Rect(rect.x, rect.y + BrushHH + 1f, rect.width, rect.height - BrushHH - 1f),
                    "—", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // Ensure brush template is valid for the active cell type
            if (session.BrushTemplate == null || session.BrushTemplate.CellTypeId != def.TypeId)
                session.BrushTemplate = def.CreateDefault();

            var inspRect = new Rect(
                rect.x + 4f,
                rect.y + BrushHH + 4f,
                rect.width - 8f,
                rect.height - BrushHH - 8f);

            var brushCell = session.BrushTemplate;
            EditorGUI.BeginChangeCheck();
            def.DrawInspector(inspRect, ref brushCell);
            if (EditorGUI.EndChangeCheck())
                session.BrushTemplate = brushCell;
        }

        private static float MeasureHeight(Dictionary<string, List<ICellTypeDefinition>> groups)
        {
            float h = Padding;
            foreach (var kv in groups)
            {
                h += 2f + HeaderH + RowGap;
                h += kv.Value.Count * (RowH + RowGap);
                h += GroupGap;
            }
            return h;
        }

        private static Dictionary<string, List<ICellTypeDefinition>> BuildGroups(LevelEditorSession session)
        {
            var groups = new Dictionary<string, List<ICellTypeDefinition>>();
            foreach (var def in session.CellTypes.AllDefinitions)
            {
                var key = string.IsNullOrEmpty(def.PaletteGroup) ? "General" : def.PaletteGroup;
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<ICellTypeDefinition>();
                    groups[key] = list;
                }
                list.Add(def);
            }
            return groups;
        }
    }
}
