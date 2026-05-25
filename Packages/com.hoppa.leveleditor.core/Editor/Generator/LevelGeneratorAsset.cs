using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Abstract ScriptableObject base for Layer 2 level generators.
    //
    // Mirrors the LevelExporterAsset / EditorPanelAsset pattern: the abstract
    // SO base exists so GameProfile's serialized field can type-filter the
    // ObjectField to subclasses only. The interface (ILevelGenerator) is the
    // shape the rest of Layer 1 reads.
    public abstract class LevelGeneratorAsset : ScriptableObject, ILevelGenerator
    {
        public abstract LevelGeneratorResult Generate(LevelGeneratorRequest request, GameProfile profile);
    }
}
