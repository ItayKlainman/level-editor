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
    }
}
