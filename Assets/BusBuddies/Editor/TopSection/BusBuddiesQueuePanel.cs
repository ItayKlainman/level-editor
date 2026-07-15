using System.Collections.Generic;
using System.Linq;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Bus-queue editor for Bus Buddies. A clone of YAKSpoolSectionPanel retyped to
    // BusQueueData / BusColumn / BusEntry. Lives in the framework's top-section slot
    // (the editor layout does not need to mirror the game's placement).
    //
    // 1..5 columns. Each bus has a color swatch (right-click to pick, filtered to grid
    // colors), a capacity IntField, and a Hidden toggle. HEAD SEMANTICS: BusColumn.Buses[0]
    // is the only tappable bus (the queue head); the back of the queue is the last element.
    // This panel draws the head at the TOP of each column (visual top row = data index 0),
    // tagged "HEAD", so the top-to-bottom reading matches the in-game pull order (the game
    // taps BusConfigs[0] first — verified against BUBBusColumnPrefabComponent).
    // Connected-bus UI: each bus has a link button. Click it on bus A (anchors it), then on a
    // bus in another column to pair them (a shared ConnectedId). A connected bus's button shows
    // its pair number and disconnects the pair when clicked. Completion is refused if it would
    // create a soft-lock (BusConnection.ConnectionsDeadlock on a trial clone).
    public sealed class BusBuddiesQueuePanel : TopSectionPanel
    {
        public const int MinColumns = 1;
        public const int MaxColumns = 5;

        private const float BusH      = 30f;
        private const float HandleW   = 14f;
        private const float DelBtnW   = 40f;
        private const float ArrowBtnW = 16f;
        private const float HeaderH   = 22f;
        private const float ColLblH   = 18f;
        private const float BtnH      = 20f;
        private const float ColPad    = 4f;
        private const float CapW      = 38f;
        private const float HeadTagW  = 34f;

        private static readonly Color HeaderBg    = new Color(0.17f, 0.19f, 0.26f);
        private static readonly Color Accent      = new Color(0.30f, 0.65f, 1.00f);
        private static readonly Color HiddenTint  = new Color(0.35f, 0.28f, 0.45f, 0.28f);
        private static readonly Color DragDimTint = new Color(0f,    0f,    0f,    0.30f);
        private static readonly Color HeadTint    = new Color(0.30f, 0.65f, 1.00f, 0.18f);

        // Scroll positions per column; sized lazily.
        private Vector2[] _scrollPos = new Vector2[MaxColumns];

        // Drag state (within a single column).
        private int _dragCol = -1;
        private int _dragSrc = -1;
        private int _dragTgt = -1;

        // Connect-mode: the first bus picked, awaiting a partner. null = idle.
        private (int col, int pos)? _pendingAnchor;

        private const float LinkBtnW = 22f;
        private static readonly Color LinkAnchorBg    = new Color(1.00f, 0.85f, 0.20f);
        private static readonly Color LinkConnectedBg = new Color(0.45f, 0.75f, 0.95f);

        // Reserved height. Sized to show ~5 bus rows + headers + add/remove buttons.
        public override float PreferredHeight =>
            HeaderH + ColLblH + (BusH * 5f) + BtnH + 24f;

        public override void OnGUI(Rect rect, LevelEditorSession session)
        {
            var palette = session.Profile.ColorPalette;
            var queue = session.Document.TopSection != null
                ? session.Document.TopSection.ToObject<BusQueueData>() ?? new BusQueueData()
                : new BusQueueData();

            // Bootstrap: at least MinColumns visible.
            while (queue.Columns.Count < MinColumns)
                queue.Columns.Add(new BusColumn());

            int columnCount = Mathf.Clamp(queue.Columns.Count, MinColumns, MaxColumns);

            // Connection groups for this frame: id -> members, used for the pair badge.
            BusConnection.BuildConnInfo(queue, out var connMembers, out _);

            var gridColors = GetGridColors(session);
            ICollection<string> pickerFilter = gridColors.Count > 0 ? gridColors : null;

            // Header bar with prominent +/- COLUMN controls (text-labelled buttons).
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, HeaderH), HeaderBg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), Accent);
            GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, 360f, HeaderH - 2f),
                $"Bus Queue ({columnCount}/{MaxColumns})  ·  top bus = tappable HEAD", EditorStyles.boldLabel);

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
                    if (queue.Columns.Count > MinColumns)
                    {
                        session.PushUndoSnapshot();
                        queue.Columns.RemoveAt(queue.Columns.Count - 1);
                        session.Document.TopSection = JObject.FromObject(queue);
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
                    if (queue.Columns.Count < MaxColumns)
                    {
                        session.PushUndoSnapshot();
                        queue.Columns.Add(new BusColumn());
                        session.Document.TopSection = JObject.FromObject(queue);
                        session.MarkDirty();
                    }
                }
                GUI.backgroundColor = prevBg;
            }

            if (_scrollPos.Length < columnCount) _scrollPos = new Vector2[Mathf.Max(columnCount, MaxColumns)];

            float colW       = (rect.width - ColPad * (columnCount - 1)) / columnCount;
            float scrollAreaH = Mathf.Max(BusH,
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
                var   col = queue.Columns[c];

                GUI.Label(new Rect(cx, cy, colW, ColLblH),
                    $"Col {c + 1}", EditorStyles.centeredGreyMiniLabel);
                cy += ColLblH;

                int   count          = col.Buses.Count;
                int   removeIdx      = -1;
                bool  needsScrollbar = count * BusH > scrollAreaH;
                float contentW       = needsScrollbar ? colW - 14f : colW;
                float contentH       = Mathf.Max(scrollAreaH, count * BusH);

                _scrollPos[c] = GUI.BeginScrollView(
                    new Rect(cx, cy, colW, scrollAreaH),
                    _scrollPos[c],
                    new Rect(0f, 0f, contentW, contentH),
                    false, needsScrollbar);

                if (_dragCol == c && count > 0)
                {
                    _dragTgt = Mathf.Clamp(
                        Mathf.FloorToInt((Event.current.mousePosition.y + BusH * 0.5f) / BusH),
                        0, count);
                }

                for (int s = count - 1; s >= 0; s--)
                {
                    var   bus       = col.Buses[s];
                    int   visualIdx = s;      // Buses[0] = tappable queue head, drawn at the TOP row.
                    float rowTop    = visualIdx * BusH;
                    bool  isHead    = s == 0; // head = top row now.

                    bool colDragging   = _dragCol == c;
                    bool thisIsDragged = colDragging && _dragSrc == visualIdx;

                    if (isHead && Event.current.type == EventType.Repaint)
                        EditorGUI.DrawRect(new Rect(0f, rowTop, contentW, BusH), HeadTint);
                    if (thisIsDragged && Event.current.type == EventType.Repaint)
                        EditorGUI.DrawRect(new Rect(0f, rowTop, contentW, BusH), DragDimTint);

                    // Drag handle
                    var handleRect = new Rect(0f, rowTop, HandleW, BusH);
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
                        float vCenter = rowTop + (BusH - sz) * 0.5f;
                        const float ToggleW = 18f;
                        float toggleX = HandleW + 2f;

                        // Hidden toggle
                        bool newHidden = EditorGUI.Toggle(
                            new Rect(toggleX, vCenter + 1f, ToggleW, ToggleW), bus.Hidden);
                        if (newHidden != bus.Hidden)
                        {
                            session.PushUndoSnapshot();
                            bus.Hidden = newHidden;
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
                        if (palette != null) palette.TryGetColor(bus.ColorId, out swatchColor);
                        EditorGUI.DrawRect(swatchRect, swatchColor);
                        if (bus.Hidden) EditorGUI.DrawRect(swatchRect, HiddenTint);

                        GUI.Label(swatchRect, new GUIContent(string.Empty,
                            $"Right-click to change color\n{bus.ColorId}"));

                        if (Event.current.type == EventType.MouseDown && Event.current.button == 1
                            && swatchHover && palette != null)
                        {
                            var capBus     = bus;
                            var capQueue   = queue;
                            var capSession = session;
                            PopupWindow.Show(
                                swatchRect,
                                new ColorPickerPopup(palette, bus.ColorId, id =>
                                {
                                    capSession.PushUndoSnapshot();
                                    capBus.ColorId = id;
                                    capSession.Document.TopSection = JObject.FromObject(capQueue);
                                    capSession.MarkDirty();
                                }, pickerFilter));
                            Event.current.Use();
                        }

                        // Capacity int field
                        float capX = swatchX + sz + 4f;
                        var capRect = new Rect(capX, vCenter + 1f, CapW, sz - 2f);
                        EditorGUI.BeginChangeCheck();
                        int newCap = EditorGUI.IntField(capRect, bus.Capacity, EditorStyles.miniTextField);
                        if (EditorGUI.EndChangeCheck() && newCap != bus.Capacity)
                        {
                            session.PushUndoSnapshot();
                            bus.Capacity = Mathf.Max(0, newCap);
                            session.MarkDirty();
                        }

                        // ← → buttons: move bus to adjacent column
                        float arrowGrpX = contentW - DelBtnW - ArrowBtnW * 2f - 4f;
                        float arrowBtnY = rowTop + (BusH - 18f) * 0.5f;

                        // Link button sits just left of the ← arrow; the label fills what's left.
                        float linkBtnX = arrowGrpX - LinkBtnW - 4f;

                        // "HEAD" tag (on the head row) or color ID label, if room.
                        float labelX = capX + CapW + 4f;
                        float labelW = linkBtnX - labelX - 4f;
                        if (isHead && labelW > 6f)
                        {
                            var headStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                            {
                                normal = { textColor = Accent }
                            };
                            GUI.Label(new Rect(labelX, vCenter + 2f, Mathf.Min(HeadTagW, labelW), sz),
                                "HEAD", headStyle);
                        }
                        else if (labelW > 6f)
                        {
                            GUI.Label(new Rect(labelX, vCenter + 2f, labelW, sz),
                                bus.ColorId, EditorStyles.miniLabel);
                        }

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

                        // Connect / disconnect link button.
                        DrawLinkButton(new Rect(linkBtnX, arrowBtnY, LinkBtnW, 18f),
                            session, queue, connMembers, c, s, prevBg);
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
                            MoveVisual(col.Buses, _dragSrc, _dragTgt);
                            GUI.changed = true;
                        }
                        _dragCol = -1; _dragSrc = -1; _dragTgt = -1;
                        Event.current.Use();
                    }

                    if (Event.current.type == EventType.Repaint && _dragTgt >= 0)
                    {
                        float lineY = _dragTgt * BusH;
                        EditorGUI.DrawRect(new Rect(0f, lineY - 1f, contentW, 2f), Accent);
                    }
                }

                if (removeIdx >= 0)
                {
                    session.PushUndoSnapshot();
                    col.Buses.RemoveAt(removeIdx);
                }

                GUI.EndScrollView();

                // Bottom row: [← swap col] [+ add bus] [→ swap col]
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
                    string newId = col.Buses.Count > 0
                        ? col.Buses[col.Buses.Count - 1].ColorId
                        : GetFirstGridColor(session)
                          ?? (palette != null ? palette.ColorIds.FirstOrDefault() ?? "blue" : "blue");
                    int newCap = col.Buses.Count > 0 ? col.Buses[col.Buses.Count - 1].Capacity : 6;
                    col.Buses.Add(new BusEntry { ColorId = newId, Capacity = newCap });
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
                var bus = queue.Columns[moveFromCol].Buses[moveFromIdx];
                queue.Columns[moveFromCol].Buses.RemoveAt(moveFromIdx);
                queue.Columns[moveToColIdx].Buses.Add(bus);
                GUI.changed = true;
            }

            // Apply deferred column swap
            if (swapColA >= 0 && swapColA + 1 < columnCount)
            {
                session.PushUndoSnapshot();
                (queue.Columns[swapColA], queue.Columns[swapColA + 1]) =
                    (queue.Columns[swapColA + 1], queue.Columns[swapColA]);
                (_scrollPos[swapColA], _scrollPos[swapColA + 1]) =
                    (_scrollPos[swapColA + 1], _scrollPos[swapColA]);
                GUI.changed = true;
            }

            if (EditorGUI.EndChangeCheck())
                session.Document.TopSection = JObject.FromObject(queue);
        }

        // Draws the per-bus connect/disconnect affordance and handles its click.
        //  - connected bus  → shows its pair number; click disconnects the whole group.
        //  - this bus is the pending anchor → highlighted; click cancels.
        //  - another bus is anchored → "+"; click completes the pair (soft-lock guarded).
        //  - idle            → "🔗"; click anchors this bus.
        private void DrawLinkButton(Rect r, LevelEditorSession session, BusQueueData queue,
            Dictionary<int, List<(int col, int pos)>> members, int c, int s, Color prevBg)
        {
            var bus = queue.Columns[c].Buses[s];
            bool isConnected   = bus.ConnectedId >= 0;
            bool anchorHere    = _pendingAnchor.HasValue && _pendingAnchor.Value.col == c && _pendingAnchor.Value.pos == s;
            bool anchorElse    = _pendingAnchor.HasValue && !anchorHere;

            string caption;
            string tooltip;
            Color? bg = null;
            if (isConnected)
            {
                int n = BusConnection.DisplayNumber(members, bus.ConnectedId);
                caption = n.ToString();
                tooltip = $"Connected pair {n} — click to disconnect";
                bg = LinkConnectedBg;
            }
            else if (anchorHere)
            {
                caption = "●";
                tooltip = "Anchored — click another bus to connect, or click again to cancel";
                bg = LinkAnchorBg;
            }
            else if (anchorElse)
            {
                caption = "+";
                tooltip = "Connect this bus to the anchored bus";
            }
            else
            {
                caption = "🔗";
                tooltip = "Link: start a connection from this bus";
            }

            if (bg.HasValue) GUI.backgroundColor = bg.Value;
            bool clicked = GUI.Button(r, new GUIContent(caption, tooltip), EditorStyles.miniButton);
            GUI.backgroundColor = prevBg;
            if (!clicked) return;

            if (isConnected)
            {
                BusConnection.DisconnectGroup(session, queue, bus.ConnectedId);
                _pendingAnchor = null;
            }
            else if (anchorHere)
            {
                _pendingAnchor = null;
            }
            else if (anchorElse)
            {
                var anchor = _pendingAnchor.Value;
                if (!InRange(queue, anchor.col, anchor.pos))
                {
                    _pendingAnchor = null; // anchor got edited away; restart
                }
                else
                {
                    // Trial on a clone: would connecting anchor↔this soft-lock?
                    var clone = session.Document.TopSection.ToObject<BusQueueData>();
                    int trialId = BusConnection.AllocId(clone);
                    clone.Columns[anchor.col].Buses[anchor.pos].ConnectedId = trialId;
                    clone.Columns[c].Buses[s].ConnectedId = trialId;
                    if (BusConnection.ConnectionsDeadlock(clone))
                    {
                        Debug.LogWarning("[BusBuddies] That connection would create a soft-lock; pick a different bus.");
                    }
                    else
                    {
                        int id = BusConnection.AllocId(queue);
                        BusConnection.ConnectPair(session, queue,
                            queue.Columns[anchor.col].Buses[anchor.pos], queue.Columns[c].Buses[s], id);
                        _pendingAnchor = null;
                    }
                }
            }
            else
            {
                _pendingAnchor = (c, s);
            }
        }

        private static bool InRange(BusQueueData queue, int col, int pos)
            => col >= 0 && col < queue.Columns.Count
               && queue.Columns[col]?.Buses != null
               && pos >= 0 && pos < queue.Columns[col].Buses.Count;

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

        // Moves a bus from visual index vSrc to before visual index vTgt.
        // Visual index 0 = top of column = highest data index (count-1).
        // Visual row order now equals list order (Buses[0] = top). A drag from visual
        // row vSrc to insert-position vTgt maps directly onto the list.
        private static void MoveVisual(List<BusEntry> buses, int vSrc, int vTgt)
        {
            if (vSrc < 0 || vSrc >= buses.Count) return;
            var item = buses[vSrc];
            buses.RemoveAt(vSrc);
            buses.Insert(vSrc < vTgt ? vTgt - 1 : vTgt, item);
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
