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

    }
}
