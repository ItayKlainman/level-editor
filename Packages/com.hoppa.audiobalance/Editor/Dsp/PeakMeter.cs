using System;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Peak diagnostics. Reported in the window to flag assets that arrived already clipped
    /// or hard against full scale -- applied gain can never cause clipping, because the
    /// headroom pass keeps every gain at or below 0 dB.
    /// </summary>
    public static class PeakMeter
    {
        private const int OversampleFactor = 4;

        public static float SamplePeakDb(float[] interleaved)
        {
            if (interleaved == null || interleaved.Length == 0)
            {
                return AudioGainMath.MinDb;
            }

            var peak = 0f;
            foreach (var sample in interleaved)
            {
                var magnitude = Math.Abs(sample);
                if (magnitude > peak)
                {
                    peak = magnitude;
                }
            }

            return AudioGainMath.DbFromLinear(peak);
        }

        /// <summary>
        /// Peak after 4x linear-interpolation oversampling. Approximate by design -- a true
        /// BS.1770 true-peak meter uses a polyphase FIR, which is more machinery than a
        /// diagnostic readout justifies.
        /// </summary>
        public static float ApproxTruePeakDb(float[] interleaved, int channels)
        {
            if (interleaved == null || interleaved.Length == 0 || channels <= 0)
            {
                return AudioGainMath.MinDb;
            }

            var frames = interleaved.Length / channels;
            if (frames < 2)
            {
                return SamplePeakDb(interleaved);
            }

            var peak = 0f;

            for (var ch = 0; ch < channels; ch++)
            {
                for (var frame = 0; frame < frames - 1; frame++)
                {
                    var a = interleaved[frame * channels + ch];
                    var b = interleaved[(frame + 1) * channels + ch];

                    for (var step = 0; step < OversampleFactor; step++)
                    {
                        var t = step / (float)OversampleFactor;
                        var magnitude = Math.Abs(a + (b - a) * t);
                        if (magnitude > peak)
                        {
                            peak = magnitude;
                        }
                    }
                }
            }

            return AudioGainMath.DbFromLinear(peak);
        }
    }
}
