using System.Collections.Generic;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Generic contract: "given a partial level + knobs, fill in what's missing."
    // Layer 2 decides what 'missing' means (spools, enemies, item placements…).
    // For YarnTwist v1 this fills the TopSection from a hand-painted grid.
    public interface ILevelCompleter
    {
        LevelCompletionResult Complete(LevelDocument doc, GameProfile profile, CompletionRequest req);

        // Names of the per-mechanic on/off toggles this completer understands, in
        // display order. The generic Auto-fill panel renders one checkbox per name
        // and passes the choices back via CompletionRequest.MechanicToggles. Null or
        // empty = the completer exposes no toggles (panel shows none).
        // (Implemented on LevelCompleterAsset — no default-interface-member, which
        // Unity's Mono runtime does not reliably support.)
        IReadOnlyList<string> MechanicToggles { get; }
    }
}
