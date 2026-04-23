using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    [CustomEditor(typeof(ColorPaletteAsset))]
    public sealed class ColorPaletteEditor : UnityEditor.Editor
    {
        private SerializedProperty _entries;

        private void OnEnable() => _entries = serializedObject.FindProperty("_entries");

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(_entries, new GUIContent("Color Entries"), includeChildren: true);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
