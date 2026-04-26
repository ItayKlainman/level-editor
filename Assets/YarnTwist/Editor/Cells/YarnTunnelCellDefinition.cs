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

        private static readonly Color TunnelBg  = new Color(0.14f, 0.22f, 0.32f);
        private static readonly Color ArrowColor = new Color(0.35f, 0.72f, 1.00f);
        private static readonly Color BadgeColor = new Color(0.90f, 0.60f, 0.10f);

        public override ICellData CreateDefault() => new YarnTunnelCell();

        public override void DrawCell(Rect rect, ICellData data)
        {
            EditorGUI.DrawRect(rect, TunnelBg);
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

            var  ids = _palette != null ? new List<string>(_palette.ColorIds) : new List<string>();
            float lh = EditorGUIUtility.singleLineHeight;
            float x  = rect.x;
            float y  = rect.y;

            // Direction row
            GUI.Label(new Rect(x, y, 60f, lh), "Direction", EditorStyles.miniLabel);
            tunnel.OutputDirection = (YarnDirection)EditorGUI.EnumPopup(
                new Rect(x + 62f, y, rect.width - 62f, lh), tunnel.OutputDirection);
            y += lh + 4f;

            // Queue label
            GUI.Label(new Rect(x, y, rect.width, lh), "Queue", EditorStyles.miniLabel);
            y += lh + 2f;

            const float BtnW   = 18f;
            const float BtnGap = 2f;
            float swatchX      = x + BtnW * 2 + BtnGap * 2 + 2f;
            float removeX      = rect.xMax - BtnW;
            float entryH       = ColorSwatchDrawer.Size + 4f;

            int removeAt = -1, swapA = -1, swapB = -1;

            for (int i = 0; i < tunnel.Queue.Count; i++)
            {
                if (y + entryH > rect.yMax) break;

                float by = y + 2f;

                // Reorder buttons
                GUI.enabled = i > 0;
                if (GUI.Button(new Rect(x, by, BtnW, lh), "▲", EditorStyles.miniButton))
                { swapA = i - 1; swapB = i; }
                GUI.enabled = i < tunnel.Queue.Count - 1;
                if (GUI.Button(new Rect(x + BtnW + BtnGap, by, BtnW, lh), "▼", EditorStyles.miniButton))
                { swapA = i; swapB = i + 1; }
                GUI.enabled = true;

                // Color swatch (click to cycle to next color)
                var swRect = new Rect(swatchX, y, ColorSwatchDrawer.Size, ColorSwatchDrawer.Size);
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

                // Color label
                float labelX = swatchX + ColorSwatchDrawer.Size + 4f;
                GUI.Label(new Rect(labelX, by, removeX - labelX - 2f, lh),
                    tunnel.Queue[i], EditorStyles.miniLabel);

                // Remove
                if (GUI.Button(new Rect(removeX, by, BtnW, lh), "×", EditorStyles.miniButton))
                    removeAt = i;

                y += entryH;
            }

            // Deferred mutations
            if (removeAt >= 0) tunnel.Queue.RemoveAt(removeAt);
            if (swapA >= 0)
            {
                var tmp = tunnel.Queue[swapA];
                tunnel.Queue[swapA] = tunnel.Queue[swapB];
                tunnel.Queue[swapB] = tmp;
            }

            // Add button
            if (y + lh <= rect.yMax && ids.Count > 0)
            {
                if (GUI.Button(new Rect(x, y, rect.width, lh), "+ Add color", EditorStyles.miniButton))
                    tunnel.Queue.Add(ids[0]);
            }
        }
    }
}


