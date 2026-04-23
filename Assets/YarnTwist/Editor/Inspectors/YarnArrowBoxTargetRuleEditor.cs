using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CustomEditor(typeof(YarnArrowBoxTargetRule))]
    public sealed class YarnArrowBoxTargetRuleEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Arrow Box Target Rule\n\n" +
                "Every Arrow Box cell must point to a reachable, non-blocked cell.\n\n" +
                "What is checked:\n" +
                "  • ERROR   — target cell is outside the grid bounds\n" +
                "  • ERROR   — target cell is a Wall (yarn can never exit)\n\n" +
                "This rule has no configurable parameters.",
                MessageType.Info);

            EditorGUILayout.Space(4);
            DrawDefaultInspector();
        }
    }
}
