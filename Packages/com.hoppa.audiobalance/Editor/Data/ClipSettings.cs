using System;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>Per-clip authoring state: which category it belongs to, plus a manual trim.</summary>
    [Serializable]
    public sealed class ClipSettings
    {
        [Tooltip("The audio file these settings belong to.")]
        public AudioClip Clip;

        [Tooltip("Which group this clip is balanced with. Must match a category name on the " +
                 "profile; each group is measured its own way, so moving a clip between " +
                 "groups re-measures it.")]
        public string Category = "SFX";

        /// <summary>Manual dB adjustment stacked on top of the category offset.</summary>
        [Tooltip("A manual nudge for this one clip, in dB, on top of its group's offset. Use " +
                 "it when a single sound still feels wrong after the group is right.")]
        public float TrimDb;
    }
}
