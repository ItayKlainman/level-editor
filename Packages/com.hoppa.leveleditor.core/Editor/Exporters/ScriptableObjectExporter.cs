using System.IO;
using Hoppa.LevelEditor.Core;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Abstract SO exporter. Subclass once per game to specify the concrete LevelAsset type.
    // The resulting .asset is created next to the .json file (same name, .asset extension).
    // Only works when the JSON is saved inside the project's Assets folder.
    public abstract class ScriptableObjectExporter : ScriptableObject, ILevelExporter
    {
        public string Name => "ScriptableObject";

        protected abstract LevelAsset CreateLevelAssetInstance();

        public bool Export(LevelDocument document, CellTypeRegistry cellTypes, string jsonFilePath)
        {
            string dataPath   = Application.dataPath.Replace('\\', '/');
            string normalJson = jsonFilePath.Replace('\\', '/');

            if (!normalJson.StartsWith(dataPath))
            {
                Debug.LogWarning($"[ScriptableObjectExporter] '{jsonFilePath}' is outside Assets/ — skipping .asset export.");
                return false;
            }

            string relJson  = "Assets" + normalJson.Substring(dataPath.Length);
            string relAsset = Path.ChangeExtension(relJson, ".asset").Replace('\\', '/');
            string json     = new JsonLevelSerializer().Save(document, cellTypes);

            var asset = AssetDatabase.LoadAssetAtPath<LevelAsset>(relAsset);
            if (asset == null)
            {
                asset = CreateLevelAssetInstance();
                asset.ApplyJson(json);
                AssetDatabase.CreateAsset(asset, relAsset);
            }
            else
            {
                asset.ApplyJson(json);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }

            return true;
        }
    }
}
