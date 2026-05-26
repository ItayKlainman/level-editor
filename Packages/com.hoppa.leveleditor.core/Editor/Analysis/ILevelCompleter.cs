namespace Hoppa.LevelEditor.Core.Editor
{
    // Generic contract: "given a partial level + knobs, fill in what's missing."
    // Layer 2 decides what 'missing' means (spools, enemies, item placements…).
    // For YarnTwist v1 this fills the TopSection from a hand-painted grid.
    public interface ILevelCompleter
    {
        LevelCompletionResult Complete(LevelDocument doc, GameProfile profile, CompletionRequest req);
    }
}
