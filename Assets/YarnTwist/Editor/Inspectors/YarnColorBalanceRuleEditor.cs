using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CustomEditor(typeof(YarnColorBalanceRule))]
    public sealed class YarnColorBalanceRuleEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Color Balance Rule\n\n" +
                "Ensures every color has exactly as many yarn balls in the grid as spool capacity in the top section.\n\n" +
                "How balls are counted:\n" +
                "  • Box cell          → Balls Per Box  (default: 9)\n" +
                "  • Arrow Box cell    → Balls Per Box  (default: 9)\n" +
                "  • Tunnel queue item → Balls Per Box  (default: 9)\n\n" +
                "How capacity is counted:\n" +
                "  • Each spool in the top section → Balls Per Spool  (default: 3)\n\n" +
                "Pass condition (per color):\n" +
                "  total_balls == total_spool_capacity",
                MessageType.Info);

            EditorGUILayout.Space(4);
            DrawDefaultInspector();
        }
    }
}
