using System.IO;
using Hoppa.LevelEditor.Core;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Always-on exporter: writes the canonical .json file to disk.
    // Instantiated directly by the window; not a ScriptableObject.
    public sealed class JsonExporter : ILevelExporter
    {
        public string Name => "JSON";

        public bool Export(LevelDocument document, CellTypeRegistry cellTypes, string jsonFilePath)
        {
            var dir = Path.GetDirectoryName(jsonFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(jsonFilePath, new JsonLevelSerializer().Save(document, cellTypes));
            return true;
        }
    }
}
