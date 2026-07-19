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
        public void SamplePeakDb_FindsThePeakOnTheFinalFrame()
        {
            // Guards the boundary an earlier interpolating implementation got wrong:
            // the loudest sample sits on the very last frame and must still be found.
            Assert.AreEqual(0f, PeakMeter.SamplePeakDb(new[] { 0f, 0f, 1f }), 1e-3f);
        }

        [Test]
        public void SamplePeakDb_ScansEveryChannelOfAnInterleavedBuffer()
        {
            // Quiet left, full-scale right. A meter that only scanned channel 0 would
            // report -20 dB and miss a clipped right channel entirely.
            var stereo = new[] { 0.1f, 0.5f, 0.1f, -1f };

            Assert.AreEqual(0f, PeakMeter.SamplePeakDb(stereo), 1e-3f);
        }
    }
}
