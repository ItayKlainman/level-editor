using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CustomEditor(typeof(YarnTunnelOutputRule))]
    public sealed class YarnTunnelOutputRuleEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Tunnel Output Rule\n\n" +
                "Every Tunnel cell is validated for queue content and output reachability.\n\n" +
                "What is checked:\n" +
                "  • WARNING — queue is empty (tunnel exists but does nothing)\n" +
                "  • ERROR   — tunnel on a grid border points outward (off the grid)\n" +
                "  • ERROR   — output cell is not Empty (must be Empty to let items exit)\n\n" +
                "This rule has no configurable parameters.",
                MessageType.Info);

            EditorGUILayout.Space(4);
            DrawDefaultInspector();
        }
    }
}
