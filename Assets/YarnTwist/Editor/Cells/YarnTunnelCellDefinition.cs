using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Cells/Tunnel")]
    public sealed class YarnTunnelCellDefinition : CellTypeDefinition
    {
        [SerializeField] private ColorPaletteAsset _palette;

        private static readonly Color TunnelBg     = new Color(0.04f, 0.07f, 0.14f);
        private static readonly Color TunnelAccent  = new Color(0.35f, 0.72f, 1.00f, 0.70f);
        private static readonly Color ArrowColor    = new Color(0.35f, 0.72f, 1.00f);
        private static readonly Color BadgeColor    = new Color(0.90f, 0.60f, 0.10f);

        private Vector2        _queueScroll;
        private bool           _pendingSave;
        private YarnDirection  _inspectorDirection = YarnDirection.Up;

        public override float InspectorPreferredHeight => 150f;

        public override ICellData CreateDefault() => new YarnTunnelCell();

        public override void OnAfterPlaced(int x, int y, LevelEditorSession session)
        {
            if (session.Document.Grid.Get(x, y) is not YarnTunnelCell tunnel) return;

            var (nx, ny) = NeighborOf(tunnel.OutputDirection, x, y);
            var grid     = session.Document.Grid;

            if (!grid.InBounds(nx, ny)) return;

            var existing = grid.Get(nx, ny);
            tunnel.DisplacedCell = existing is not YarnEmptyCell ? existing : null;
            session.SetCell(nx, ny, new YarnEmptyCell());
        }

        public override void OnAfterInspectorChanged(int x, int y, LevelEditorSession session)
        {
            if (session.Document.Grid.Get(x, y) is not YarnTunnelCell tunnel) return;
            if (tunnel.OutputDirection == _inspectorDirection) return;

            var grid = session.Document.Grid;
            var (ox, oy) = NeighborOf(_inspectorDirection,      x, y);
            var (nx, ny) = NeighborOf(tunnel.OutputDirection,   x, y);

            // Restore old neighbor
            if (grid.InBounds(ox, oy))
            {
                var restore = tunnel.DisplacedCell ?? FallbackFill(session);
                session.SetCell(ox, oy, restore);
            }

            // Clear new neighbor, capturing what was there
            if (grid.InBounds(nx, ny))
            {
                var displaced = grid.Get(nx, ny);
                tunnel.DisplacedCell = displaced is not YarnEmptyCell ? displaced : null;
                session.SetCell(nx, ny, new YarnEmptyCell());
            }
            else
            {
                tunnel.DisplacedCell = null;
            }
        }

        private static (int x, int y) NeighborOf(YarnDirection dir, int x, int y) => dir switch
        {
            YarnDirection.Up    => (x, y + 1),
            YarnDirection.Down  => (x, y - 1),
            YarnDirection.Left  => (x - 1, y),
            YarnDirection.Right => (x + 1, y),
            _                   => (x, y)
        };

        private static ICellData FallbackFill(LevelEditorSession session)
        {
            var types = session.Profile.CellTypes;
            return (types.Count > 1 ? types[1] : types.Count > 0 ? types[0] : null)?.CreateDefault();
        }

        public override void DrawCell(Rect rect, ICellData data)
        {
            EditorGUI.DrawRect(rect, TunnelBg);
            DrawFrame(rect, TunnelAccent);

            if (data is not YarnTunnelCell tunnel) return;

            string arrow = tunnel.OutputDirection switch
            {
                YarnDirection.Up    => "↑", YarnDirection.Down  => "↓",
                YarnDirection.Left  => "←", YarnDirection.Right => "→",
                _ => "?"
            };
            GUI.Label(rect, arrow, new GUIStyle(EditorStyles.boldLabel)
                { alignment = TextAnchor.MiddleCenter, fontSize = 12, normal = { textColor = ArrowColor } });

            if (tunnel.Queue.Count > 0)
            {
                var badge = new Rect(rect.xMax - 14f, rect.y + 2f, 12f, 12f);
                EditorGUI.DrawRect(badge, BadgeColor);
                GUI.Label(badge, tunnel.Queue.Count.ToString(), new GUIStyle(EditorStyles.miniLabel)
                    { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.black } });

                if (Event.current.type == EventType.Repaint
                    && rect.Contains(Event.current.mousePosition))
                    DrawQueueTooltip(rect, tunnel);
            }
        }

        // All rendering uses absolute rects — no GUILayout — to prevent layout leaks.
        public override void DrawInspector(Rect rect, ref ICellData data)
        {
            if (data is not YarnTunnelCell tunnel) return;

            // Flush pending save triggered by ColorPickerPopup callback (fires in a different OnGUI frame)
            if (_pendingSave)
            {
                GUI.changed  = true;
                _pendingSave = false;
            }

            // Capture window-local mouse position before the scroll view transforms it
            var windowMouse = Event.current.mousePosition;

            var   ids = _palette != null ? new List<string>(_palette.ColorIds) : new List<string>();
            float lh  = EditorGUIUtility.singleLineHeight;
            float x   = rect.x;
            float y   = rect.y;

            // Direction row
            GUI.Label(new Rect(x, y, 60f, lh), "Direction", EditorStyles.miniLabel);
            _inspectorDirection = tunnel.OutputDirection; // snapshot before EnumPopup may change it
            tunnel.OutputDirection = (YarnDirection)EditorGUI.EnumPopup(
                new Rect(x + 62f, y, rect.width - 62f, lh), tunnel.OutputDirection);
            y += lh + 4f;

            // Queue header
            GUI.Label(new Rect(x, y, rect.width, lh), "Queue", EditorStyles.miniLabel);
            y += lh + 2f;

            // Add button pinned to the bottom of the rect
            float addBtnH  = ids.Count > 0 ? lh + 2f : 0f;
            float listH    = Mathf.Max(0f, rect.yMax - y - addBtnH);
            float innerW   = rect.width - 16f; // reserve width for potential scrollbar

            const float BtnW   = 18f;
            const float BtnGap = 2f;
            float entryH   = ColorSwatchDrawer.Size + 4f;
            float swatchX  = BtnW * 2 + BtnGap * 2 + 2f;
            float removeX  = innerW - BtnW;
            float contentH = Mathf.Max(tunnel.Queue.Count * entryH, listH);

            var scrollViewRect = new Rect(x, y, rect.width, listH);
            var contentRect    = new Rect(0f, 0f, innerW, contentH);

            _queueScroll = GUI.BeginScrollView(scrollViewRect, _queueScroll, contentRect,
                alwaysShowHorizontal: false, alwaysShowVertical: false);

            int removeAt = -1, swapA = -1, swapB = -1;
            float ey = 0f;

            for (int i = 0; i < tunnel.Queue.Count; i++)
            {
                float by = ey + 2f;

                GUI.enabled = i > 0;
                if (GUI.Button(new Rect(0f, by, BtnW, lh), "▲", EditorStyles.miniButton))
                { swapA = i - 1; swapB = i; }
                GUI.enabled = i < tunnel.Queue.Count - 1;
                if (GUI.Button(new Rect(BtnW + BtnGap, by, BtnW, lh), "▼", EditorStyles.miniButton))
                { swapA = i; swapB = i + 1; }
                GUI.enabled = true;

                var swRect = new Rect(swatchX, ey, ColorSwatchDrawer.Size, ColorSwatchDrawer.Size);
                if (_palette != null && _palette.TryGetColor(tunnel.Queue[i], out var ec))
                    EditorGUI.DrawRect(swRect, ec);
                else
                    EditorGUI.DrawRect(swRect, Color.grey);

                GUI.Label(swRect, new GUIContent(string.Empty, $"Right-click to change color\n{tunnel.Queue[i]}"));
                if (Event.current.type == EventType.MouseDown && Event.current.button == 1
                    && swRect.Contains(Event.current.mousePosition) && _palette != null)
                {
                    var capturedTunnel = tunnel;
                    var capturedI      = i;
                    PopupWindow.Show(
                        new Rect(windowMouse.x, windowMouse.y, 1f, 1f),
                        new ColorPickerPopup(_palette, tunnel.Queue[i], id =>
                        {
                            capturedTunnel.Queue[capturedI] = id;
                            _pendingSave = true;
                        }));
                    Event.current.Use();
                }

                float labelX = swatchX + ColorSwatchDrawer.Size + 4f;
                GUI.Label(new Rect(labelX, by, removeX - labelX - 2f, lh),
                    tunnel.Queue[i], EditorStyles.miniLabel);

                if (GUI.Button(new Rect(removeX, by, BtnW, lh), "×", EditorStyles.miniButton))
                    removeAt = i;

                ey += entryH;
            }

            if (removeAt >= 0) tunnel.Queue.RemoveAt(removeAt);
            if (swapA >= 0)
            {
                var tmp = tunnel.Queue[swapA];
                tunnel.Queue[swapA] = tunnel.Queue[swapB];
                tunnel.Queue[swapB] = tmp;
            }

            GUI.EndScrollView();

            // Add button always visible at the bottom
            if (ids.Count > 0)
            {
                float addY = rect.yMax - lh - 2f;
                if (GUI.Button(new Rect(x, addY, rect.width, lh), "+ Add color", EditorStyles.miniButton))
                    tunnel.Queue.Add(ids[0]);
            }
        }

        private void DrawQueueTooltip(Rect anchor, YarnTunnelCell tunnel)
        {
            const float PadX     = 6f;
            const float PadY     = 5f;
            const float SwatchSz = 10f;
            const float HeaderH  = 15f;
            const float RowH     = 14f;

            float boxW = 120f;
            float boxH = PadY + HeaderH + tunnel.Queue.Count * RowH + PadY;
            var   box  = new Rect(anchor.x, anchor.yMax + 3f, boxW, boxH);

            EditorGUI.DrawRect(box, new Color(0.08f, 0.09f, 0.12f, 0.97f));
            DrawFrame(box, new Color(0.35f, 0.40f, 0.55f, 0.90f));

            float y = box.y + PadY;
            GUI.Label(new Rect(box.x + PadX, y, boxW - PadX * 2f, HeaderH),
                $"Queue ({tunnel.Queue.Count}):",
                new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold });
            y += HeaderH;

            foreach (var colorId in tunnel.Queue)
            {
                Color c = Color.grey;
                _palette?.TryGetColor(colorId, out c);
                float sy = y + (RowH - SwatchSz) * 0.5f;
                EditorGUI.DrawRect(new Rect(box.x + PadX - 1f, sy - 1f, SwatchSz + 2f, SwatchSz + 2f),
                    new Color(0f, 0f, 0f, 0.55f));
                EditorGUI.DrawRect(new Rect(box.x + PadX, sy, SwatchSz, SwatchSz), c);
                GUI.Label(new Rect(box.x + PadX + SwatchSz + 4f, y, boxW - PadX - SwatchSz - 8f, RowH),
                    colorId, EditorStyles.miniLabel);
                y += RowH;
            }
        }

        private static void DrawFrame(Rect r, Color c, float t = 1f)
        {
            EditorGUI.DrawRect(new Rect(r.x,        r.y,        r.width, t),  c);
            EditorGUI.DrawRect(new Rect(r.x,        r.yMax - t, r.width, t),  c);
            EditorGUI.DrawRect(new Rect(r.x,        r.y,        t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y,        t, r.height), c);
        }
    }
}
