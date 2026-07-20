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
        ///
        /// Untested: this branch has no automated coverage. <see cref="AudioClip.Create"/> —
        /// the only way tests build clips — always produces a fully-resident, non-streaming
        /// clip; <see cref="AudioClipLoadType.Streaming"/> can only be set on an imported asset
        /// via its AudioImporter. Covering it would take a committed .wav fixture with its
        /// .meta pinned to Streaming. Accepting the gap rather than adding that fixture was a
        /// deliberate call, not an oversight.
        /// </summary>
        public const string StreamingError = "set Load Type to Decompress On Load";

        /// <summary>
        /// LoadAudioData() returning true only means the load was queued, not that decoding
        /// finished — GetData must never run before loadState is actually Loaded. Unlike the
        /// "failed to load audio data" case below (the request itself was rejected), this one
        /// means the request was accepted but hadn't completed by the time we checked back.
        /// </summary>
        public const string LoadPendingError = "audio data is still loading; re-run the analysis once the clip finishes loading";

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

            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                if (!clip.LoadAudioData())
                {
                    error = "failed to load audio data";
                    return false;
                }

                if (clip.loadState != AudioDataLoadState.Loaded)
                {
                    error = LoadPendingError;
                    return false;
                }
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
