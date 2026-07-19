using Hoppa.AudioBalance.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class ClipSampleReaderTests
    {
        [Test]
        public void TryRead_OnAProceduralClip_ReturnsInterleavedSamples()
        {
            var clip = AudioClip.Create("tone", 256, 2, 48000, false);
            var data = SignalFactory.Sine(-6.0, 256 / 48000.0, 2, 48000);
            clip.SetData(data, 0);

            var ok = ClipSampleReader.TryRead(clip, out var samples, out var error);

            Assert.IsTrue(ok, error);
            Assert.IsNull(error);
            Assert.AreEqual(256 * 2, samples.Length);
        }

        [Test]
        public void TryRead_OnNullClip_FailsWithAnError()
        {
            var ok = ClipSampleReader.TryRead(null, out var samples, out var error);

            Assert.IsFalse(ok);
            Assert.IsNull(samples);
            Assert.IsNotNull(error);
        }

        [Test]
        public void TryRead_RoundTripsSampleValues()
        {
            var clip = AudioClip.Create("ramp", 4, 1, 48000, false);
            clip.SetData(new[] { 0.25f, -0.5f, 0.75f, -1f }, 0);

            Assert.IsTrue(ClipSampleReader.TryRead(clip, out var samples, out _));

            Assert.AreEqual(0.25f, samples[0], 1e-4f);
            Assert.AreEqual(-0.5f, samples[1], 1e-4f);
            Assert.AreEqual(0.75f, samples[2], 1e-4f);
            Assert.AreEqual(-1f, samples[3], 1e-4f);
        }
    }
}
