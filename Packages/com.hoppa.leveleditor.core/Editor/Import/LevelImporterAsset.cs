using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Layer-1 counterpart of LevelExporterAsset: lets a game provide a reader for a
    // foreign level format (e.g. its shipped game schema) so the editor's Open can
    // load it directly. A profile lists its importers in GameProfile._importers.
    //
    // Open flow (LevelEditorWindow.LoadFromPath): the window sniffs the file with each
    // importer's CanImport; the first that claims it produces the LevelDocument and the
    // session is flagged foreign-format so Save round-trips through the matching exporter
    // instead of stamping the editor's internal format onto the game file.
    public abstract class LevelImporterAsset : ScriptableObject
    {
        // Human-readable name (for logs / future UI).
        public abstract string Name { get; }

        // Cheap schema sniff: does this importer recognize the given JSON? Must not throw
        // on unrelated JSON — return false instead.
        public abstract bool CanImport(string json);

        // Parse the foreign JSON into a LevelDocument. Only called when CanImport returned
        // true. The active cell-type registry is provided for importers that need it;
        // simple importers that construct cells directly may ignore it.
        public abstract LevelDocument Import(string json, CellTypeRegistry registry);
    }
}
