using Hoppa.LevelEditor.Core;

namespace Hoppa.LevelEditor.Core.Editor
{
    public interface ILevelExporter
    {
        string Name { get; }

        // jsonFilePath is the absolute path to the .json file being saved.
        // Returns true on success.
        bool Export(LevelDocument document, CellTypeRegistry cellTypes, string jsonFilePath);
    }
}
