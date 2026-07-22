using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Abstract base for a game-specific section rendered in the LEFT column of
    // LevelEditorWindow, between the cell/brush palette and the TOOLS section.
    // Games subclass this and register it on their GameProfile via a MonoScript
    // field (_leftPanelScript). Keeps the Layer-1 / Layer-2 separation intact:
    // Layer 1 renders whatever the profile provides without knowing the game.
    public abstract class ProfileLeftPanel
    {
        // Height (px) the panel would like in the left column, reserved between the
        // cell list and the TOOLS section. The window shrinks the cell list to fit.
        public abstract float PreferredHeight { get; }

        // Draw into `rect`. `session` exposes the open document (mutate via
        // PushUndoSnapshot / MarkDirty / RunValidation); `profile` is the active
        // game profile (for the completer/analyzer).
        public abstract void OnGUI(Rect rect, LevelEditorSession session, GameProfile profile);
    }
}
