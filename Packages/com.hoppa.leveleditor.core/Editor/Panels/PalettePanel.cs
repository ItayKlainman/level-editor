using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public sealed class PalettePanel : IEditorPanel
    {
        private Vector2 _scroll;

        private const float RowH     = 36f;
        private const float RowGap   = 2f;
        private const float SwatchS  = 22f;
        private const float Padding  = 6f;
        private const float HeaderH  = 22f;
        private const float GroupGap = 10f;
        private const float ToolsH   = 80f;   // TOOLS section height
        private const float BrushH   = 180f;  // BRUSH section height (sized for larger swatches)
        private const float BrushHH  = 22f;
        private const float BtnH     = 38f;   // tool button height

        private static readonly Color SelectedBg     = new Color(0.22f, 0.50f, 0.92f, 0.28f);
        private static readonly Color SelectedAccent = new Color(0.35f, 0.68f, 1.00f, 1.00f);
        private static readonly Color RowBg          = new Color(0.19f, 0.20f, 0.24f, 1.00f);
        private static readonly Color HeaderBg       = new Color(0.16f, 0.18f, 0.24f, 1.00f);
        private static readonly Color HeaderAccent   = new Color(0.35f, 0.68f, 1.00f, 0.90f);
        private static readonly Color SwatchBg       = new Color(0.13f, 0.14f, 0.16f, 1.00f);
        private static readonly Color Divider        = new Color(1.00f, 1.00f, 1.00f, 0.04f);
        private static readonly Color ToolsPanelBg   = new Color(0.13f, 0.15f, 0.19f, 1.00f);
        private static readonly Color BrushPanelBg   = new Color(0.14f, 0.16f, 0.20f, 1.00f);
        private static readonly Color ToolActiveBg   = new Color(0.35f, 0.68f, 1.00f, 1.00f);
        private static readonly Color ToolInactiveBg = new Color(0.22f, 0.24f, 0.30f, 1.00f);

        // IEditorPanel entry point — no optional left panel (used by generic callers).
        public void OnGUI(Rect rect, LevelEditorSession session)
            => OnGUI(rect, session, null, null);

        // Full entry point. When `leftPanel` is non-null it is drawn in a reserved
        // region between the cell list and TOOLS (the cell list shrinks to fit);
        // when null the layout is byte-identical to the original (list fills the
        // space above the fixed TOOLS + BRUSH sections).
        public void OnGUI(Rect rect, LevelEditorSession session, ProfileLeftPanel leftPanel, GameProfile profile)
        {
            if (session == null)
            {
                GUI.Label(rect, "No session.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            float fixedBottom  = ToolsH + BrushH;
            float leftPanelH   = leftPanel != null ? leftPanel.PreferredHeight : 0f;

            var listRect  = new Rect(rect.x, rect.y, rect.width, rect.height - fixedBottom - leftPanelH);
            DrawCellList(listRect, session);

            float y = listRect.yMax;
            if (leftPanel != null)
            {
                var leftRect = new Rect(rect.x, y, rect.width, leftPanelH);
                leftPanel.OnGUI(leftRect, session, profile);
                y = leftRect.yMax;
            }

            var toolsRect = new Rect(rect.x, y, rect.width, ToolsH);
            var brushRect = new Rect(rect.x, toolsRect.yMax, rect.width, BrushH);

            DrawToolsPanel(toolsRect, session);
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
                    bool isActive = session.ActiveCellType == def && session.ActiveTool == GridEditTool.Paint;
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
                            $"Click to select  |  LMB: paint  |  RMB: context\nType ID: {def.TypeId}"),
                        isActive ? EditorStyles.boldLabel : EditorStyles.label);

                    if (Event.current.type == EventType.MouseDown
                        && rowRect.Contains(Event.current.mousePosition))
                    {
                        session.ActiveCellType = def;
                        session.BrushTemplate  = def.CreateDefault();
                        session.ActiveTool     = GridEditTool.Paint;
                        Event.current.Use();
                        GUI.changed = true;
                    }

                    y += RowH + RowGap;
                }

                y += GroupGap;
            }

            GUI.EndScrollView();
        }

        // Lazy-loaded icon content (null image = icon not found, falls back to text-only)
        private static GUIContent _selectContent;
        private static GUIContent _deleteContent;
        private static GUIContent _moveContent;

        private static GUIContent ToolContent(ref GUIContent cache, string iconName, string label, string tooltip)
        {
            if (cache != null) return cache;
            var ic = EditorGUIUtility.IconContent(iconName);
            cache = ic?.image != null
                ? new GUIContent(label, ic.image, tooltip)
                : new GUIContent(label, tooltip);
            return cache;
        }

        private static void DrawToolsPanel(Rect rect, LevelEditorSession session)
        {
            EditorGUI.DrawRect(rect, ToolsPanelBg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), HeaderAccent);

            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 1f, rect.width, BrushHH), HeaderBg);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, BrushHH - 4f),
                "TOOLS", EditorStyles.miniLabel);

            float btnY   = rect.y + BrushHH + 5f;
            float totalW = rect.width - Padding * 2f - 4f;

            var painter = session.Profile?.FlagPainter;
            var tools = new System.Collections.Generic.List<(GUIContent, GridEditTool)>
            {
                (ToolContent(ref _selectContent, "d_RectTool",          "Select", "Select cells without painting"),
                 GridEditTool.Select),
                (ToolContent(ref _deleteContent, "d_P4_DeletedLocal",    "Delete", "Click/drag to erase cells"),
                 GridEditTool.Delete),
                (ToolContent(ref _moveContent,   "d_MoveTool",           "Move",   "Click two cells to swap them"),
                 GridEditTool.Move),
            };
            if (painter != null)
                tools.Add((new GUIContent(painter.ToolLabel, painter.ToolTooltip), GridEditTool.Hide));
            var region = session.Profile?.RegionTool;
            if (region != null)
                tools.Add((new GUIContent(region.ToolLabel, region.ToolTooltip), GridEditTool.Region));
            var toolDefs = tools.ToArray();

            float btnW = Mathf.Floor(totalW / Mathf.Max(3, toolDefs.Length));

            var btnStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize     = 10,
                imagePosition = ImagePosition.ImageAbove,
                fixedHeight  = BtnH,
            };

            for (int i = 0; i < toolDefs.Length; i++)
            {
                var (content, tool) = toolDefs[i];
                float bx      = rect.x + Padding + i * (btnW + 2f);
                bool  isActive = session.ActiveTool == tool;

                var old = GUI.backgroundColor;
                GUI.backgroundColor = isActive ? ToolActiveBg : ToolInactiveBg;
                var style = new GUIStyle(btnStyle);
                if (isActive) style.normal.textColor = Color.black;

                if (GUI.Button(new Rect(bx, btnY, btnW, BtnH), content, style))
                    session.ActiveTool = tool;

                GUI.backgroundColor = old;
            }
        }

        private static void DrawBrushPanel(Rect rect, LevelEditorSession session)
        {
            EditorGUI.DrawRect(rect, BrushPanelBg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), HeaderAccent);

            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 1f, rect.width, BrushHH), HeaderBg);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, BrushHH - 4f),
                "BRUSH", EditorStyles.miniLabel);

            var def = session.ActiveCellType;
            if (def == null || session.ActiveTool != GridEditTool.Paint)
            {
                string msg = session.ActiveTool == GridEditTool.Paint ? "—" : $"{session.ActiveTool} mode";
                GUI.Label(new Rect(rect.x, rect.y + BrushHH + 1f, rect.width, rect.height - BrushHH - 1f),
                    msg, EditorStyles.centeredGreyMiniLabel);
                return;
            }

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
