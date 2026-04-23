using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CustomEditor(typeof(YarnSortExporter))]
    public sealed class YarnSortExporterEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Yarn Sort Exporter\n\n" +
                "Runs automatically every time you Save a level from the Level Editor window.\n\n" +
                "What it does:\n" +
                "  1. Takes the current level document (JSON)\n" +
                "  2. Creates or updates a YarnLevelAsset (.asset) next to the .json file\n" +
                "  3. The .asset can be referenced via AssetReference in the game — no file paths needed at runtime\n\n" +
                "Requirement: the .json save path must be inside the Assets/ folder.",
                MessageType.Info);

            EditorGUILayout.Space(4);
            DrawDefaultInspector();
        }
    }
}
