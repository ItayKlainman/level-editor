using Hoppa.LevelEditor.Core;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Layer 2 generator contract.
    //
    // A game-specific generator is a ScriptableObject that implements this
    // interface; the asset is referenced from the GameProfile's _levelGenerator
    // field. The Layer 1 GeneratorModePanel hands the operator's request to
    // Generate() and renders whatever LevelDocument comes back.
    //
    // The generator owns its reroll loop. Use LevelGeneratorRunner to evaluate
    // candidates against the profile's existing validation rules so every game
    // produces consistent rejection diagnostics.
    public interface ILevelGenerator
    {
        LevelGeneratorResult Generate(LevelGeneratorRequest request, GameProfile profile);
    }
}
