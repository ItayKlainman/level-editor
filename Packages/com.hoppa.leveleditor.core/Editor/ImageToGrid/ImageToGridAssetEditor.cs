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

        // Measured bottom (yMax) of the last drawn inspector, published so the host
        // panel can size its embedded area to fit. 0 until the first repaint.
        public static float LastContentHeight;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Everything except the remap list via the default drawer.
            DrawPropertiesExcluding(serializedObject, "m_Script", "ColorRemaps");

            var remaps = serializedObject.FindProperty("ColorRemaps");
            if (remaps != null)
                DrawRemaps(remaps);

            serializedObject.ApplyModifiedProperties();

            // Publish the real bottom of the drawn inspector so the host panel can
            // size its embedded area to fit (avoids clipping the buttons below).
            if (Event.current.type == EventType.Repaint)
                LastContentHeight = GUILayoutUtility.GetLastRect().yMax;
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
                        int idx = System.Array.IndexOf(ids, target.stringValue);
                        EditorGUI.BeginChangeCheck();
                        int next = EditorGUILayout.Popup("Target", Mathf.Max(0, idx), ids);
                        if (EditorGUI.EndChangeCheck())
                            target.stringValue = ids[next];
                        else if (idx < 0 && string.IsNullOrEmpty(target.stringValue))
                            target.stringValue = ids[next];        // new entry: seed a real default once
                        if (idx < 0 && !string.IsNullOrEmpty(target.stringValue))
                            EditorGUILayout.HelpBox($"Target '{target.stringValue}' is not in the current palette.", MessageType.Warning);
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(target, new GUIContent("Target Color Id"));
                    }

                    EditorGUILayout.Slider(reach, 0f, 1f, new GUIContent("Reach"));
                }
            }

            if (GUILayout.Button("+ Add remap"))
            {
                int newIndex = remaps.arraySize;
                remaps.InsertArrayElementAtIndex(newIndex);
                var added = remaps.GetArrayElementAtIndex(newIndex);
                added.FindPropertyRelative("Source").colorValue        = Color.white;
                added.FindPropertyRelative("TargetColorId").stringValue = "";
                added.FindPropertyRelative("Reach").floatValue         = 0.15f;
            }
        }
    }
}
