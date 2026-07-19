using Hoppa.AudioBalance.Editor;
using NUnit.Framework;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class LufsMeterTests
    {
        private const int Rate = 48000;

        [Test]
        public void Integrated_StereoSineAtMinus23_ReadsMinus23Lufs()
        {
            var signal = SignalFactory.Sine(-23.0, 5.0, 2, Rate);

            var result = LufsMeter.MeasureIntegrated(signal, 2, Rate);

            Assert.IsFalse(result.IsSilent);
            Assert.AreEqual(-23.0f, result.Lufs, 0.1f);
        }

        [Test]
        public void Integrated_StereoSineAtMinus20_ReadsMinus20Lufs()
        {
            var signal = SignalFactory.Sine(-20.0, 5.0, 2, Rate);

            var result = LufsMeter.MeasureIntegrated(signal, 2, Rate);

            Assert.AreEqual(-20.0f, result.Lufs, 0.1f);
        }

        [Test]
        public void Integrated_MonoSine_ReadsThreeDbBelowTheStereoEquivalent()
        {
            var mono = LufsMeter.MeasureIntegrated(SignalFactory.Sine(-23.0, 5.0, 1, Rate), 1, Rate);

            Assert.AreEqual(-26.01f, mono.Lufs, 0.1f);
        }

        [Test]
        public void Integrated_AtOtherSampleRates_MatchesThe48kResult()
        {
            var at441 = LufsMeter.MeasureIntegrated(SignalFactory.Sine(-23.0, 5.0, 2, 44100), 2, 44100);

            Assert.AreEqual(-23.0f, at441.Lufs, 0.1f);
        }

        [Test]
        public void Integrated_AllZeroSignal_IsSilentNotNegativeInfinity()
        {
            var result = LufsMeter.MeasureIntegrated(SignalFactory.Silence(5.0, 2, Rate), 2, Rate);

            Assert.IsTrue(result.IsSilent);
            Assert.IsFalse(float.IsNegativeInfinity(result.Lufs));
            Assert.IsFalse(float.IsNaN(result.Lufs));
        }

        [Test]
        public void Integrated_AbsoluteGate_ExcludesAVeryQuietPassage()
        {
            // 3 s at -23 dBFS then 3 s at -85 dBFS. The quiet half is below the -70 LUFS
            // absolute gate, so it must not drag the result down.
            var loudOnly = LufsMeter.MeasureIntegrated(
                SignalFactory.Sine(-23.0, 3.0, 2, Rate), 2, Rate);

            var mixed = LufsMeter.MeasureIntegrated(
                SignalFactory.Concat(
                    SignalFactory.Sine(-23.0, 3.0, 2, Rate),
                    SignalFactory.Sine(-85.0, 3.0, 2, Rate)),
                2, Rate);

            // Tolerance widened from 0.2 to 0.26: three of the ~30 gated 400 ms blocks
            // straddle the hard t=3s transition with loud-signal duty cycles of 75%/50%/25%
            // (a consequence of the mandated 75%-overlap block scheme, not a gating bug).
            // Their diluted power pulls the gated mean down by 10*log10(28.5/30) = -0.223 dB,
            // verified independently against the measured output to five decimal places.
            // This is inherent to any spec-faithful implementation of this exact hard-cut
            // scenario, not an artifact of this implementation.
            Assert.AreEqual(loudOnly.Lufs, mixed.Lufs, 0.26f);
        }

        [Test]
        public void Integrated_RelativeGate_ExcludesAPassageMoreThan10LuDown()
        {
            // -23 dBFS then -45 dBFS: the quiet half clears the absolute gate but sits
            // more than 10 LU below the ungated loudness, so the relative gate drops it.
            var loudOnly = LufsMeter.MeasureIntegrated(
                SignalFactory.Sine(-23.0, 3.0, 2, Rate), 2, Rate);

            var mixed = LufsMeter.MeasureIntegrated(
                SignalFactory.Concat(
                    SignalFactory.Sine(-23.0, 3.0, 2, Rate),
                    SignalFactory.Sine(-45.0, 3.0, 2, Rate)),
                2, Rate);

            Assert.AreEqual(loudOnly.Lufs, mixed.Lufs, 0.5f);
        }

        [Test]
        public void Integrated_ClipShorterThanOneBlock_ReturnsAFiniteValue()
        {
            // 200 ms is shorter than the 400 ms block, so the block loop produces nothing
            // and a naive implementation returns -Infinity, which becomes NaN downstream.
            var signal = SignalFactory.Sine(-23.0, 0.2, 2, Rate);

            var result = LufsMeter.MeasureIntegrated(signal, 2, Rate);

            Assert.IsFalse(result.IsSilent);
            Assert.IsFalse(float.IsInfinity(result.Lufs) || float.IsNaN(result.Lufs));
            Assert.AreEqual(-23.0f, result.Lufs, 0.5f);
        }

        [Test]
        public void Integrated_EmptySignal_IsSilent()
        {
            Assert.IsTrue(LufsMeter.MeasureIntegrated(new float[0], 2, Rate).IsSilent);
        }

        [Test]
        public void Integrated_NullSignal_IsSilent()
        {
            Assert.IsTrue(LufsMeter.MeasureIntegrated(null, 2, Rate).IsSilent);
        }
    }
}
