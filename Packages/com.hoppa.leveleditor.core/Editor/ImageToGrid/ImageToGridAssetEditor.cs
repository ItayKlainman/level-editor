using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Shared inspector for every ImageToGridAsset subclass. Renders the default
    // fields, then a friendly ColorRemaps editor: a ColorField source (Unity's
    // built-in swatch + screen eyedropper), a palette-id dropdown target, and a
    // reach slider. The active palette is published by ImageToGridModePanel.
    [CustomEditor(typeof(ImageToGridAsset), editorForChildClasses: true)]
    public sealed class ImageToGridAssetEditor : UnityEditor.Editor
    {
        // Set by the Image panel each frame before drawing; null when inspected elsewhere.
        public static ColorPaletteAsset ActivePalette;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Everything except the remap list via the default drawer.
            DrawPropertiesExcluding(serializedObject, "m_Script", "ColorRemaps");

            var remaps = serializedObject.FindProperty("ColorRemaps");
            if (remaps != null)
                DrawRemaps(remaps);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRemaps(SerializedProperty remaps)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Color Remap", EditorStyles.boldLabel);

            string[] ids = ActivePalette?.ColorIds?.ToArray();

            for (int i = 0; i < remaps.arraySize; i++)
            {
                var entry   = remaps.GetArrayElementAtIndex(i);
                var source  = entry.FindPropertyRelative("Source");
                var target  = entry.FindPropertyRelative("TargetColorId");
                var reach   = entry.FindPropertyRelative("Reach");

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(source, new GUIContent("Source"));
                        if (GUILayout.Button("✕", GUILayout.Width(22)))
                        {
                            remaps.DeleteArrayElementAtIndex(i);
                            break;
                        }
                    }

                    if (ids != null && ids.Length > 0)
                    {
                        int cur = Mathf.Max(0, System.Array.IndexOf(ids, target.stringValue));
                        int next = EditorGUILayout.Popup("Target", cur, ids);
                        target.stringValue = ids[next];
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(target, new GUIContent("Target Color Id"));
                    }

                    EditorGUILayout.Slider(reach, 0f, 1f, new GUIContent("Reach"));
                }
            }

            if (GUILayout.Button("+ Add remap"))
                remaps.InsertArrayElementAtIndex(remaps.arraySize);
        }
    }
}
