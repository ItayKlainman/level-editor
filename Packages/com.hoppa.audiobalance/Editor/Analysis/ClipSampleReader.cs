using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>Reads decoded PCM out of an AudioClip for analysis.</summary>
    public static class ClipSampleReader
    {
        /// <summary>
        /// Streaming clips return silence from GetData. We report this rather than flipping
        /// the importer's load type, because silently rewriting someone's import settings is
        /// a worse surprise than an actionable message.
        /// </summary>
        public const string StreamingError = "set Load Type to Decompress On Load";

        public static bool TryRead(AudioClip clip, out float[] interleaved, out string error)
        {
            interleaved = null;
            error = null;

            if (clip == null)
            {
                error = "clip is null";
                return false;
            }

            if (clip.loadType == AudioClipLoadType.Streaming)
            {
                error = StreamingError;
                return false;
            }

            if (clip.loadState != AudioDataLoadState.Loaded && !clip.LoadAudioData())
            {
                error = "failed to load audio data";
                return false;
            }

            var samples = clip.samples * clip.channels;
            if (samples <= 0)
            {
                error = "clip contains no samples";
                return false;
            }

            var data = new float[samples];
            if (!clip.GetData(data, 0))
            {
                error = "GetData failed";
                return false;
            }

            interleaved = data;
            return true;
        }
    }
}
