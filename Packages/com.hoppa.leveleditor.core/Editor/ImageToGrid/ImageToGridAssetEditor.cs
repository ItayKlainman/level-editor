using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Shared inspector for every ImageToGridAsset subclass, drawn by the 🖼 Image
    // panel. Draws tooltipped section headers (Unity's [Header] can't carry a
    // tooltip), renders OutlineColorId + remap Target as palette dropdowns and
    // BackgroundNeutrals as a palette checklist, and a friendly ColorRemaps editor
    // (ColorField source with the built-in eyedropper + Reach slider). The active
    // palette is published by ImageToGridModePanel.
    [CustomEditor(typeof(ImageToGridAsset), editorForChildClasses: true)]
    public sealed class ImageToGridAssetEditor : UnityEditor.Editor
    {
        // Set by the Image panel each frame before drawing; null when inspected elsewhere.
        public static ColorPaletteAsset ActivePalette;

        // Measured bottom (yMax) of the last drawn inspector, published so the host
        // panel can size its embedded area to fit. 0 until the first repaint.
        public static float LastContentHeight;

        // Property name → tooltipped section header, drawn just before that property.
        private static readonly Dictionary<string, GUIContent> SectionHeaders = new Dictionary<string, GUIContent>
        {
            ["Sampling"]           = new GUIContent("Sampling",      "How each grid square picks its color from the source image."),
            ["ColorCap"]           = new GUIContent("Colors",        "How many distinct colors the finished grid may use."),
            ["Background"]         = new GUIContent("Background",     "What the area around the subject becomes — a neutral fill or empty cells."),
            ["OutlineSubject"]     = new GUIContent("Outline",       "An optional dark silhouette ring drawn around the subject."),
            ["Segmentation"]       = new GUIContent("Segmentation",  "How the tool decides which pixels are the subject vs the background."),
            ["DefaultActiveSlots"] = new GUIContent("Active Bus Row","How many active-row slots the new level starts with."),
        };

        private static readonly GUIContent RemapHeader = new GUIContent(
            "Color Remap",
            "Force a color from the source image to become a specific palette color — eyedrop the color off the source image, then raise Reach until it catches.");

        private bool _neutralsFoldout;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            string[] ids = ActivePalette?.ColorIds?.ToArray();

            var prop = serializedObject.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.name == "m_Script" || prop.name == "ColorRemaps") continue;

                if (SectionHeaders.TryGetValue(prop.name, out var header))
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
                }

                switch (prop.name)
                {
                    case "OutlineColorId":
                        DrawPaletteIdDropdown(prop, ids,
                            new GUIContent("Outline Color", "Palette color used for the outline ring."));
                        break;
                    case "BackgroundNeutrals":
                        DrawNeutralToggles(prop, ids);
                        break;
                    default:
                        EditorGUILayout.PropertyField(prop, true);
                        break;
                }
            }

            var remaps = serializedObject.FindProperty("ColorRemaps");
            if (remaps != null)
                DrawRemaps(remaps, ids);

            serializedObject.ApplyModifiedProperties();

            // Publish the real bottom of the drawn inspector so the host panel can
            // size its embedded area to fit (avoids clipping the buttons below).
            if (Event.current.type == EventType.Repaint)
                LastContentHeight = GUILayoutUtility.GetLastRect().yMax;
        }

        // Draw a string property as a dropdown of palette ids. A stored value that
        // isn't in the palette is shown as an explicit "(not in palette)" option
        // and is NOT silently overwritten; a brand-new empty value seeds the first
        // palette id. Falls back to a text field when no palette is available.
        private static void DrawPaletteIdDropdown(SerializedProperty prop, string[] ids, GUIContent label)
        {
            if (ids == null || ids.Length == 0)
            {
                EditorGUILayout.PropertyField(prop, label);
                return;
            }

            string cur = prop.stringValue;
            int inIdx = System.Array.IndexOf(ids, cur);

            string[] options;
            int selected;
            if (inIdx >= 0)                       { options = ids; selected = inIdx; }
            else if (!string.IsNullOrEmpty(cur))  { options = ids.Concat(new[] { cur + "  (not in palette)" }).ToArray(); selected = options.Length - 1; }
            else                                  { options = ids; selected = 0; }

            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup(label, selected, options);
            if (EditorGUI.EndChangeCheck())
            {
                if (next < ids.Length) prop.stringValue = ids[next]; // ignore a pick of the stray "(not in palette)" row
            }
            else if (inIdx < 0 && string.IsNullOrEmpty(cur))
            {
                prop.stringValue = ids[0]; // seed a real default for a brand-new empty entry
            }
        }

        // Background neutrals as a checklist over the palette colors (plus any current
        // entries not in the palette, so nothing already set is silently dropped).
        private void DrawNeutralToggles(SerializedProperty arr, string[] ids)
        {
            var current = new List<string>();
            for (int i = 0; i < arr.arraySize; i++)
                current.Add(arr.GetArrayElementAtIndex(i).stringValue);
            var currentSet = new HashSet<string>(current);

            var candidates = new List<string>();
            if (ids != null) candidates.AddRange(ids);
            foreach (var c in current)
                if (!string.IsNullOrEmpty(c) && !candidates.Contains(c)) candidates.Add(c);

            _neutralsFoldout = EditorGUILayout.Foldout(_neutralsFoldout,
                new GUIContent("Background Neutrals",
                    "Which palette colors may fill the background (the most contrasting one is chosen)."),
                true);
            if (!_neutralsFoldout) return;

            if (candidates.Count == 0)
            {
                EditorGUILayout.HelpBox("No palette available.", MessageType.None);
                return;
            }

            EditorGUI.indentLevel++;
            var chosen = new List<string>();
            foreach (var id in candidates)
            {
                bool inPalette = ids != null && System.Array.IndexOf(ids, id) >= 0;
                var label = inPalette ? new GUIContent(id) : new GUIContent(id + "  (not in palette)");
                if (EditorGUILayout.ToggleLeft(label, currentSet.Contains(id))) chosen.Add(id);
            }
            EditorGUI.indentLevel--;

            // Rebuild only when the checked SET actually changed (no spurious reorder-dirtying).
            if (!new HashSet<string>(chosen).SetEquals(currentSet))
            {
                arr.ClearArray();
                for (int i = 0; i < chosen.Count; i++)
                {
                    arr.InsertArrayElementAtIndex(arr.arraySize);
                    arr.GetArrayElementAtIndex(arr.arraySize - 1).stringValue = chosen[i];
                }
            }
        }

        private void DrawRemaps(SerializedProperty remaps, string[] ids)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(RemapHeader, EditorStyles.boldLabel);

            for (int i = 0; i < remaps.arraySize; i++)
            {
                var entry  = remaps.GetArrayElementAtIndex(i);
                var source = entry.FindPropertyRelative("Source");
                var target = entry.FindPropertyRelative("TargetColorId");
                var reach  = entry.FindPropertyRelative("Reach");

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(source,
                            new GUIContent("Source", "The color in the source image to replace — use the eyedropper on the swatch."));
                        if (GUILayout.Button("✕", GUILayout.Width(22)))
                        {
                            remaps.DeleteArrayElementAtIndex(i);
                            break;
                        }
                    }

                    DrawPaletteIdDropdown(target, ids,
                        new GUIContent("Target", "The palette color the source color is forced to."));

                    EditorGUILayout.Slider(reach, 0f, 1f,
                        new GUIContent("Reach", "0 = exact color only; higher catches more nearby shades."));
                }
            }

            if (GUILayout.Button("+ Add remap"))
            {
                int newIndex = remaps.arraySize;
                remaps.InsertArrayElementAtIndex(newIndex);
                var added = remaps.GetArrayElementAtIndex(newIndex);
                added.FindPropertyRelative("Source").colorValue        = Color.white;
                added.FindPropertyRelative("TargetColorId").stringValue = "";
                added.FindPropertyRelative("Reach").floatValue         = 0.2f;
            }
        }
    }
}
