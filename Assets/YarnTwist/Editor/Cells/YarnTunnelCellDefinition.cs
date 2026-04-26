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

        public override void DrawInspector(Rect rect, ref ICellData data)
        {
            if (data is not YarnTunnelCell tunnel) return;

            var ids = _palette != null ? new List<string>(_palette.ColorIds) : new List<string>();
            string[] idsArray = ids.Count > 0 ? ids.ToArray() : System.Array.Empty<string>();

            GUILayout.BeginArea(rect);

            // Direction
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Direction", GUILayout.Width(60f));
            tunnel.OutputDirection = (YarnDirection)EditorGUILayout.EnumPopup(tunnel.OutputDirection);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Queue", EditorStyles.miniLabel);

            // Queue entries
            int removeAt = -1;
            int swapA    = -1;
            int swapB    = -1;

            for (int i = 0; i < tunnel.Queue.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                GUI.enabled = i > 0;
                if (GUILayout.Button("▲", GUILayout.Width(20f))) { swapA = i - 1; swapB = i; }
                GUI.enabled = i < tunnel.Queue.Count - 1;
                if (GUILayout.Button("▼", GUILayout.Width(20f))) { swapA = i; swapB = i + 1; }
                GUI.enabled = true;

                if (idsArray.Length > 0)
                {
                    int idx    = Mathf.Max(0, ids.IndexOf(tunnel.Queue[i]));
                    int newIdx = EditorGUILayout.Popup(idx, idsArray);
                    tunnel.Queue[i] = ids[newIdx];
                }
                else
                {
                    EditorGUILayout.LabelField(tunnel.Queue[i]);
                }

                if (GUILayout.Button("×", GUILayout.Width(20f))) removeAt = i;

                EditorGUILayout.EndHorizontal();
            }

            // Apply deferred list mutations
            if (removeAt >= 0) tunnel.Queue.RemoveAt(removeAt);
            if (swapA >= 0)
            {
                var tmp = tunnel.Queue[swapA];
                tunnel.Queue[swapA] = tunnel.Queue[swapB];
                tunnel.Queue[swapB] = tmp;
            }

            if (idsArray.Length > 0 && GUILayout.Button("+ Add color", EditorStyles.miniButton))
                tunnel.Queue.Add(ids[0]);

            GUILayout.EndArea();
        }
    }
}

