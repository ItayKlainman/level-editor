using System;
using System.IO;
using Hoppa.AudioBalance.Editor;
using NUnit.Framework;

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

        private static long CombinedTicks(long assetTicks, long metaTicks) => Math.Max(assetTicks, metaTicks);

        [Test]
        public void TryGet_HitsOnUnchangedFileIdentity()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, 5555, Sample());

            Assert.IsTrue(cache.TryGet("guid-a", 1000, 5555, out var value));
            Assert.AreEqual(-21.5f, value.Lufs, 1e-4f);
        }

        [Test]
        public void TryGet_MissesWhenFileLengthChanges()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, 5555, Sample());

            Assert.IsFalse(cache.TryGet("guid-a", 2000, 5555, out _));
        }

        [Test]
        public void TryGet_MissesWhenModifiedTimeChanges()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, 5555, Sample());

            Assert.IsFalse(cache.TryGet("guid-a", 1000, 9999, out _));
        }

        [Test]
        public void TryGet_MissesWhenOnlyTheMetaTimestampChanges()
        {
            // Contract (see LoudnessCache class doc): callers must pass
            // ticks = max(assetTicks, metaTicks) so an importer-setting-only edit (e.g. Force To
            // Mono) invalidates the cache even though the source audio bytes -- and therefore the
            // asset's own length/mtime -- are completely untouched.
            const long assetTicks = 5000;
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, CombinedTicks(assetTicks, 5100), Sample());

            Assert.IsFalse(cache.TryGet("guid-a", 1000, CombinedTicks(assetTicks, 5200), out _),
                "A meta-only touch changes the combined ticks and must invalidate the cache.");
        }

        [Test]
        public void TryGet_MissesForAnUnknownGuid()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, 5555, Sample());

            Assert.IsFalse(cache.TryGet("never-seen", 1, 1, out _));
        }

        [Test]
        public void SaveThenLoad_RoundTripsEntries()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, 5555, Sample());
            cache.Save();

            var reloaded = LoudnessCache.Load(_path);

            Assert.IsTrue(reloaded.TryGet("guid-a", 1000, 5555, out var value));
            Assert.AreEqual(-21.5f, value.Lufs, 1e-4f);
            Assert.AreEqual(-3f, value.PeakDb, 1e-4f);
            Assert.AreEqual((int)ClipStatus.Ok, value.Status);
        }

        [Test]
        public void SaveThenLoad_RoundTripsMultipleEntries()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, 5555, Sample());
            cache.Put("guid-b", 2000, 6666, new CachedLoudness
            {
                Status = (int)ClipStatus.Silent,
                Lufs = -70f,
                PeakDb = -90f
            });
            cache.Save();

            var reloaded = LoudnessCache.Load(_path);

            Assert.IsTrue(reloaded.TryGet("guid-a", 1000, 5555, out var a));
            Assert.AreEqual(-21.5f, a.Lufs, 1e-4f);
            Assert.AreEqual(-3f, a.PeakDb, 1e-4f);

            Assert.IsTrue(reloaded.TryGet("guid-b", 2000, 6666, out var b));
            Assert.AreEqual((int)ClipStatus.Silent, b.Status);
            Assert.AreEqual(-70f, b.Lufs, 1e-4f);
            Assert.AreEqual(-90f, b.PeakDb, 1e-4f);
        }

        [Test]
        public void Put_OverwritesAnExistingEntryForTheSameGuid()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, 5555, Sample());
            cache.Put("guid-a", 1000, 6666, new CachedLoudness { Lufs = -9f });

            Assert.IsFalse(cache.TryGet("guid-a", 1000, 5555, out _),
                "The stale identity must no longer hit.");
            Assert.IsTrue(cache.TryGet("guid-a", 1000, 6666, out var value));
            Assert.AreEqual(-9f, value.Lufs, 1e-4f);
        }

        [Test]
        public void Put_WithNullValue_IsIgnored()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, 5555, null);

            Assert.IsFalse(cache.TryGet("guid-a", 1000, 5555, out _));
        }

        [Test]
        public void Put_WithNullValue_DoesNotResurrectAsAFakeOkEntryAfterSaveLoad()
        {
            // Regression: JsonUtility round-trips a stored null as a zero-filled, non-null
            // CachedLoudness (Status 0 == ClipStatus.Ok, Lufs 0f) -- a false "measured
            // successfully at 0 LUFS" that would flow into the gain solver. Put() must refuse
            // the null before it ever reaches the serialized store.
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, 5555, null);
            cache.Save();

            var reloaded = LoudnessCache.Load(_path);

            Assert.IsFalse(reloaded.TryGet("guid-a", 1000, 5555, out _));
        }

        [Test]
        public void Load_OnCorruptFile_DegradesToAnEmptyCacheWithoutThrowing()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, "{ this is not valid json ][");

            LoudnessCache cache = null;
            Assert.DoesNotThrow(() => cache = LoudnessCache.Load(_path));
            Assert.IsFalse(cache.TryGet("anything", 1, 1, out _));
        }

        [Test]
        public void Load_OnMissingFile_ReturnsAnEmptyCache()
        {
            var cache = LoudnessCache.Load(_path);

            Assert.IsFalse(cache.TryGet("anything", 1, 1, out _));
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
                cache.Put("guid-a", 1000, 5555, Sample());
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
        public void Clear_DropsAllEntries()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, 5555, Sample());
            cache.Clear();

            Assert.IsFalse(cache.TryGet("guid-a", 1000, 5555, out _));
        }

        [Test]
        public void Clear_AlsoDeletesTheCacheFileOnDisk()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, 5555, Sample());
            cache.Save();
            Assert.IsTrue(File.Exists(_path), "Precondition: the cache file must exist before Clear().");

            cache.Clear();

            Assert.IsFalse(File.Exists(_path),
                "Clear() must delete the on-disk file too, or a reload without an intervening Save() resurrects everything.");
        }
    }
}
