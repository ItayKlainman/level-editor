using System.Collections.Generic;
using System.Linq;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YAK.Editor
{
    // Bottom-section spool queue editor for YAK.
    // Lives in the framework's top-section slot (the editor layout doesn't need to mirror
    // the game's bottom placement). Variable column count (2..5). Each spool has a color,
    // a capacity int, and an IsHidden toggle. Capacity int is drawn in-line next to the swatch.
    public sealed class YAKSpoolSectionPanel : TopSectionPanel
    {
        public const int MinColumns = 2;
        public const int MaxColumns = 7;

        private const float SpoolH    = 30f;
        private const float HandleW   = 14f;
        private const float DelBtnW   = 40f;
        private const float ArrowBtnW = 16f;
        private const float HeaderH   = 22f;
        private const float ColLblH   = 18f;
        private const float BtnH      = 20f;
        private const float ColPad    = 4f;
        private const float CapW      = 38f;

        private static readonly Color HeaderBg    = new Color(0.17f, 0.19f, 0.26f);
        private static readonly Color Accent      = new Color(0.30f, 0.65f, 1.00f);
        private static readonly Color HiddenTint  = new Color(0.35f, 0.28f, 0.45f, 0.28f);
        private static readonly Color DragDimTint = new Color(0f,    0f,    0f,    0.30f);

        // Scroll positions per column; sized lazily.
        private Vector2[] _scrollPos = new Vector2[MaxColumns];

        // Drag state (within a single column).
        private int _dragCol = -1;
        private int _dragSrc = -1;
        private int _dragTgt = -1;

        // Reserved height. The LevelEditorWindow guarantees at least this much
        // for the spool panel even when the grid would otherwise consume the
        // entire centre column (e.g. 30×30 with default cell size). Sized to
        // show ~5 spool rows + headers + add/remove buttons comfortably.
        public override float PreferredHeight =>
            HeaderH + ColLblH + (SpoolH * 5f) + BtnH + 24f;

        public override void OnGUI(Rect rect, LevelEditorSession session)
        {
            var palette = session.Profile.ColorPalette;
            var topData = session.Document.TopSection != null
                ? session.Document.TopSection.ToObject<YAKTopSectionData>() ?? new YAKTopSectionData()
                : new YAKTopSectionData();

            // Bootstrap: at least MinColumns visible.
            while (topData.Columns.Count < MinColumns)
                topData.Columns.Add(new YAKSpoolColumn());

            int columnCount = Mathf.Clamp(topData.Columns.Count, MinColumns, MaxColumns);

            var gridColors = GetGridColors(session);
            ICollection<string> pickerFilter = gridColors.Count > 0 ? gridColors : null;

            // Header bar with prominent +/- COLUMN controls (text-labelled buttons).
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, HeaderH), HeaderBg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), Accent);
            GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, 220f, HeaderH - 2f),
                $"Spool Columns ({columnCount}/{MaxColumns})", EditorStyles.boldLabel);

            const float HdrBtnW = 78f;
            const float HdrBtnH = 18f;
            float hdrBtnY = rect.y + (HeaderH - HdrBtnH) * 0.5f;
            float hdrBtnX = rect.xMax - HdrBtnW * 2f - 12f;

            var removeStyle = new GUIStyle(EditorStyles.miniButton) { fontStyle = FontStyle.Bold };
            var addStyle    = new GUIStyle(EditorStyles.miniButton) { fontStyle = FontStyle.Bold };

            using (new EditorGUI.DisabledGroupScope(columnCount <= MinColumns))
            {
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.85f, 0.45f, 0.45f);
                if (GUI.Button(new Rect(hdrBtnX, hdrBtnY, HdrBtnW, HdrBtnH),
                    new GUIContent("− Column", "Remove the last column"), removeStyle))
                {
                    if (topData.Columns.Count > MinColumns)
                    {
                        session.PushUndoSnapshot();
                        topData.Columns.RemoveAt(topData.Columns.Count - 1);
                        session.Document.TopSection = JObject.FromObject(topData);
                        session.MarkDirty();
                    }
                }
                GUI.backgroundColor = prevBg;
            }
            using (new EditorGUI.DisabledGroupScope(columnCount >= MaxColumns))
            {
                var prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.45f, 0.75f, 0.50f);
                if (GUI.Button(new Rect(hdrBtnX + HdrBtnW + 6f, hdrBtnY, HdrBtnW, HdrBtnH),
                    new GUIContent("+ Column", "Add a new column"), addStyle))
                {
                    if (topData.Columns.Count < MaxColumns)
                    {
                        session.PushUndoSnapshot();
                        topData.Columns.Add(new YAKSpoolColumn());
                        session.Document.TopSection = JObject.FromObject(topData);
                        session.MarkDirty();
                    }
                }
                GUI.backgroundColor = prevBg;
            }

            if (_scrollPos.Length < columnCount) _scrollPos = new Vector2[Mathf.Max(columnCount, MaxColumns)];

            float colW       = (rect.width - ColPad * (columnCount - 1)) / columnCount;
            float scrollAreaH = Mathf.Max(SpoolH,
                rect.height - HeaderH - 2f - ColLblH - BtnH - 14f);

            // Deferred mutations to avoid mid-loop list mutation.
            int moveFromCol  = -1;
            int moveFromIdx  = -1;
            int moveToColIdx = -1;
            int swapColA     = -1;

            EditorGUI.BeginChangeCheck();

            for (int c = 0; c < columnCount; c++)
            {
                float cx  = rect.x + c * (colW + ColPad);
                float cy  = rect.y + HeaderH + 2f;
                var   col = topData.Columns[c];

                GUI.Label(new Rect(cx, cy, colW, ColLblH),
                    $"Col {c + 1}", EditorStyles.centeredGreyMiniLabel);
                cy += ColLblH;

                int   count          = col.Spools.Count;
                int   removeIdx      = -1;
                bool  needsScrollbar = count * SpoolH > scrollAreaH;
                float contentW       = needsScrollbar ? colW - 14f : colW;
                float contentH       = Mathf.Max(scrollAreaH, count * SpoolH);

                _scrollPos[c] = GUI.BeginScrollView(
                    new Rect(cx, cy, colW, scrollAreaH),
                    _scrollPos[c],
                    new Rect(0f, 0f, contentW, contentH),
                    false, needsScrollbar);

                if (_dragCol == c && count > 0)
                {
                    _dragTgt = Mathf.Clamp(
                        Mathf.FloorToInt((Event.current.mousePosition.y + SpoolH * 0.5f) / SpoolH),
                        0, count);
                }

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
                        float sz      = ColorSwatchDrawer.Size;
                        float vCenter = rowTop + (SpoolH - sz) * 0.5f;
                        const float ToggleW = 18f;
                        float toggleX = HandleW + 2f;

                        // Hidden toggle
                        bool newHidden = EditorGUI.Toggle(
                            new Rect(toggleX, vCenter + 1f, ToggleW, ToggleW), spool.Hidden);
                        if (newHidden != spool.Hidden)
                        {
                            session.PushUndoSnapshot();
                            spool.Hidden = newHidden;
                            session.MarkDirty();
                        }

                        // Color swatch (right-click to pick)
                        float swatchX     = toggleX + ToggleW + 2f;
                        var   swatchRect  = new Rect(swatchX, vCenter, sz, sz);
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

                        if (Event.current.type == EventType.MouseDown && Event.current.button == 1
                            && swatchHover && palette != null)
                        {
                            var capSpool   = spool;
                            var capTopData = topData;
                            var capSession = session;
                            PopupWindow.Show(
                                swatchRect,
                                new ColorPickerPopup(palette, spool.ColorId, id =>
                                {
                                    capSession.PushUndoSnapshot();
                                    capSpool.ColorId = id;
                                    capSession.Document.TopSection = JObject.FromObject(capTopData);
                                    capSession.MarkDirty();
                                }, pickerFilter));
                            Event.current.Use();
                        }

                        // Capacity int field
                        float capX = swatchX + sz + 4f;
                        var capRect = new Rect(capX, vCenter + 1f, CapW, sz - 2f);
                        EditorGUI.BeginChangeCheck();
                        int newCap = EditorGUI.IntField(capRect, spool.Capacity, EditorStyles.miniTextField);
                        if (EditorGUI.EndChangeCheck() && newCap != spool.Capacity)
                        {
                            session.PushUndoSnapshot();
                            spool.Capacity = Mathf.Max(0, newCap);
                            session.MarkDirty();
                        }

                        // ← → buttons: move spool to adjacent column
                        float arrowGrpX = contentW - DelBtnW - ArrowBtnW * 2f - 4f;
                        float arrowBtnY = rowTop + (SpoolH - 18f) * 0.5f;

                        // Color ID label (between capacity and arrows, if room)
                        float labelX = capX + CapW + 4f;
                        float labelW = arrowGrpX - labelX - 4f;
                        if (labelW > 6f)
                            GUI.Label(new Rect(labelX, vCenter + 2f, labelW, sz),
                                spool.ColorId, EditorStyles.miniLabel);

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
                        using (new EditorGUI.DisabledGroupScope(c == columnCount - 1))
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
                                new Rect(contentW - DelBtnW, arrowBtnY, DelBtnW, 18f),
                                "✕ Del", EditorStyles.miniButton))
                            removeIdx = s;
                        GUI.backgroundColor = prevBg;
                    }
                }

                // Column drag events (inside scroll view)
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

                if (removeIdx >= 0)
                {
                    session.PushUndoSnapshot();
                    col.Spools.RemoveAt(removeIdx);
                }

                GUI.EndScrollView();

                // Bottom row: [← swap col] [+ add spool] [→ swap col]
                const float AddBtnW = 36f;
                float btnRowY = cy + scrollAreaH + 2f;

                using (new EditorGUI.DisabledGroupScope(c == 0))
                {
                    if (GUI.Button(new Rect(cx + 2f, btnRowY, ArrowBtnW, BtnH), "←", EditorStyles.miniButton))
                        swapColA = c - 1;
                }
                using (new EditorGUI.DisabledGroupScope(c == columnCount - 1))
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
                          ?? (palette != null ? palette.ColorIds.FirstOrDefault() ?? "blue" : "blue");
                    int newCap = col.Spools.Count > 0 ? col.Spools[col.Spools.Count - 1].Capacity : 20;
                    col.Spools.Add(new YAKSpoolEntry { ColorId = newId, Capacity = newCap });
                }
            }

            // Safety net: release drag if mouse up outside all columns
            if (_dragCol >= 0 && Event.current.type == EventType.MouseUp)
            {
                _dragCol = -1; _dragSrc = -1; _dragTgt = -1;
            }

            // Apply deferred cross-column move
            if (moveFromCol >= 0 && moveToColIdx >= 0 && moveToColIdx < columnCount)
            {
                session.PushUndoSnapshot();
                var spool = topData.Columns[moveFromCol].Spools[moveFromIdx];
                topData.Columns[moveFromCol].Spools.RemoveAt(moveFromIdx);
                topData.Columns[moveToColIdx].Spools.Add(spool);
                GUI.changed = true;
            }

            // Apply deferred column swap
            if (swapColA >= 0 && swapColA + 1 < columnCount)
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
        private static void MoveVisual(List<YAKSpoolEntry> spools, int vSrc, int vTgt)
        {
            int count = spools.Count;
            var visual = new List<YAKSpoolEntry>(count);
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
            }
            return result;
        }
    }
}
