using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>One clip's solved gain. FinalGainDb is what gets baked into the table.</summary>
    public readonly struct GainResult
    {
        public readonly AudioClip Clip;
        public readonly ClipStatus Status;

        /// <summary>Gain before the headroom pass. May be positive.</summary>
        public readonly float RawGainDb;

        /// <summary>Gain after the headroom pass. Always at or below 0 dB.</summary>
        public readonly float FinalGainDb;

        public readonly bool IsOutlier;

        public GainResult(AudioClip clip, ClipStatus status, float rawGainDb,
            float finalGainDb, bool isOutlier)
        {
            Clip = clip;
            Status = status;
            RawGainDb = rawGainDb;
            FinalGainDb = finalGainDb;
            IsOutlier = isOutlier;
        }
    }
}
