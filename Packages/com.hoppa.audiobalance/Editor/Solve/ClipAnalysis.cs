using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>The measured facts about one clip, before any balancing decision is made.</summary>
    public readonly struct ClipAnalysis
    {
        public readonly AudioClip Clip;
        public readonly ClipStatus Status;
        public readonly float Lufs;
        public readonly float PeakDb;
        public readonly string Reason;

        public ClipAnalysis(AudioClip clip, ClipStatus status, float lufs,
            float peakDb, string reason = null)
        {
            Clip = clip;
            Status = status;
            Lufs = lufs;
            PeakDb = peakDb;
            Reason = reason;
        }

        public static ClipAnalysis Ok(AudioClip clip, float lufs, float peakDb)
        {
            return new ClipAnalysis(clip, ClipStatus.Ok, lufs, peakDb);
        }

        public static ClipAnalysis Silent(AudioClip clip)
        {
            return new ClipAnalysis(clip, ClipStatus.Silent, 0f, AudioGainMath.MinDb, "silent");
        }

        public static ClipAnalysis Unanalyzable(AudioClip clip, string reason)
        {
            return new ClipAnalysis(clip, ClipStatus.Unanalyzable, 0f, AudioGainMath.MinDb, reason);
        }
    }
}
