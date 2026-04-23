using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Abstract base for game-specific top-section editors (e.g. spool columns in Yarn Sort).
    // Games subclass this and register it on their GameProfile.
    // The window hides the region when EmptyTopSectionPanel is active.
    public abstract class TopSectionPanel : IEditorPanel
    {
        public abstract float PreferredHeight { get; }
        public abstract void OnGUI(Rect rect, LevelEditorSession session);
    }

    // Used when a game has no top section.
    public sealed class EmptyTopSectionPanel : TopSectionPanel
    {
        public override float PreferredHeight => 0f;
        public override void OnGUI(Rect rect, LevelEditorSession session) { }
    }
}
