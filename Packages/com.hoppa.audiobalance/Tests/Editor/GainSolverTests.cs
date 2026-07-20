using System.Collections.Generic;
using System.Linq;
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class GainSolverTests
    {
        private static AudioClip MakeClip(string name)
        {
            return AudioClip.Create(name, 128, 1, 44100, false);
        }

        private static IReadOnlyList<GainResult> Solve(
            IReadOnlyList<ClipAnalysis> analyses,
            float anchorLufs,
            Dictionary<AudioClip, float> offsets = null,
            Dictionary<AudioClip, float> trims = null)
        {
            return GainSolver.Solve(
                analyses,
                anchorLufs,
                clip => offsets != null && offsets.TryGetValue(clip, out var o) ? o : 0f,
                clip => trims != null && trims.TryGetValue(clip, out var t) ? t : 0f);
        }

        [Test]
        public void Anchor_InAZeroOffsetCategoryWithNoTrim_ResolvesToZeroRawGain()
        {
            var anchor = MakeClip("anchor");
            var results = Solve(new[] { ClipAnalysis.Ok(anchor, -18f, -1f) }, -18f);

            Assert.AreEqual(0f, results[0].RawGainDb, 1e-4f);
        }

        [Test]
        public void CategoryOffset_ShiftsGainByExactlyThatManyDb()
        {
            var clip = MakeClip("sfx");
            var offsets = new Dictionary<AudioClip, float> { { clip, 3f } };

            var results = Solve(new[] { ClipAnalysis.Ok(clip, -18f, -1f) }, -18f, offsets);

            Assert.AreEqual(3f, results[0].RawGainDb, 1e-4f);
        }

        [Test]
        public void Trim_StacksAdditivelyOnTopOfTheCategoryOffset()
        {
            var clip = MakeClip("sfx");
            var offsets = new Dictionary<AudioClip, float> { { clip, 3f } };
            var trims = new Dictionary<AudioClip, float> { { clip, -1.5f } };

            var results = Solve(new[] { ClipAnalysis.Ok(clip, -18f, -1f) }, -18f, offsets, trims);

            Assert.AreEqual(1.5f, results[0].RawGainDb, 1e-4f);
        }

        [Test]
        public void QuieterClipThanAnchor_NeedsPositiveRawGain()
        {
            var clip = MakeClip("quiet");
            var results = Solve(new[] { ClipAnalysis.Ok(clip, -30f, -12f) }, -18f);

            Assert.AreEqual(12f, results[0].RawGainDb, 1e-4f);
        }

        [Test]
        public void AfterNormalization_TheMaximumFinalGainIsExactlyZeroDb()
        {
            var loud = MakeClip("loud");
            var quiet = MakeClip("quiet");
            var results = Solve(new[]
            {
                ClipAnalysis.Ok(loud, -12f, -1f),
                ClipAnalysis.Ok(quiet, -30f, -14f)
            }, -18f);

            Assert.AreEqual(0f, results.Max(r => r.FinalGainDb), 1e-4f);
        }

        [Test]
        public void AfterNormalization_EveryFinalGainIsAtOrBelowZeroDb()
        {
            var results = Solve(new[]
            {
                ClipAnalysis.Ok(MakeClip("a"), -30f, -14f),
                ClipAnalysis.Ok(MakeClip("b"), -26f, -10f),
                ClipAnalysis.Ok(MakeClip("c"), -40f, -20f)
            }, -18f);

            foreach (var result in results)
            {
                Assert.LessOrEqual(result.FinalGainDb, 1e-4f,
                    "AudioSource.volume caps at 1.0, so a positive gain is unachievable.");
            }
        }

        [Test]
        public void Normalization_PreservesRelativeSpacingExactly()
        {
            var a = MakeClip("a");
            var b = MakeClip("b");
            var results = Solve(new[]
            {
                ClipAnalysis.Ok(a, -30f, -14f),
                ClipAnalysis.Ok(b, -22f, -6f)
            }, -18f);

            var rawSpacing = results[0].RawGainDb - results[1].RawGainDb;
            var finalSpacing = results[0].FinalGainDb - results[1].FinalGainDb;

            Assert.AreEqual(rawSpacing, finalSpacing, 1e-4f);
        }

        [Test]
        public void SilentAndUnanalyzableClips_AreExcludedFromTheMaxGainCalculation()
        {
            var ok = MakeClip("ok");
            var silent = MakeClip("silent");

            // ClipAnalysis.Silent(...) hardcodes Lufs = 0f, which is too quiet to ever win
            // the max comparison against a real Ok clip -- deleting the exclusion in
            // GainSolver would still leave this test passing. Building the Silent analysis
            // through the public constructor with an artificially low Lufs (-60f) makes its
            // would-be raw gain (+42) far exceed the Ok clip's (+4), so this test can only
            // pass if GainSolver actually skips non-Ok clips before the max comparison.
            var silentAnalysis = new ClipAnalysis(silent, ClipStatus.Silent, -60f, AudioGainMath.MinDb, "silent");

            var results = Solve(new[]
            {
                ClipAnalysis.Ok(ok, -22f, -6f), // raw = -18 + 0 + 0 - (-22) = +4
                silentAnalysis                  // raw would be -18 + 0 + 0 - (-60) = +42 if included
            }, -18f);

            var okResult = results.First(r => r.Clip == ok);
            Assert.AreEqual(0f, okResult.FinalGainDb, 1e-4f,
                "The single analyzable clip should define the 0 dB ceiling, even though the " +
                "excluded clip's (would-be) raw gain is far higher.");
        }

        [Test]
        public void SilentAndUnanalyzableClips_GetZeroGainAndKeepTheirStatus()
        {
            var silent = MakeClip("silent");
            var broken = MakeClip("broken");

            var results = Solve(new[]
            {
                ClipAnalysis.Ok(MakeClip("ok"), -22f, -6f),
                ClipAnalysis.Silent(silent),
                ClipAnalysis.Unanalyzable(broken, "streaming")
            }, -18f);

            var silentResult = results.First(r => r.Clip == silent);
            Assert.AreEqual(ClipStatus.Silent, silentResult.Status);
            Assert.AreEqual(0f, silentResult.FinalGainDb, 1e-4f);

            var brokenResult = results.First(r => r.Clip == broken);
            Assert.AreEqual(ClipStatus.Unanalyzable, brokenResult.Status);
            Assert.AreEqual(0f, brokenResult.FinalGainDb, 1e-4f);
        }

        [Test]
        public void OutlierFlag_TriggersAboveTwelveDbAndNotBelow()
        {
            var inside = MakeClip("inside");
            var outside = MakeClip("outside");
            var boundary = MakeClip("boundary");

            var results = Solve(new[]
            {
                ClipAnalysis.Ok(inside, -29f, -12f),   // raw = +11
                ClipAnalysis.Ok(outside, -35f, -20f),  // raw = +17
                ClipAnalysis.Ok(boundary, -30f, -18f)  // raw = +12, exactly at the threshold
            }, -18f);

            Assert.IsFalse(results.First(r => r.Clip == inside).IsOutlier);
            Assert.IsTrue(results.First(r => r.Clip == outside).IsOutlier);
            // GainSolver uses strict '>' against OutlierThresholdDb, so a raw gain of
            // exactly 12 sits inside the band and must not be flagged.
            Assert.IsFalse(results.First(r => r.Clip == boundary).IsOutlier);
        }

        [Test]
        public void OutlierFlag_TriggersOnLargeNegativeRawGainToo()
        {
            var clip = MakeClip("blaring");
            var results = Solve(new[] { ClipAnalysis.Ok(clip, -2f, -0.1f) }, -18f);

            Assert.AreEqual(-16f, results[0].RawGainDb, 1e-4f);
            Assert.IsTrue(results[0].IsOutlier);
        }

        [Test]
        public void Solve_WithNoAnalyzableClips_ReturnsZeroGainsWithoutThrowing()
        {
            var results = Solve(new[]
            {
                ClipAnalysis.Silent(MakeClip("s1")),
                ClipAnalysis.Unanalyzable(MakeClip("s2"), "streaming")
            }, -18f);

            Assert.AreEqual(2, results.Count);
            foreach (var result in results)
            {
                Assert.AreEqual(0f, result.FinalGainDb, 1e-4f);
            }
        }

        [Test]
        public void Solve_WithEmptyInput_ReturnsAnEmptyList()
        {
            Assert.AreEqual(0, Solve(new ClipAnalysis[0], -18f).Count);
        }
    }
}
