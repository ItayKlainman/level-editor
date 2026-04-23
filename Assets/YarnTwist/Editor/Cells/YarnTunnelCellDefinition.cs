using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Cells/Tunnel")]
    public sealed class YarnTunnelCellDefinition : CellTypeDefinition
    {
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
            tunnel.OutputDirection = (YarnDirection)EditorGUI.EnumPopup(rect, tunnel.OutputDirection);
        }
    }
}
