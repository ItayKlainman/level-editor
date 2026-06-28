using System.Collections.Generic;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Inputs to ILevelCompleter.Complete. The Difficulty knob is a 1..10 hint
    // that the Layer 2 completer maps to game-specific behaviour via its own
    // config asset. Seed = 0 means "pick a random seed".
    public sealed class CompletionRequest
    {
        public int    Difficulty = 5;
        public float? TargetAPS;
        public int    Seed;
        public int?   ConveyorCapacityOverride;

        // Target click-pattern complexity (1..10). Null = the completer's config
        // default. Composes with TargetAPS — a candidate must satisfy both bands.
        public int? TargetComplexity;

        // Per-mechanic on/off choices the completer understands (names come from
        // ILevelCompleter.MechanicToggles). A checked toggle = "include this
        // mechanic when filling". Null = the completer's defaults apply.
        public Dictionary<string, bool> MechanicToggles;
    }
}
