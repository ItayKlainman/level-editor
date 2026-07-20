using NUnit.Framework;

namespace Hoppa.AudioBalance.Editor.Tests
{
    /// <summary>
    /// Covers the half of the preview path that can be tested. Playback itself reflects into
    /// UnityEditor.AudioUtil and is documented as an accepted, untestable gap on
    /// <see cref="AudioPreviewPlayer"/> -- but the gain arithmetic it depends on is pinned here.
    /// </summary>
    public class PreviewClipFactoryTests
    {
        [Test]
        public void Scale_MinusSixDbHalvesTheAmplitude()
        {
            var scaled = PreviewClipFactory.Scale(new[] { 1f, -1f, 0.5f }, -6.0206f);

            Assert.AreEqual(0.5f, scaled[0], 1e-3f);
            Assert.AreEqual(-0.5f, scaled[1], 1e-3f);
            Assert.AreEqual(0.25f, scaled[2], 1e-3f);
        }

        [Test]
        public void Scale_ZeroDbIsIdentity()
        {
            var scaled = PreviewClipFactory.Scale(new[] { 0.3f, -0.7f }, 0f);

            Assert.AreEqual(0.3f, scaled[0], 1e-6f);
            Assert.AreEqual(-0.7f, scaled[1], 1e-6f);
        }

        [Test]
        public void Scale_ClampsRatherThanWrappingWhenAPositiveTrimOverdrivesTheSignal()
        {
            // A +12 dB trim on a near-full-scale sample must clip, not wrap to a negative
            // value, and not silently renormalise the whole preview.
            var scaled = PreviewClipFactory.Scale(new[] { 0.9f, -0.9f }, 12f);

            Assert.AreEqual(1f, scaled[0], 1e-6f);
            Assert.AreEqual(-1f, scaled[1], 1e-6f);
        }

        [Test]
        public void Scale_OnNullInput_ReturnsAnEmptyArray()
        {
            Assert.AreEqual(0, PreviewClipFactory.Scale(null, 0f).Length);
        }

        [Test]
        public void Mix_SumsBothSignalsAtTheirRespectiveGains()
        {
            var mixed = PreviewClipFactory.Mix(new[] { 0.5f }, 0f, new[] { 0.25f }, 0f);

            Assert.AreEqual(0.75f, mixed[0], 1e-4f);
        }

        /// <summary>
        /// The other Mix tests all pass 0 dB for both gains, so every one of them would survive
        /// a Mix that ignored its gain parameters entirely and just summed the raw arrays --
        /// which is precisely the bug that would make A/B inaudible-as-balanced (both signals
        /// played at source level, so the comparison shows nothing). This is the test that
        /// actually pins the gains being applied, and it pins them SEPARATELY: swapping the two
        /// gain arguments changes the result.
        /// </summary>
        [Test]
        public void Mix_AppliesEachSignalsOwnGainRatherThanIgnoringThem()
        {
            var mixed = PreviewClipFactory.Mix(new[] { 0.8f }, -6.0206f, new[] { 0.4f }, -12.0412f);

            // 0.8 * 0.5 + 0.4 * 0.25 = 0.4 + 0.1
            Assert.AreEqual(0.5f, mixed[0], 1e-3f);
        }

        [Test]
        public void Mix_ResultIsAsLongAsTheLongerInput()
        {
            // A short SFX over a long bed must not truncate the bed -- judging it in context
            // is the entire point of A/B.
            var mixed = PreviewClipFactory.Mix(new[] { 0.5f }, 0f, new[] { 0.1f, 0.1f, 0.1f }, 0f);

            Assert.AreEqual(3, mixed.Length);
            Assert.AreEqual(0.6f, mixed[0], 1e-4f);
            Assert.AreEqual(0.1f, mixed[1], 1e-4f);
        }

        [Test]
        public void Mix_ClampsTheSum()
        {
            var mixed = PreviewClipFactory.Mix(new[] { 0.8f }, 0f, new[] { 0.8f }, 0f);

            Assert.AreEqual(1f, mixed[0], 1e-6f);
        }
    }
}
