using System;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    /// <summary>
    /// A profile-supplied panel that adds a second mode to the Generate tab
    /// (alongside the built-in level generator). Plain POCO instantiated via
    /// reflection, mirroring ProfileLeftPanel/ProfileRightPanel.
    /// </summary>
    /// <remarks>
    /// Lifecycle contract: <see cref="OnEnterMode"/> is called once when the panel
    /// is bound to a profile. <see cref="OnExitMode"/> may be called more than once
    /// and may fire WITHOUT a preceding <see cref="OnEnterMode"/> (e.g. re-opening
    /// the Generate tab on the same profile does not re-fire OnEnterMode), so
    /// implementations MUST make OnExitMode idempotent — tearing down already-clean
    /// state must be a safe no-op.
    /// </remarks>
    public abstract class ProfileGeneratePanel
    {
        // Set by the host so async work can request a repaint. May be null.
        public Action RequestRepaint { get; set; }

        // Short label shown on the mode toggle (e.g. "Idea Generator").
        public abstract string Title { get; }

        /// <summary>Called once when the panel is bound to a profile.</summary>
        public virtual void OnEnterMode() { }

        /// <summary>
        /// May be called more than once and may fire without a preceding
        /// <see cref="OnEnterMode"/>; implementations must be idempotent.
        /// </summary>
        public virtual void OnExitMode() { }

        public abstract void OnGUI(Rect rect, GameProfile profile);
    }
}
