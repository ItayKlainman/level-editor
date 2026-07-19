using Hoppa.AudioBalance;
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class PeakMeterTests
    {
        [Test]
        public void SamplePeakDb_OfFullScaleSignal_IsZeroDb()
        {
            var signal = new[] { 0.1f, -1.0f, 0.5f, 0.2f };

            Assert.AreEqual(0f, PeakMeter.SamplePeakDb(signal), 1e-3f);
        }

        [Test]
        public void SamplePeakDb_OfHalfScaleSignal_IsAboutMinusSixDb()
        {
            var signal = new[] { 0.1f, 0.5f, -0.25f };

            Assert.AreEqual(-6.02f, PeakMeter.SamplePeakDb(signal), 0.05f);
        }

        [Test]
        public void SamplePeakDb_OfSilence_IsTheFloorNotNegativeInfinity()
        {
            Assert.AreEqual(AudioGainMath.MinDb, PeakMeter.SamplePeakDb(new float[16]), 1e-3f);
        }

        [Test]
        public void SamplePeakDb_OfNullOrEmpty_IsTheFloor()
        {
            Assert.AreEqual(AudioGainMath.MinDb, PeakMeter.SamplePeakDb(null), 1e-3f);
            Assert.AreEqual(AudioGainMath.MinDb, PeakMeter.SamplePeakDb(new float[0]), 1e-3f);
        }

        [Test]
        public void ApproxTruePeakDb_IsAtLeastTheSamplePeak()
        {
            var signal = SignalFactory.Sine(-6.0, 0.25, 2, 48000);

            Assert.GreaterOrEqual(
                PeakMeter.ApproxTruePeakDb(signal, 2),
                PeakMeter.SamplePeakDb(signal) - 1e-3f);
        }

        [Test]
        public void ApproxTruePeakDb_OfSilence_IsTheFloor()
        {
            Assert.AreEqual(AudioGainMath.MinDb, PeakMeter.ApproxTruePeakDb(new float[64], 2), 1e-3f);
        }
    }
}
