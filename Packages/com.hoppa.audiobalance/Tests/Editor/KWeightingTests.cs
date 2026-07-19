using System;
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class KWeightingTests
    {
        private const double Tolerance = 1e-9;

        [Test]
        public void HighShelf_At48kHz_MatchesPublishedConstants()
        {
            var c = KWeighting.HighShelf(48000);

            Assert.AreEqual(1.53512485958697, c.B0, Tolerance);
            Assert.AreEqual(-2.69169618940638, c.B1, Tolerance);
            Assert.AreEqual(1.19839281085285, c.B2, Tolerance);
            Assert.AreEqual(-1.69065929318241, c.A1, Tolerance);
            Assert.AreEqual(0.73248077421585, c.A2, Tolerance);
        }

        [Test]
        public void HighPass_At48kHz_MatchesPublishedConstants()
        {
            var c = KWeighting.HighPass(48000);

            Assert.AreEqual(1.0, c.B0, Tolerance);
            Assert.AreEqual(-2.0, c.B1, Tolerance);
            Assert.AreEqual(1.0, c.B2, Tolerance);
            Assert.AreEqual(-1.99004745483398, c.A1, Tolerance);
            Assert.AreEqual(0.99007225036621, c.A2, Tolerance);
        }

        [TestCase(44100)]
        [TestCase(22050)]
        [TestCase(96000)]
        public void Coefficients_AtOtherRates_AreFinite(int sampleRate)
        {
            var shelf = KWeighting.HighShelf(sampleRate);
            var pass = KWeighting.HighPass(sampleRate);

            foreach (var v in new[] { shelf.B0, shelf.B1, shelf.B2, shelf.A1, shelf.A2,
                                      pass.B0, pass.B1, pass.B2, pass.A1, pass.A2 })
            {
                Assert.IsFalse(double.IsNaN(v) || double.IsInfinity(v));
            }
        }

        [Test]
        public void ApplyInPlace_GainAt1kHz_IsAboutPlusPoint691Db()
        {
            // The -0.691 offset in the loudness formula exists to cancel K-weighting's
            // gain at 1 kHz. Proving that gain here is what makes the LUFS calibration
            // test in LufsMeterTests meaningful.
            const int sampleRate = 48000;
            const int frames = sampleRate * 2;

            var signal = new double[frames];
            for (var i = 0; i < frames; i++)
            {
                signal[i] = Math.Sin(2.0 * Math.PI * 1000.0 * i / sampleRate);
            }

            var filtered = (double[])signal.Clone();
            KWeighting.ApplyInPlace(filtered, KWeighting.HighShelf(sampleRate));
            KWeighting.ApplyInPlace(filtered, KWeighting.HighPass(sampleRate));

            // Skip the first 0.5 s so the filters' transient response is excluded.
            var start = sampleRate / 2;
            var gainDb = 10.0 * Math.Log10(MeanSquare(filtered, start) / MeanSquare(signal, start));

            Assert.AreEqual(0.691, gainDb, 0.02);
        }

        private static double MeanSquare(double[] values, int start)
        {
            var sum = 0.0;
            for (var i = start; i < values.Length; i++)
            {
                sum += values[i] * values[i];
            }

            return sum / (values.Length - start);
        }
    }
}
