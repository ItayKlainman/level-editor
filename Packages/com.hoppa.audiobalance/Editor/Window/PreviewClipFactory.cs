using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Builds the gain-applied sample buffers the preview plays. Kept separate from
    /// <see cref="AudioPreviewPlayer"/> because this half is deterministic, has no editor
    /// dependencies, and therefore unit-tests -- unlike the reflection-based playback.
    /// </summary>
    public static class PreviewClipFactory
    {
        /// <summary>
        /// Scales every sample by a linear gain, clamping to [-1, 1].
        ///
        /// <para>
        /// The clamp is defence in depth HERE, not the load-bearing case. Every caller passes
        /// <c>row.Gain.FinalGainDb</c>, which the headroom pass guarantees is at or below 0 dB,
        /// so scaling alone cannot exceed full scale -- the per-clip trim is already folded into
        /// that solved value rather than applied on top of it. The clamp that actually does work
        /// is the one in <see cref="Mix"/>, where two signals each at or below unity sum past it.
        /// Clamping is kept here so the function is total for any gain a future caller passes,
        /// and because clipping is the honest failure -- it is what the runtime would do, whereas
        /// normalising would make the preview quieter than the thing it is previewing.
        /// </para>
        /// </summary>
        public static float[] Scale(float[] samples, float gainDb)
        {
            if (samples == null)
            {
                return new float[0];
            }

            var gain = AudioGainMath.LinearFromDb(gainDb);
            var scaled = new float[samples.Length];

            for (var i = 0; i < samples.Length; i++)
            {
                scaled[i] = Mathf.Clamp(samples[i] * gain, -1f, 1f);
            }

            return scaled;
        }

        /// <summary>
        /// Sums two gain-applied signals, aligned at sample 0. The result is as long as the
        /// longer input, so a short SFX over a long music bed keeps the bed audible after the
        /// SFX ends -- which is the whole point of judging it in context.
        /// </summary>
        public static float[] Mix(float[] a, float aGainDb, float[] b, float bGainDb)
        {
            var left = Scale(a, aGainDb);
            var right = Scale(b, bGainDb);
            var mixed = new float[Mathf.Max(left.Length, right.Length)];

            for (var i = 0; i < mixed.Length; i++)
            {
                var sum = 0f;
                if (i < left.Length)
                {
                    sum += left[i];
                }

                if (i < right.Length)
                {
                    sum += right[i];
                }

                mixed[i] = Mathf.Clamp(sum, -1f, 1f);
            }

            return mixed;
        }
    }
}
