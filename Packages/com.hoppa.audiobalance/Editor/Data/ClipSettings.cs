using System;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>Per-clip authoring state: which category it belongs to, plus a manual trim.</summary>
    [Serializable]
    public sealed class ClipSettings
    {
        public AudioClip Clip;
        public string Category = "SFX";

        /// <summary>Manual dB adjustment stacked on top of the category offset.</summary>
        public float TrimDb;
    }
}
