using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>One line in the window: a clip plus its measurement and solved gain.</summary>
    public sealed class AudioBalanceRow
    {
        public AudioClip Clip;
        public ClipAnalysis Analysis;
        public GainResult Gain;
    }
}
