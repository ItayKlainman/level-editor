using System;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // A profile-supplied panel that adds a second mode to the Generate tab
    // (alongside the built-in level generator). Plain POCO instantiated via
    // reflection, mirroring ProfileLeftPanel/ProfileRightPanel.
    public abstract class ProfileGeneratePanel
    {
        // Set by the host so async work can request a repaint. May be null.
        public Action RequestRepaint { get; set; }

        // Short label shown on the mode toggle (e.g. "Idea Generator").
        public abstract string Title { get; }

        public virtual void OnEnterMode() { }
        public virtual void OnExitMode() { }

        public abstract void OnGUI(Rect rect, GameProfile profile);
    }
}
