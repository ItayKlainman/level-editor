using System;
using System.IO;
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class LoudnessCacheTests
    {
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _path = Path.Combine(Path.GetTempPath(), "hoppa-audiobalance-cache-test.json");
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }

        private static CachedLoudness Sample()
        {
            return new CachedLoudness
            {
                Status = (int)ClipStatus.Ok,
                Lufs = -21.5f,
                PeakDb = -3f
            };
        }

        private static LoudnessCacheKey Key(string guid, long length, long ticks, MeasureMode mode = MeasureMode.Integrated)
        {
            return new LoudnessCacheKey(guid, length, ticks, mode);
        }

        [Test]
        public void TryGet_HitsOnUnchangedFileIdentity()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put(Key("guid-a", 1000, 5555), Sample());

            Assert.IsTrue(cache.TryGet(Key("guid-a", 1000, 5555), out var value));
            Assert.AreEqual(-21.5f, value.Lufs, 1e-4f);
        }

        [Test]
        public void TryGet_MissesWhenFileLengthChanges()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put(Key("guid-a", 1000, 5555), Sample());

            Assert.IsFalse(cache.TryGet(Key("guid-a", 2000, 5555), out _));
        }

        [Test]
        public void TryGet_MissesWhenModifiedTimeChanges()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put(Key("guid-a", 1000, 5555), Sample());

            Assert.IsFalse(cache.TryGet(Key("guid-a", 1000, 9999), out _));
        }

        [Test]
        public void TryGet_MissesWhenMeasureModeDiffers()
        {
            // The same clip measured Integrated vs MomentaryMax is a different answer -- Mode is
            // a real field on the key, not folded into the guid.
            var cache = LoudnessCache.Load(_path);
            cache.Put(Key("guid-a", 1000, 5555, MeasureMode.Integrated), Sample());

            Assert.IsFalse(cache.TryGet(Key("guid-a", 1000, 5555, MeasureMode.MomentaryMax), out _),
                "The same clip under a different measure mode must be a different cache entry.");
        }

        [Test]
        public void TryGet_MissesForAnUnknownGuid()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put(Key("guid-a", 1000, 5555), Sample());

            Assert.IsFalse(cache.TryGet(Key("never-seen", 1, 1), out _));
        }

        [Test]
        public void TryGet_OnAnInvalidKey_AlwaysMisses()
        {
            var cache = LoudnessCache.Load(_path);

            Assert.IsFalse(cache.TryGet(default, out _));
        }

        [Test]
        public void Put_WithAnInvalidKey_IsIgnored()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put(default, Sample());

            Assert.IsFalse(cache.TryGet(default, out _));
        }

        [Test]
        public void SaveThenLoad_RoundTripsEntries()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put(Key("guid-a", 1000, 5555), Sample());
            cache.Save();

            var reloaded = LoudnessCache.Load(_path);

            Assert.IsTrue(reloaded.TryGet(Key("guid-a", 1000, 5555), out var value));
            Assert.AreEqual(-21.5f, value.Lufs, 1e-4f);
            Assert.AreEqual(-3f, value.PeakDb, 1e-4f);
            Assert.AreEqual((int)ClipStatus.Ok, value.Status);
        }

        [Test]
        public void SaveThenLoad_RoundTripsMultipleEntriesIncludingSameGuidDifferentMode()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put(Key("guid-a", 1000, 5555, MeasureMode.Integrated), Sample());
            cache.Put(Key("guid-a", 1000, 5555, MeasureMode.MomentaryMax), new CachedLoudness
            {
                Status = (int)ClipStatus.Ok,
                Lufs = -12f,
                PeakDb = -1f
            });
            cache.Put(Key("guid-b", 2000, 6666, MeasureMode.Integrated), new CachedLoudness
            {
                Status = (int)ClipStatus.Silent,
                Lufs = -70f,
                PeakDb = -90f
            });
            cache.Save();

            var reloaded = LoudnessCache.Load(_path);

            Assert.IsTrue(reloaded.TryGet(Key("guid-a", 1000, 5555, MeasureMode.Integrated), out var integrated));
            Assert.AreEqual(-21.5f, integrated.Lufs, 1e-4f);

            Assert.IsTrue(reloaded.TryGet(Key("guid-a", 1000, 5555, MeasureMode.MomentaryMax), out var momentary));
            Assert.AreEqual(-12f, momentary.Lufs, 1e-4f);

            Assert.IsTrue(reloaded.TryGet(Key("guid-b", 2000, 6666, MeasureMode.Integrated), out var b));
            Assert.AreEqual((int)ClipStatus.Silent, b.Status);
            Assert.AreEqual(-70f, b.Lufs, 1e-4f);
            Assert.AreEqual(-90f, b.PeakDb, 1e-4f);
        }

        [Test]
        public void Put_OverwritesAnExistingEntryForTheSameGuidAndMode()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put(Key("guid-a", 1000, 5555), Sample());
            cache.Put(Key("guid-a", 1000, 6666), new CachedLoudness { Lufs = -9f });

            Assert.IsFalse(cache.TryGet(Key("guid-a", 1000, 5555), out _),
                "The stale identity must no longer hit.");
            Assert.IsTrue(cache.TryGet(Key("guid-a", 1000, 6666), out var value));
            Assert.AreEqual(-9f, value.Lufs, 1e-4f);
        }

        [Test]
        public void Put_WithNullValue_IsIgnored()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put(Key("guid-a", 1000, 5555), null);

            Assert.IsFalse(cache.TryGet(Key("guid-a", 1000, 5555), out _));
        }

        [Test]
        public void Put_WithNullValue_DoesNotResurrectAsAFakeOkEntryAfterSaveLoad()
        {
            // Regression: JsonUtility round-trips a stored null as a zero-filled, non-null
            // CachedLoudness (Status 0 == ClipStatus.Ok, Lufs 0f) -- a false "measured
            // successfully at 0 LUFS" that would flow into the gain solver. Put() must refuse
            // the null before it ever reaches the serialized store.
            var cache = LoudnessCache.Load(_path);
            cache.Put(Key("guid-a", 1000, 5555), null);
            cache.Save();

            var reloaded = LoudnessCache.Load(_path);

            Assert.IsFalse(reloaded.TryGet(Key("guid-a", 1000, 5555), out _));
        }

        [Test]
        public void Put_CopiesTheValue_SoCallerMutationAfterwardsDoesNotAffectTheCache()
        {
            var cache = LoudnessCache.Load(_path);
            var value = Sample();
            var key = Key("guid-a", 1000, 5555);
            cache.Put(key, value);

            value.Lufs = -1f;

            Assert.IsTrue(cache.TryGet(key, out var stored));
            Assert.AreEqual(-21.5f, stored.Lufs, 1e-4f,
                "Put must store a copy; mutating the caller's instance afterwards must not leak into the cache.");
        }

        [Test]
        public void TryGet_ReturnsACopy_SoCallerMutationDoesNotAffectTheCache()
        {
            var cache = LoudnessCache.Load(_path);
            var key = Key("guid-a", 1000, 5555);
            cache.Put(key, Sample());

            Assert.IsTrue(cache.TryGet(key, out var first));
            first.Lufs = -1f;

            Assert.IsTrue(cache.TryGet(key, out var second));
            Assert.AreEqual(-21.5f, second.Lufs, 1e-4f,
                "TryGet must return a copy; mutating the caller's instance afterwards must not leak into the cache.");
        }

        [Test]
        public void Load_OnCorruptFile_DegradesToAnEmptyCacheWithoutThrowing()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, "{ this is not valid json ][");

            LoudnessCache cache = null;
            Assert.DoesNotThrow(() => cache = LoudnessCache.Load(_path));
            Assert.IsFalse(cache.TryGet(Key("anything", 1, 1), out _));
        }

        [Test]
        public void Load_OnMissingFile_ReturnsAnEmptyCache()
        {
            var cache = LoudnessCache.Load(_path);

            Assert.IsFalse(cache.TryGet(Key("anything", 1, 1), out _));
        }

        [Test]
        public void Save_CreatesTheParentDirectoryWhenMissing()
        {
            // All other tests write straight to Path.GetTempPath(), which always exists, so
            // Directory.CreateDirectory is never exercised there -- yet DefaultPath
            // ("Library/HoppaAudioBalance/...") does NOT exist on a fresh clone. Prove the
            // nested-directory-creation path with a directory that genuinely does not exist yet.
            var nestedDir = Path.Combine(Path.GetTempPath(), "hoppa-audiobalance-nested-" + Guid.NewGuid().ToString("N"));
            var nestedPath = Path.Combine(nestedDir, "loudness-cache.json");
            Assert.IsFalse(Directory.Exists(nestedDir), "Precondition: the nested directory must not already exist.");

            try
            {
                var cache = LoudnessCache.Load(nestedPath);
                cache.Put(Key("guid-a", 1000, 5555), Sample());
                cache.Save();

                Assert.IsTrue(File.Exists(nestedPath));
            }
            finally
            {
                if (Directory.Exists(nestedDir))
                {
                    Directory.Delete(nestedDir, recursive: true);
                }
            }
        }

        [Test]
        public void Save_CleansUpTheOrphanTempFileWhenTheFinalSwapFails()
        {
            // _path points at an existing DIRECTORY, not a file: File.Exists(_path) is false for
            // a directory, so Save() takes the File.Move branch, which throws because the
            // destination already exists as a directory. That deterministically forces the swap
            // to fail without any mocking.
            var conflictDir = Path.Combine(Path.GetTempPath(), "hoppa-audiobalance-conflict-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(conflictDir);

            try
            {
                var cache = LoudnessCache.Load(conflictDir);
                cache.Put(Key("guid-a", 1000, 5555), Sample());

                Assert.DoesNotThrow(() => cache.Save(), "A failed final swap must be caught and logged, not thrown.");

                Assert.IsFalse(File.Exists(conflictDir + ".tmp"),
                    "A failed final swap must not leave an orphan .tmp file behind.");
            }
            finally
            {
                if (Directory.Exists(conflictDir))
                {
                    Directory.Delete(conflictDir, recursive: true);
                }
            }
        }

        [Test]
        public void Clear_DropsAllEntries()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put(Key("guid-a", 1000, 5555), Sample());
            cache.Clear();

            Assert.IsFalse(cache.TryGet(Key("guid-a", 1000, 5555), out _));
        }

        [Test]
        public void Clear_AlsoDeletesTheCacheFileOnDisk()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put(Key("guid-a", 1000, 5555), Sample());
            cache.Save();
            Assert.IsTrue(File.Exists(_path), "Precondition: the cache file must exist before Clear().");

            cache.Clear();

            Assert.IsFalse(File.Exists(_path),
                "Clear() must delete the on-disk file too, or a reload without an intervening Save() resurrects everything.");
        }

        [Test]
        public void Clear_AlsoDeletesAnOrphanTempFile()
        {
            var cache = LoudnessCache.Load(_path);
            File.WriteAllText(_path + ".tmp", "leftover from a crashed save");

            cache.Clear();

            Assert.IsFalse(File.Exists(_path + ".tmp"));
        }

        [Test]
        public void KeyFor_OnANullClip_ReturnsAnInvalidKey()
        {
            var key = LoudnessCache.KeyFor(null, MeasureMode.Integrated);

            Assert.IsFalse(key.IsValid);
        }

        [Test]
        public void KeyFor_OnAProceduralClipWithNoAssetPath_ReturnsAnInvalidKey()
        {
            var clip = AudioClip.Create("procedural", 4, 1, 48000, false);

            var key = LoudnessCache.KeyFor(clip, MeasureMode.Integrated);

            Assert.IsFalse(key.IsValid);
        }

        [Test]
        public void KeyForPaths_MissesWhenOnlyTheMetaFileIsTouched()
        {
            // Non-vacuous proof of the CRITICAL fix: two REAL files on disk, standing in for an
            // asset + its .meta. Touch ONLY the meta file's mtime -- the "asset" is byte-for-byte
            // and timestamp-for-timestamp untouched, exactly like a Force-To-Mono importer edit
            // leaves the source .wav untouched. If KeyForPaths ever regresses to deriving Ticks
            // from the asset file alone, this test fails.
            var dir = Path.Combine(Path.GetTempPath(), "hoppa-audiobalance-metatest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var assetPath = Path.Combine(dir, "clip.wav");
            var metaPath = assetPath + ".meta";

            try
            {
                File.WriteAllText(assetPath, "stand-in for audio bytes -- only file identity matters here");
                File.WriteAllText(metaPath, "stand-in for importer settings");

                var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(assetPath, t0);
                File.SetLastWriteTimeUtc(metaPath, t0);

                var before = LoudnessCache.KeyForPaths("guid-meta-test", assetPath, metaPath, MeasureMode.Integrated);

                var cache = LoudnessCache.Load(_path);
                cache.Put(before, Sample());
                Assert.IsTrue(cache.TryGet(before, out _), "Precondition: the freshly-put key must hit.");

                // Touch ONLY the .meta file.
                File.SetLastWriteTimeUtc(metaPath, t0.AddSeconds(1));

                var after = LoudnessCache.KeyForPaths("guid-meta-test", assetPath, metaPath, MeasureMode.Integrated);

                Assert.IsFalse(cache.TryGet(after, out _),
                    "A meta-only touch must change the derived key and invalidate the cache.");
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }

        [Test]
        public void KeyForPaths_HitsWhenNeitherFileChanges()
        {
            var dir = Path.Combine(Path.GetTempPath(), "hoppa-audiobalance-metatest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var assetPath = Path.Combine(dir, "clip.wav");
            var metaPath = assetPath + ".meta";

            try
            {
                File.WriteAllText(assetPath, "stand-in for audio bytes");
                File.WriteAllText(metaPath, "stand-in for importer settings");

                var key1 = LoudnessCache.KeyForPaths("guid-meta-test", assetPath, metaPath, MeasureMode.Integrated);
                var key2 = LoudnessCache.KeyForPaths("guid-meta-test", assetPath, metaPath, MeasureMode.Integrated);

                var cache = LoudnessCache.Load(_path);
                cache.Put(key1, Sample());

                Assert.IsTrue(cache.TryGet(key2, out _), "Re-deriving the key from unchanged files must still hit.");
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }
    }
}
