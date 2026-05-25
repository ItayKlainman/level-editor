using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Inputs the operator picks in the generator panel. Passed verbatim to the
    // game's ILevelGenerator. AdvancedConfig is the game's own tuning SO
    // (typically the same instance referenced from the GameProfile, but Layer 2
    // may clone it for thread-safety / per-run overrides if it wants to).
    public sealed class LevelGeneratorRequest
    {
        public int Difficulty;          // 1..10 in v1; generator clamps.
        public float? TargetAPS;        // optional; recorded on metadata, not enforced in v1.
        public int Seed;                // 0 = random; otherwise deterministic.
        public ScriptableObject AdvancedConfig;
    }
}
