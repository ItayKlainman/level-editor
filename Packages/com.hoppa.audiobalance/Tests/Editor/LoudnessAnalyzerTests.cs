using System;
using UnityEditor;
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class LoudnessAnalyzerTests
    {
        private const int Rate = 48000;

        private static AudioClip MakeToneClip(string name, double peakDbfs, double seconds)
        {
            var frames = (int)(seconds * Rate);
            var clip = AudioClip.Create(name, frames, 2, Rate, false);
            clip.SetData(SignalFactory.Sine(peakDbfs, seconds, 2, Rate), 0);
            return clip;
        }

        [Test]
        public void Analyze_OnAToneClip_ReportsOkWithTheExpectedLoudness()
        {
            var clip = MakeToneClip("tone", -23.0, 4.0);

            var analysis = LoudnessAnalyzer.Analyze(clip, MeasureMode.Integrated, null);

            Assert.AreEqual(ClipStatus.Ok, analysis.Status);
            Assert.AreEqual(-23f, analysis.Lufs, 0.2f);
            Assert.AreSame(clip, analysis.Clip);
        }

        [Test]
        public void Analyze_PopulatesPeakDiagnostics()
        {
            var clip = MakeToneClip("tone", -6.0, 1.0);

            var analysis = LoudnessAnalyzer.Analyze(clip, MeasureMode.MomentaryMax, null);

            Assert.AreEqual(-6f, analysis.PeakDb, 0.2f);
        }

        [Test]
        public void Analyze_OnASilentClip_ReportsSilent()
        {
            var clip = AudioClip.Create("quiet", Rate * 2, 2, Rate, false);

            var analysis = LoudnessAnalyzer.Analyze(clip, MeasureMode.Integrated, null);

            Assert.AreEqual(ClipStatus.Silent, analysis.Status);
        }

        [Test]
        public void Analyze_OnNullClip_ReportsUnanalyzable()
        {
            var analysis = LoudnessAnalyzer.Analyze(null, MeasureMode.Integrated, null);

            Assert.AreEqual(ClipStatus.Unanalyzable, analysis.Status);
        }

        [Test]
        public void Analyze_HonoursTheMeasureMode()
        {
            // Loud burst then a long quiet tail: the two modes must disagree.
            var seconds = 4.5;
            var frames = (int)(seconds * Rate);
            var clip = AudioClip.Create("oneshot", frames, 2, Rate, false);
            clip.SetData(SignalFactory.Concat(
                SignalFactory.Sine(-18.0, 0.5, 2, Rate),
                SignalFactory.Sine(-50.0, 4.0, 2, Rate)), 0);

            var integrated = LoudnessAnalyzer.Analyze(clip, MeasureMode.Integrated, null);
            var momentary = LoudnessAnalyzer.Analyze(clip, MeasureMode.MomentaryMax, null);

            Assert.Greater(momentary.Lufs, integrated.Lufs);
        }

        [Test]
        public void Analyze_WithACache_ButAProceduralClip_BypassesTheCacheWithoutThrowing()
        {
            // Procedural clips have no asset path, so LoudnessCache.KeyFor returns an invalid
            // key and Analyze must bypass the cache for them (see LoudnessCacheKey.IsValid).
            // This proves that bypass is safe -- a non-null cache handed an unaddressable clip
            // neither throws nor corrupts the result -- not that a hit occurred.
            var clip = MakeToneClip("tone", -23.0, 4.0);
            var cache = LoudnessCache.Load(TempCachePath());
            try
            {
                var key = LoudnessCache.KeyFor(clip, MeasureMode.Integrated);
                Assert.IsFalse(key.IsValid, "Precondition: a procedural clip has no asset path.");

                var first = LoudnessAnalyzer.Analyze(clip, MeasureMode.Integrated, cache);
                var second = LoudnessAnalyzer.Analyze(clip, MeasureMode.Integrated, cache);

                Assert.AreEqual(ClipStatus.Ok, first.Status);
                Assert.AreEqual(first.Lufs, second.Lufs, 1e-4f);
            }
            finally
            {
                DeleteCacheFile(cache);
            }
        }

        [Test]
        public void ShouldCache_ForOk_ReturnsTrue()
        {
            Assert.IsTrue(LoudnessAnalyzer.ShouldCache(ClipStatus.Ok));
        }

        [Test]
        public void ShouldCache_ForSilent_ReturnsTrue()
        {
            // Silence is a genuine, stable measurement of the clip's actual content -- unlike
            // Unanalyzable, re-measuring it later would produce the same answer, so there is no
            // harm (and real value) in caching it.
            Assert.IsTrue(LoudnessAnalyzer.ShouldCache(ClipStatus.Silent));
        }

        [Test]
        public void ShouldCache_ForUnanalyzable_ReturnsFalse()
        {
            // ClipSampleReader.LoadPendingError's own message tells the user to "re-run the
            // analysis once the clip finishes loading" -- but re-running does not change the
            // cache key (guid, length, ticks, mode), so a cached Unanalyzable would be served
            // back forever and the remedy the error promises would be impossible. Also:
            // CachedLoudness has no Reason field, so a cache hit would silently drop WHY the
            // clip failed. Unanalyzable must never be persisted.
            Assert.IsFalse(LoudnessAnalyzer.ShouldCache(ClipStatus.Unanalyzable));
        }

        [Test]
        public void Analyze_ForAnUnanalyzableProceduralClip_DoesNotThrowWhenGivenACache()
        {
            // A null clip can never have a valid cache key (LoudnessCache.KeyFor rejects it
            // outright), so this cannot prove ShouldCache's guard fires through the cache --
            // that would need an asset-backed clip that fails to read, which this test suite
            // cannot build without a committed audio fixture (see the untested-branch note on
            // Analyze). What this DOES prove: an Unanalyzable result flowing through the
            // cache-population branch with a real cache instance is safe.
            var cache = LoudnessCache.Load(TempCachePath());
            try
            {
                var analysis = LoudnessAnalyzer.Analyze(null, MeasureMode.Integrated, cache);

                Assert.AreEqual(ClipStatus.Unanalyzable, analysis.Status);
            }
            finally
            {
                DeleteCacheFile(cache);
            }
        }

        [Test]
        public void FindClips_WithNullOrEmptyFolders_ReturnsAnEmptyList()
        {
            Assert.AreEqual(0, LoudnessAnalyzer.FindClips(null).Count);
            Assert.AreEqual(0, LoudnessAnalyzer.FindClips(new string[0]).Count);
        }

        [Test]
        public void FindClips_SkipsFoldersThatDoNotExist()
        {
            var clips = LoudnessAnalyzer.FindClips(new[] { "Assets/ThisFolderDoesNotExist" });

            Assert.AreEqual(0, clips.Count);
        }

        [Test]
        public void PruneMissingClips_RemovesEntriesForGuidsThatNoLongerResolveToAnAsset()
        {
            var cache = LoudnessCache.Load(TempCachePath());
            try
            {
                // A syntactically-valid but never-assigned guid: AssetDatabase.GUIDToAssetPath
                // deterministically returns "" for it regardless of project state.
                var staleGuid = Guid.NewGuid().ToString("N");
                var staleKey = new LoudnessCacheKey(staleGuid, 100, 200, MeasureMode.Integrated);
                cache.Put(staleKey, new CachedLoudness { Status = (int)ClipStatus.Ok, Lufs = -20f, PeakDb = -3f });

                // A real, currently-resolvable asset in the project -- must survive the prune.
                var liveGuid = AssetDatabase.AssetPathToGUID("Packages/com.hoppa.audiobalance/package.json");
                Assert.IsFalse(string.IsNullOrEmpty(liveGuid), "Precondition: package.json must have a real guid.");
                var liveKey = new LoudnessCacheKey(liveGuid, 100, 200, MeasureMode.Integrated);
                cache.Put(liveKey, new CachedLoudness { Status = (int)ClipStatus.Ok, Lufs = -18f, PeakDb = -2f });

                var removed = LoudnessAnalyzer.PruneMissingClips(cache);

                Assert.AreEqual(1, removed);
                Assert.IsFalse(cache.TryGet(staleKey, out _), "The stale guid's entry must be gone.");
                Assert.IsTrue(cache.TryGet(liveKey, out _), "The live guid's entry must survive.");
            }
            finally
            {
                DeleteCacheFile(cache);
            }
        }

        [Test]
        public void PruneMissingClips_OnACacheWithNothingStale_RemovesNothing()
        {
            var cache = LoudnessCache.Load(TempCachePath());
            try
            {
                var liveGuid = AssetDatabase.AssetPathToGUID("Packages/com.hoppa.audiobalance/package.json");
                var liveKey = new LoudnessCacheKey(liveGuid, 100, 200, MeasureMode.Integrated);
                cache.Put(liveKey, new CachedLoudness { Status = (int)ClipStatus.Ok, Lufs = -18f, PeakDb = -2f });

                var removed = LoudnessAnalyzer.PruneMissingClips(cache);

                Assert.AreEqual(0, removed);
                Assert.IsTrue(cache.TryGet(liveKey, out _));
            }
            finally
            {
                DeleteCacheFile(cache);
            }
        }

        [Test]
        public void PruneMissingClips_OnANullCache_ReturnsZeroWithoutThrowing()
        {
            Assert.DoesNotThrow(() => Assert.AreEqual(0, LoudnessAnalyzer.PruneMissingClips(null)));
        }

        private static string TempCachePath()
        {
            return System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "hoppa-audiobalance-analyzer-test-" + Guid.NewGuid().ToString("N") + ".json");
        }

        private static void DeleteCacheFile(LoudnessCache cache)
        {
            cache.Clear();
        }
    }
}
