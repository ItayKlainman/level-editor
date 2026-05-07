using System.Collections.Generic;
using System.Linq;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    public sealed class YarnTopSectionPanel : TopSectionPanel
    {
        private const int   Columns          = 4;
        private const float SpoolH           = 30f;
        private const float HandleW          = 14f;
        private const float DelBtnW          = 44f;
        private const float ArrowBtnW        = 16f;
        private const float HeaderH          = 22f;
        private const float ColLblH          = 18f;
        private const float BtnH             = 20f;
        private const float ColPad           = 4f;
        private static readonly Color HeaderBg    = new Color(0.17f, 0.19f, 0.26f);
        private static readonly Color Accent      = new Color(0.30f, 0.65f, 1.00f);
        private static readonly Color HiddenTint  = new Color(0.35f, 0.28f, 0.45f, 0.28f);
        private static readonly Color DragDimTint = new Color(0f,    0f,    0f,    0.30f);

        private readonly Vector2[] _scrollPos = new Vector2[Columns];

        // Drag state (within a single column)
        private int _dragCol = -1;
        private int _dragSrc = -1;
        private int _dragTgt = -1;

        // PreferredHeight is no longer used for layout (LevelEditorWindow uses
        // GridCanvasPanel.RequiredHeight to bottom-anchor the canvas instead).
        public override float PreferredHeight => HeaderH + ColLblH + SpoolH + BtnH + 14f;

        public override void OnGUI(Rect rect, LevelEditorSession session)
        {
            var palette = session.Profile.ColorPalette;
            var topData = session.Document.TopSection != null
                ? session.Document.TopSection.ToObject<YarnTopSectionData>() ?? new YarnTopSectionData()
                : new YarnTopSectionData();

            while (topData.Columns.Count < Columns)
                topData.Columns.Add(new YarnSpoolColumn());

            var gridColors = GetGridColors(session);
            ICollection<string> pickerFilter = gridColors.Count > 0 ? gridColors : null;

            // Header
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, HeaderH), HeaderBg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), Accent);
            GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, 200f, HeaderH - 2f),
                "Spool Columns", EditorStyles.boldLabel);

            float colW       = (rect.width - ColPad * (Columns - 1)) / Columns;
            // Scroll area fills all space the window gives us (minus header, col labels, + button).
            // Scroll only activates when a column's content actually overflows this height.
            float scrollAreaH = Mathf.Max(SpoolH,
                rect.height - HeaderH - 2f - ColLblH - BtnH - 14f);

            // Deferred cross-column move (applied after all columns to avoid mid-loop mutation)
            int moveFromCol  = -1;
            int moveFromIdx  = -1;
            int moveToColIdx = -1;
            // Deferred column swap: swapColA and swapColA+1 are exchanged
            int swapColA = -1;

            EditorGUI.BeginChangeCheck();

            for (int c = 0; c < Columns; c++)
            {
                float cx  = rect.x + c * (colW + ColPad);
                float cy  = rect.y + HeaderH + 2f;
                var   col = topData.Columns[c];

                GUI.Label(new Rect(cx, cy, colW, ColLblH),
                    $"Col {c + 1}", EditorStyles.centeredGreyMiniLabel);
                cy += ColLblH;

                int   count         = col.Spools.Count;
                int   removeIdx     = -1;
                bool  needsScrollbar = count * SpoolH > scrollAreaH;
                float contentW      = needsScrollbar ? colW - 14f : colW;
                float contentH      = Mathf.Max(scrollAreaH, count * SpoolH);

                _scrollPos[c] = GUI.BeginScrollView(
                    new Rect(cx, cy, colW, scrollAreaH),
                    _scrollPos[c],
                    new Rect(0f, 0f, contentW, contentH),
                    false, needsScrollbar);

                // ── Drag target: compute in scroll-content-local coords ──────
                if (_dragCol == c && count > 0)
                {
                    _dragTgt = Mathf.Clamp(
                        Mathf.FloorToInt((Event.current.mousePosition.y + SpoolH * 0.5f) / SpoolH),
                        0, count);
                }

                // ── Spool rows (all coords local: x=0, y=visualIdx*SpoolH) ──
                for (int s = count - 1; s >= 0; s--)
                {
                    var   spool     = col.Spools[s];
                    int   visualIdx = count - 1 - s;
                    float rowTop    = visualIdx * SpoolH;

                    bool colDragging   = _dragCol == c;
                    bool thisIsDragged = colDragging && _dragSrc == visualIdx;

                    if (thisIsDragged && Event.current.type == EventType.Repaint)
                        EditorGUI.DrawRect(new Rect(0f, rowTop, contentW, SpoolH), DragDimTint);

                    // Drag handle
                    var handleRect = new Rect(0f, rowTop, HandleW, SpoolH);
                    DrawDragHandle(handleRect);

                    if (Event.current.type == EventType.MouseDown
                        && Event.current.button == 0
                        && handleRect.Contains(Event.current.mousePosition))
                    {
                        _dragCol = c;
                        _dragSrc = visualIdx;
                        _dragTgt = visualIdx;
                        Event.current.Use();
                    }

                    using (new EditorGUI.DisabledGroupScope(colDragging))
                    {
                        float sz      = ColorSwatchDrawer.Size;  // 20px
                        float vCenter = rowTop + (SpoolH - sz) * 0.5f;
                        const float ToggleW = 18f;
                        float toggleX = HandleW + 2f;

                        spool.Hidden = EditorGUI.Toggle(
                            new Rect(toggleX, vCenter + 1f, ToggleW, ToggleW), spool.Hidden);

                        float swatchX    = toggleX + ToggleW + 2f;
                        var   swatchRect = new Rect(swatchX, vCenter, sz, sz);
                        bool  swatchHover = swatchRect.Contains(Event.current.mousePosition);

                        if (swatchHover)
                            EditorGUI.DrawRect(
                                new Rect(swatchRect.x - 1f, swatchRect.y - 1f, sz + 2f, sz + 2f),
                                new Color(1f, 1f, 1f, 0.40f));

                        Color swatchColor = Color.grey;
                        if (palette != null) palette.TryGetColor(spool.ColorId, out swatchColor);
                        EditorGUI.DrawRect(swatchRect, swatchColor);

                        if (spool.Hidden) EditorGUI.DrawRect(swatchRect, HiddenTint);

                        GUI.Label(swatchRect, new GUIContent(string.Empty,
                            $"Right-click to change color\n{spool.ColorId}"));

                        // Right-click → color picker
                        // GUIToScreenPoint needed: mouse pos is content-local inside a scroll group
                        if (Event.current.type == EventType.MouseDown && Event.current.button == 1
                            && swatchHover && palette != null)
                        {
                            var capSpool   = spool;
                            var capTopData = topData;
                            var capSession = session;
                            var screenMp   = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
                            PopupWindow.Show(
                                new Rect(screenMp.x, screenMp.y, 1f, 1f),
                                new ColorPickerPopup(palette, spool.ColorId, id =>
                                {
                                    capSession.PushUndoSnapshot();
                                    capSpool.ColorId = id;
                                    capSession.Document.TopSection = JObject.FromObject(capTopData);
                                    capSession.MarkDirty();
                                }, pickerFilter));
                            Event.current.Use();
                        }

                        // Color ID label
                        float labelX     = swatchX + sz + 4f;
                        float arrowGrpX  = contentW - DelBtnW - ArrowBtnW * 2f - 4f;
                        float labelW     = arrowGrpX - labelX - 4f;
                        if (labelW > 6f)
                            GUI.Label(new Rect(labelX, vCenter + 2f, labelW, sz),
                                spool.ColorId, EditorStyles.miniLabel);

                        // ← → buttons: move spool to adjacent column
                        float arrowBtnY = rowTop + (SpoolH - 18f) * 0.5f;
                        using (new EditorGUI.DisabledGroupScope(c == 0))
                        {
                            if (GUI.Button(new Rect(arrowGrpX, arrowBtnY, ArrowBtnW, 18f),
                                "←", EditorStyles.miniButton))
                            {
                                moveFromCol  = c;
                                moveFromIdx  = s;
                                moveToColIdx = c - 1;
                            }
                        }
                        using (new EditorGUI.DisabledGroupScope(c == Columns - 1))
                        {
                            if (GUI.Button(new Rect(arrowGrpX + ArrowBtnW + 2f, arrowBtnY, ArrowBtnW, 18f),
                                "→", EditorStyles.miniButton))
                            {
                                moveFromCol  = c;
                                moveFromIdx  = s;
                                moveToColIdx = c + 1;
                            }
                        }

                        // Delete button
                        var prevBg = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.85f, 0.35f, 0.35f);
                        if (GUI.Button(
                                new Rect(contentW - DelBtnW, rowTop + (SpoolH - 18f) * 0.5f, DelBtnW, 18f),
                                "✕ Del", EditorStyles.miniButton))
                            removeIdx = s;
                        GUI.backgroundColor = prevBg;
                    }
                }

                // ── Column drag events (inside scroll view) ──────────────────
                if (_dragCol == c)
                {
                    if (Event.current.type == EventType.MouseDrag)
                    {
                        Event.current.Use();
                    }
                    else if (Event.current.type == EventType.MouseUp)
                    {
                        if (count > 0 && _dragTgt != _dragSrc && _dragTgt != _dragSrc + 1)
                        {
                            session.PushUndoSnapshot();
                            MoveVisual(col.Spools, _dragSrc, _dragTgt);
                            GUI.changed = true;
                        }
                        _dragCol = -1; _dragSrc = -1; _dragTgt = -1;
                        Event.current.Use();
                    }

                    if (Event.current.type == EventType.Repaint && _dragTgt >= 0)
                    {
                        float lineY = _dragTgt * SpoolH;
                        EditorGUI.DrawRect(new Rect(0f, lineY - 1f, contentW, 2f), Accent);
                    }
                }

                // Deferred remove
                if (removeIdx >= 0)
                {
                    session.PushUndoSnapshot();
                    col.Spools.RemoveAt(removeIdx);
                }

                GUI.EndScrollView();

                // Bottom row: [← swap col] [+ add] [→ swap col]
                const float AddBtnW = 36f;
                float btnRowY = cy + scrollAreaH + 2f;

                using (new EditorGUI.DisabledGroupScope(c == 0))
                {
                    if (GUI.Button(new Rect(cx + 2f, btnRowY, ArrowBtnW, BtnH), "←", EditorStyles.miniButton))
                        swapColA = c - 1;
                }
                using (new EditorGUI.DisabledGroupScope(c == Columns - 1))
                {
                    if (GUI.Button(new Rect(cx + colW - ArrowBtnW - 2f, btnRowY, ArrowBtnW, BtnH), "→", EditorStyles.miniButton))
                        swapColA = c;
                }

                if (GUI.Button(
                        new Rect(cx + (colW - AddBtnW) * 0.5f, btnRowY, AddBtnW, BtnH),
                        "+", EditorStyles.miniButton))
                {
                    session.PushUndoSnapshot();
                    string newId = col.Spools.Count > 0
                        ? col.Spools[col.Spools.Count - 1].ColorId
                        : GetFirstGridColor(session)
                          ?? (palette != null ? palette.ColorIds.FirstOrDefault() ?? "pink" : "pink");
                    col.Spools.Add(new YarnSpoolData { ColorId = newId });
                }
            }

            // Safety net: release drag if mouse up outside all columns
            if (_dragCol >= 0 && Event.current.type == EventType.MouseUp)
            {
                _dragCol = -1; _dragSrc = -1; _dragTgt = -1;
            }

            // Apply deferred cross-column move (after all column loops)
            if (moveFromCol >= 0)
            {
                session.PushUndoSnapshot();
                var spool = topData.Columns[moveFromCol].Spools[moveFromIdx];
                topData.Columns[moveFromCol].Spools.RemoveAt(moveFromIdx);
                topData.Columns[moveToColIdx].Spools.Add(spool);
                GUI.changed = true;
            }

            // Apply deferred column swap
            if (swapColA >= 0 && swapColA + 1 < Columns)
            {
                session.PushUndoSnapshot();
                (topData.Columns[swapColA], topData.Columns[swapColA + 1]) =
                    (topData.Columns[swapColA + 1], topData.Columns[swapColA]);
                (_scrollPos[swapColA], _scrollPos[swapColA + 1]) =
                    (_scrollPos[swapColA + 1], _scrollPos[swapColA]);
                GUI.changed = true;
            }

            if (EditorGUI.EndChangeCheck())
                session.Document.TopSection = JObject.FromObject(topData);
        }

        // Draws a 2×3 grid of small dots as a drag handle icon.
        private static void DrawDragHandle(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return;
            var dotColor = new Color(0.7f, 0.7f, 0.7f, 0.55f);
            float cx = r.x + (r.width  - 5f)  * 0.5f;
            float cy = r.y + (r.height - 10f) * 0.5f;
            for (int row = 0; row < 3; row++)
            for (int col = 0; col < 2; col++)
                EditorGUI.DrawRect(new Rect(cx + col * 4f, cy + row * 5f, 2f, 2f), dotColor);
        }

        // Moves a spool from visual index vSrc to before visual index vTgt.
        // Visual index 0 = top of column = highest data index (count-1).
        private static void MoveVisual(List<YarnSpoolData> spools, int vSrc, int vTgt)
        {
            int count = spools.Count;
            var visual = new List<YarnSpoolData>(count);
            for (int v = 0; v < count; v++)
                visual.Add(spools[count - 1 - v]);

            var item = visual[vSrc];
            visual.RemoveAt(vSrc);
            visual.Insert(vSrc < vTgt ? vTgt - 1 : vTgt, item);

            for (int v = 0; v < count; v++)
                spools[count - 1 - v] = visual[v];
        }

        private static string GetFirstGridColor(LevelEditorSession session)
        {
            foreach (var cell in session.Document.Grid.Cells)
            {
                if (cell is IColoredCell colored && !string.IsNullOrEmpty(colored.ColorId))
                    return colored.ColorId;
                if (cell is YarnTunnelCell tunnel && tunnel.Queue != null)
                    foreach (var id in tunnel.Queue)
                        if (!string.IsNullOrEmpty(id)) return id;
            }
            return null;
        }

        private static HashSet<string> GetGridColors(LevelEditorSession session)
        {
            var result = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var cell in session.Document.Grid.Cells)
            {
                if (cell is IColoredCell colored && !string.IsNullOrEmpty(colored.ColorId))
                    result.Add(colored.ColorId);
                if (cell is YarnTunnelCell tunnel)
                    foreach (var id in tunnel.Queue)
                        if (!string.IsNullOrEmpty(id)) result.Add(id);
            }
            return result;
        }
    }
}
