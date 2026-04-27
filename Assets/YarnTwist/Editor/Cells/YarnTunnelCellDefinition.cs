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

        private Vector2 _queueScroll;

        public override float InspectorPreferredHeight => 150f;

        public override ICellData CreateDefault() => new YarnTunnelCell();

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
            }
        }

        // All rendering uses absolute rects — no GUILayout — to prevent layout leaks.
        public override void DrawInspector(Rect rect, ref ICellData data)
        {
            if (data is not YarnTunnelCell tunnel) return;

            var   ids = _palette != null ? new List<string>(_palette.ColorIds) : new List<string>();
            float lh  = EditorGUIUtility.singleLineHeight;
            float x   = rect.x;
            float y   = rect.y;

            // Direction row
            GUI.Label(new Rect(x, y, 60f, lh), "Direction", EditorStyles.miniLabel);
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

                string tooltip = tunnel.Queue[i];
                GUI.Label(swRect, new GUIContent(string.Empty, $"Click to cycle color\n{tooltip}"));
                if (Event.current.type == EventType.MouseDown && swRect.Contains(Event.current.mousePosition)
                    && ids.Count > 0)
                {
                    int ci = ids.IndexOf(tunnel.Queue[i]);
                    tunnel.Queue[i] = ids[(ci + 1) % ids.Count];
                    Event.current.Use();
                    GUI.changed = true;
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

        private static void DrawFrame(Rect r, Color c, float t = 1f)
        {
            EditorGUI.DrawRect(new Rect(r.x,        r.y,        r.width, t),  c);
            EditorGUI.DrawRect(new Rect(r.x,        r.yMax - t, r.width, t),  c);
            EditorGUI.DrawRect(new Rect(r.x,        r.y,        t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y,        t, r.height), c);
        }
    }
}
