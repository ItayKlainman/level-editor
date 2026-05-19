using System.IO;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YAK.Editor
{
    // Tools-menu entry that imports a YAK game-format LevelConfig JSON, converts it
    // into a framework LevelDocument JSON the editor can open, and hands it to the
    // active LevelEditorWindow. The active GameProfile must be a YAK profile so the
    // cell-type registry resolves yak.wool / yak.empty correctly.
    public static class YAKImportMenu
    {
        private const string LastImportDirPref = "Hoppa.YAK.LastImportDir";
        private const string LastSaveDirPref   = "Hoppa.LevelEditor.LastSaveDir";

        [MenuItem("Tools/Hoppa Level Editor/Import YAK Level…")]
        public static void Import()
        {
            var window = EditorWindow.GetWindow<LevelEditorWindow>("Level Editor");
            var profile = window != null ? window.Profile : null;
            if (profile == null)
            {
                EditorUtility.DisplayDialog("No Profile",
                    "Open the Level Editor window and select a YAK Game Profile first.", "OK");
                return;
            }

            var colorSource = FindColorSourceFor(profile);
            if (colorSource == null)
            {
                EditorUtility.DisplayDialog("Color Source Missing",
                    "The active profile's YAKLevelExporter has no Color Source assigned. Set one (a YAKStaticManagerColorSource asset) and retry.", "OK");
                return;
            }

            string startDir = EditorPrefs.GetString(LastImportDirPref, Application.dataPath);
            string srcPath  = EditorUtility.OpenFilePanel("Import YAK Level", startDir, "json");
            if (string.IsNullOrEmpty(srcPath)) return;
            EditorPrefs.SetString(LastImportDirPref, Path.GetDirectoryName(srcPath));

            LevelDocument doc;
            try
            {
                doc = YAKLevelImporter.Import(srcPath, colorSource, profile.SchemaId);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Import Failed", ex.Message, "OK");
                return;
            }

            // Ask where to save the editor's working file (LevelDocument JSON).
            string saveDir  = EditorPrefs.GetString(LastSaveDirPref, Application.dataPath);
            string defName  = Path.GetFileNameWithoutExtension(srcPath) + ".json";
            string savePath = EditorUtility.SaveFilePanel(
                "Save Imported Level As (LevelDocument JSON)", saveDir, defName, "json");
            if (string.IsNullOrEmpty(savePath)) return;
            EditorPrefs.SetString(LastSaveDirPref, Path.GetDirectoryName(savePath));

            try
            {
                var json = new JsonLevelSerializer().Save(doc, profile.BuildRegistry());
                File.WriteAllText(savePath, json);
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("Save Failed", ex.Message, "OK");
                return;
            }

            AssetDatabase.Refresh();
            window.OpenLevelFile(savePath);
            window.Focus();
        }

        // Locates the YAKLevelExporter on the active profile and returns its
        // assigned color source (needed to translate ints back to colorIds).
        private static YAKStaticManagerColorSource FindColorSourceFor(GameProfile profile)
        {
            foreach (var exporter in profile.Exporters)
            {
                if (exporter is YAKLevelExporter yak)
                {
                    var so   = new SerializedObject(yak);
                    var prop = so.FindProperty("_colorSource");
                    return prop?.objectReferenceValue as YAKStaticManagerColorSource;
                }
            }
            return null;
        }
    }
}
