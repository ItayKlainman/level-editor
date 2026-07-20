using System;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// ITU-R BS.1770-4 K-weighting: a high-shelf stage followed by an RLB high-pass stage.
    /// Coefficients are derived from the sample rate rather than resampling the audio, so a
    /// 44.1 kHz clip is measured natively. Deriving at 48 kHz reproduces the standard's
    /// published table (asserted in KWeightingTests).
    /// </summary>
    public static class KWeighting
    {
        public static BiquadCoefficients HighShelf(int sampleRate)
        {
            const double f0 = 1681.974450955533;
            const double gainDb = 3.999843853973347;
            const double q = 0.7071752369554196;

            var k = Math.Tan(Math.PI * f0 / sampleRate);
            var vh = Math.Pow(10.0, gainDb / 20.0);
            var vb = Math.Pow(vh, 0.4996667741545416);
            var a0 = 1.0 + k / q + k * k;

            return new BiquadCoefficients(
                (vh + vb * k / q + k * k) / a0,
                2.0 * (k * k - vh) / a0,
                (vh - vb * k / q + k * k) / a0,
                2.0 * (k * k - 1.0) / a0,
                (1.0 - k / q + k * k) / a0);
        }

        public static BiquadCoefficients HighPass(int sampleRate)
        {
            const double f0 = 38.13547087602444;
            const double q = 0.5003270373238773;

            var k = Math.Tan(Math.PI * f0 / sampleRate);
            var denominator = 1.0 + k / q + k * k;

            return new BiquadCoefficients(
                1.0,
                -2.0,
                1.0,
                2.0 * (k * k - 1.0) / denominator,
                (1.0 - k / q + k * k) / denominator);
        }

        /// <summary>Runs a single-channel signal through the biquad in place.</summary>
        public static void ApplyInPlace(double[] samples, BiquadCoefficients c)
        {
            if (samples == null)
            {
                return;
            }

            double x1 = 0.0, x2 = 0.0, y1 = 0.0, y2 = 0.0;

            for (var i = 0; i < samples.Length; i++)
            {
                var x0 = samples[i];
                var y0 = c.B0 * x0 + c.B1 * x1 + c.B2 * x2 - c.A1 * y1 - c.A2 * y2;

                x2 = x1;
                x1 = x0;
                y2 = y1;
                y1 = y0;

                samples[i] = y0;
            }
        }
    }
}
