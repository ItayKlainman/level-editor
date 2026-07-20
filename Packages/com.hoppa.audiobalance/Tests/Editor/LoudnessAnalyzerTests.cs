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

        private AudioAssetFixture _assets;

        [SetUp]
        public void SetUp()
        {
            _assets = new AudioAssetFixture();
        }

        [TearDown]
        public void TearDown()
        {
            _assets.Dispose();
        }

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
        public void Analyze_WithAnAssetBackedClip_StoresIntoTheCacheOnAMiss()
        {
            // Asset-backed via AudioAssetFixture -- unlike every clip above, this one has a real
            // AssetDatabase guid/path, so LoudnessCache.KeyFor returns a VALID key and Analyze's
            // store branch (previously dead in every test in this suite) actually runs.
            var clip = _assets.CreateTone("tone", -23.0, 2.0);
            var cache = LoudnessCache.Load(TempCachePath());
            try
            {
                var key = LoudnessCache.KeyFor(clip, MeasureMode.Integrated);
                Assert.IsTrue(key.IsValid, "Precondition: an imported asset must have a valid key.");
                Assert.IsFalse(cache.TryGet(key, out _), "Precondition: the cache must start empty for this key.");

                var analysis = LoudnessAnalyzer.Analyze(clip, MeasureMode.Integrated, cache);

                Assert.AreEqual(ClipStatus.Ok, analysis.Status);
                Assert.IsTrue(cache.TryGet(key, out var stored),
                    "Analyze must Put the fresh measurement into the cache on a miss.");
                Assert.AreEqual(analysis.Lufs, stored.Lufs, 1e-4f);
                Assert.AreEqual(analysis.PeakDb, stored.PeakDb, 1e-4f);
                Assert.AreEqual((int)ClipStatus.Ok, stored.Status);
            }
            finally
            {
                DeleteCacheFile(cache);
            }
        }

        [Test]
        public void Analyze_WithAnAssetBackedClip_ReturnsTheCachedValueOnAHit_NotARemeasurement()
        {
            // Proves a GENUINE hit, not merely "the code ran without throwing": seed the cache
            // with a value that is deliberately wrong for this clip's real content, then assert
            // Analyze returns exactly that wrong value. If Analyze were re-measuring instead of
            // reading the cache, it would report the clip's true ~-23 LUFS, not the seeded -5.
            var clip = _assets.CreateTone("tone", -23.0, 2.0);
            var cache = LoudnessCache.Load(TempCachePath());
            try
            {
                var key = LoudnessCache.KeyFor(clip, MeasureMode.Integrated);
                Assert.IsTrue(key.IsValid, "Precondition: an imported asset must have a valid key.");

                cache.Put(key, new CachedLoudness { Status = (int)ClipStatus.Ok, Lufs = -5f, PeakDb = -1f });

                var analysis = LoudnessAnalyzer.Analyze(clip, MeasureMode.Integrated, cache);

                Assert.AreEqual(ClipStatus.Ok, analysis.Status);
                Assert.AreEqual(-5f, analysis.Lufs, 1e-4f,
                    "A real measurement of this clip would read ~-23 LUFS, not the seeded -5 -- " +
                    "this proves the value came from the cache.");
                Assert.AreEqual(-1f, analysis.PeakDb, 1e-4f);
            }
            finally
            {
                DeleteCacheFile(cache);
            }
        }

        [Test]
        public void Analyze_OnACacheHit_ForASilentClip_PreservesTheReason()
        {
            // Regression: CachedLoudness has no Reason field, so a naive cache-hit reconstruction
            // drops it -- a freshly-measured silent clip reports Reason == "silent", but the same
            // clip served from cache reported Reason == null. Both calls here share one cache
            // instance, so the second is a genuine hit (see the seeded-value test above for the
            // stronger proof of that mechanism).
            var clip = _assets.CreateSilence("quiet", 1.0);
            var cache = LoudnessCache.Load(TempCachePath());
            try
            {
                var first = LoudnessAnalyzer.Analyze(clip, MeasureMode.Integrated, cache);
                Assert.AreEqual(ClipStatus.Silent, first.Status);
                Assert.AreEqual("silent", first.Reason, "Precondition: a fresh measurement reports the reason.");

                var second = LoudnessAnalyzer.Analyze(clip, MeasureMode.Integrated, cache);

                Assert.AreEqual(ClipStatus.Silent, second.Status);
                Assert.AreEqual("silent", second.Reason,
                    "A cache hit must preserve the reason -- a UI must not show 'silent' on the " +
                    "first run and blank on the next window open for the identical clip.");
            }
            finally
            {
                DeleteCacheFile(cache);
            }
        }

        [Test]
        public void Analyze_WithACorruptCachedStatus_ReMeasuresInsteadOfCastingOutOfRange()
        {
            // Regression: the cache is hand-editable JSON under Library/. A corrupt or
            // forward-version entry (Status: 99, out of range for ClipStatus) must not be blindly
            // cast -- an unchecked (ClipStatus)99 produces a value no downstream switch handles.
            var clip = _assets.CreateTone("tone", -23.0, 2.0);
            var cache = LoudnessCache.Load(TempCachePath());
            try
            {
                var key = LoudnessCache.KeyFor(clip, MeasureMode.Integrated);
                Assert.IsTrue(key.IsValid, "Precondition: an imported asset must have a valid key.");

                cache.Put(key, new CachedLoudness { Status = 99, Lufs = -1f, PeakDb = -1f });

                var analysis = LoudnessAnalyzer.Analyze(clip, MeasureMode.Integrated, cache);

                Assert.AreEqual(ClipStatus.Ok, analysis.Status,
                    "An out-of-range cached Status must be treated as a miss and re-measured, " +
                    "not cast into an undefined ClipStatus value.");
                Assert.AreEqual(-23f, analysis.Lufs, 0.2f,
                    "The re-measured value must be the clip's real loudness, not the corrupt -1 seeded above.");
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
        public void FindClips_ReturnsAssetBackedClipsSortedByName()
        {
            // The entire positive path had zero coverage before AudioAssetFixture: FindAssets,
            // LoadAssetAtPath, and the name sort. AudioClip.Create clips (every other test in
            // this file) never reach this method's real logic because they are never under a
            // project folder to begin with.
            var folder = _assets.CreateSubFolder("Positive");
            _assets.CreateTone("zeta", -20.0, 0.2, folder: folder);
            _assets.CreateTone("alpha", -20.0, 0.2, folder: folder);

            var clips = LoudnessAnalyzer.FindClips(new[] { folder });

            Assert.AreEqual(2, clips.Count);
            Assert.AreEqual("alpha", clips[0].name);
            Assert.AreEqual("zeta", clips[1].name);
        }

        [Test]
        public void FindClips_DedupesWhenFoldersOverlap()
        {
            // Passing both a parent and its own child folder must not return the child's clip
            // twice -- AssetDatabase.FindAssets searches each given folder recursively, so the
            // same guid is discovered once via the parent and once via the child.
            var parent = _assets.FolderPath;
            var child = _assets.CreateSubFolder("Nested");
            _assets.CreateTone("onlyone", -20.0, 0.2, folder: child);

            var clips = LoudnessAnalyzer.FindClips(new[] { parent, child });

            Assert.AreEqual(1, clips.Count, "An overlapping folder pair must not duplicate the same clip.");
            Assert.AreEqual("onlyone", clips[0].name);
        }

        [Test]
        public void FindClips_TiesBrokenByAssetPathForEqualNames()
        {
            // Regression: List.Sort's comparator was name-only, so two clips both named "click"
            // in different folders had a nondeterministic relative order -- which churns the
            // generated gain table's diff between runs. List.Sort is not a stable sort, but it IS
            // deterministic for a given pre-sort order, and .NET's small-array introsort path
            // (insertion sort, used below its ~16-element threshold) happens to preserve input
            // order for compare-equal items -- so simply feeding folders in ascending order would
            // pass "by accident" even without a path tiebreak. Feed them in DESCENDING order
            // instead: a name-only comparator has no reason to touch already-equal items, so
            // without a path tiebreak the result stays in (wrong) descending order regardless of
            // how the sort algorithm treats ties. Only an explicit path-ascending tiebreak can
            // produce the ascending order asserted below.
            var expectedPaths = new System.Collections.Generic.List<string>();
            var searchFolders = new System.Collections.Generic.List<string>();
            for (var i = 11; i >= 0; i--)
            {
                var folder = _assets.CreateSubFolder("Tie" + i.ToString("D2"));
                var clip = _assets.CreateTone("click", -20.0, 0.1, folder: folder);
                expectedPaths.Add(UnityEditor.AssetDatabase.GetAssetPath(clip));
                searchFolders.Add(folder);
            }
            expectedPaths.Sort((a, b) => string.CompareOrdinal(a, b));

            var clips = LoudnessAnalyzer.FindClips(searchFolders);

            Assert.AreEqual(12, clips.Count);
            for (var i = 0; i < clips.Count; i++)
            {
                Assert.AreEqual("click", clips[i].name);
                Assert.AreEqual(expectedPaths[i], UnityEditor.AssetDatabase.GetAssetPath(clips[i]),
                    "Equal-named clips must be tiebroken by asset path so the order is deterministic.");
            }
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
