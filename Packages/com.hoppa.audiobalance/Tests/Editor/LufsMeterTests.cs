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
            // 3 s at -65 dBFS then 3 s at -72 dBFS. Unlike a -23/-85 split, -72 LUFS clears the
            // relative gate on its own: the mean loudness of the blocks that clear the absolute
            // gate is ~-65.18 LUFS, putting the relative threshold at ~-75.18 LU -- well below
            // -72. So this signal only comes out right if the -70 LUFS absolute gate
            // independently excludes the quiet half; a -23/-85 split does not prove this because
            // -85 is already far below the relative threshold too, so the relative gate alone
            // would exclude it even with the absolute gate deleted. That is what this signal
            // isolates that the relative-gate test below does not.
            var loudOnly = LufsMeter.MeasureIntegrated(
                SignalFactory.Sine(-65.0, 3.0, 2, Rate), 2, Rate);

            var mixed = LufsMeter.MeasureIntegrated(
                SignalFactory.Concat(
                    SignalFactory.Sine(-65.0, 3.0, 2, Rate),
                    SignalFactory.Sine(-72.0, 3.0, 2, Rate)),
                2, Rate);

            // Same 75%-overlap block scheme as the sibling gating tests: 3 of the ~57 total
            // 400 ms blocks straddle the hard t=3s transition at loud-duty-cycles of
            // 75%/50%/25%. Unlike the -23/-85 case, all three straddling blocks here still
            // clear the -70 absolute gate on their own blended loudness (even the 25%-loud
            // block averages to ~-68.98 LUFS), so all 30 above-gate blocks (27 full-loud + 3
            // straddling) are kept -- none are additionally dropped by the relative gate.
            // Modelling each straddling block's per-channel power as f*P_loud + (1-f)*P_quiet
            // for f in {0.75, 0.5, 0.25} and averaging with the 27 full-loud blocks predicts a
            // gated result of -65.177 LUFS against a loud-only reference of -65.000 LUFS: a
            // diluted -0.177 dB offset from those 3 blocks, not a gating bug (matches the
            // measured output). 0.22 tolerance covers that offset plus ordinary filter noise.
            Assert.AreEqual(loudOnly.Lufs, mixed.Lufs, 0.22f);
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
