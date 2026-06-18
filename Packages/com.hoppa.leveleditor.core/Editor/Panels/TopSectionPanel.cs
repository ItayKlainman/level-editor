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

        // When true, a subclass that arranges rows vertically should reverse the
        // data→visual row mapping so data index 0 appears at the TOP. Set by the
        // window from the profile's SpoolsBelowGrid flag before each draw.
        // Visual-only: it must NOT change serialized data order.
        public bool ReverseRowOrder { get; set; }
    }

    // Used when a game has no top section.
    public sealed class EmptyTopSectionPanel : TopSectionPanel
    {
        public override float PreferredHeight => 0f;
        public override void OnGUI(Rect rect, LevelEditorSession session) { }
    }
}
