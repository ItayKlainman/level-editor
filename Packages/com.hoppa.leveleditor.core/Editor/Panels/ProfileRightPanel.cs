using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Abstract base for a game-specific section rendered in the RIGHT column of
    // LevelEditorWindow, beneath the built-in Spool Analysis / Auto-fill panel.
    // Games subclass this and register it on their GameProfile via a MonoScript
    // field (_rightPanelScript). Keeps the Layer-1 / Layer-2 separation intact:
    // Layer 1 renders whatever the profile provides without knowing the game.
    public abstract class ProfileRightPanel
    {
        // Height (px) the panel would like at the bottom of the right column.
        // The window caps this at a fraction of the available height.
        public abstract float PreferredHeight { get; }

        // Draw into `rect`. `session` exposes the open document (mutate via
        // PushUndoSnapshot / MarkDirty / RunValidation); `profile` is the active
        // game profile (for the completer/analyzer).
        public abstract void OnGUI(Rect rect, LevelEditorSession session, GameProfile profile);
    }
}
