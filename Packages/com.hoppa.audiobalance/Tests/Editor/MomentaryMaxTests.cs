using Hoppa.AudioBalance.Editor;
using NUnit.Framework;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class MomentaryMaxTests
    {
        private const int Rate = 48000;

        [Test]
        public void MomentaryMax_OnClipShorterThanOneWindow_MeasuresTheWholeClip()
        {
            // 200 ms is shorter than the 400 ms window, so ComputeBlockPowers collapses to a
            // single block spanning the clip -- the behaviour short SFX depend on.
            var signal = SignalFactory.Sine(-23.0, 0.2, 2, Rate);

            var result = LufsMeter.MeasureMomentaryMax(signal, 2, Rate);

            Assert.IsFalse(result.IsSilent);
            Assert.AreEqual(-23.0f, result.Lufs, 0.5f);
        }

        [Test]
        public void MomentaryMax_OnSteadyTone_AgreesWithIntegrated()
        {
            // On steady material the two modes must not diverge: every 400 ms window looks
            // like every other, and the gating has nothing to exclude.
            var signal = SignalFactory.Sine(-23.0, 5.0, 2, Rate);

            var integrated = LufsMeter.MeasureIntegrated(signal, 2, Rate);
            var momentary = LufsMeter.MeasureMomentaryMax(signal, 2, Rate);

            Assert.AreEqual(integrated.Lufs, momentary.Lufs, 0.1f);
        }

        [Test]
        public void MomentaryMax_ExceedsIntegrated_ForAOneShotWithALongQuietTail()
        {
            // 0.5 s at -18 dBFS then 4 s at -50 dBFS -- a percussive one-shot decaying out.
            // The 400 ms window lands entirely inside the attack (~-18), while integrated
            // gating keeps the attack blocks AND the three that straddle the transition,
            // pulling its answer below the peak (~-19.5).
            //
            // A 3 s window would FAIL this: it would be forced to average 0.5 s of attack
            // with 2.5 s of near-silence (~-25.8), landing well BELOW integrated. That is
            // why this mode measures 400 ms, not 3 s.
            var signal = SignalFactory.Concat(
                SignalFactory.Sine(-18.0, 0.5, 2, Rate),
                SignalFactory.Sine(-50.0, 4.0, 2, Rate));

            var integrated = LufsMeter.MeasureIntegrated(signal, 2, Rate);
            var momentary = LufsMeter.MeasureMomentaryMax(signal, 2, Rate);

            Assert.Greater(momentary.Lufs, integrated.Lufs,
                "Momentary max must track the loudest moment, not the gated average.");
        }

        [Test]
        public void MomentaryMax_OnSilence_IsSilent()
        {
            var result = LufsMeter.MeasureMomentaryMax(SignalFactory.Silence(4.0, 2, Rate), 2, Rate);

            Assert.IsTrue(result.IsSilent);
        }

        [Test]
        public void MomentaryMax_OnVeryQuietSignalBelowAbsoluteGate_IsSilent()
        {
            var result = LufsMeter.MeasureMomentaryMax(SignalFactory.Sine(-90.0, 4.0, 2, Rate), 2, Rate);

            Assert.IsTrue(result.IsSilent);
        }

        [Test]
        public void Measure_DispatchesOnMode()
        {
            var signal = SignalFactory.Sine(-23.0, 5.0, 2, Rate);

            var viaIntegrated = LufsMeter.Measure(signal, 2, Rate, MeasureMode.Integrated);
            var viaMomentary = LufsMeter.Measure(signal, 2, Rate, MeasureMode.MomentaryMax);

            Assert.AreEqual(LufsMeter.MeasureIntegrated(signal, 2, Rate).Lufs, viaIntegrated.Lufs, 1e-4f);
            Assert.AreEqual(LufsMeter.MeasureMomentaryMax(signal, 2, Rate).Lufs, viaMomentary.Lufs, 1e-4f);
        }
    }
}
