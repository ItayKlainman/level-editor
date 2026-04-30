using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public abstract class LevelExporterAsset : ScriptableObject, ILevelExporter
    {
        public abstract string Name { get; }
        public abstract bool Export(LevelDocument document, CellTypeRegistry cellTypes, string jsonFilePath);
    }
}
