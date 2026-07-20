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
        public void TryGet_MissesForAnUnknownGuid()
        {
            var cache = LoudnessCache.Load(_path);

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
            Assert.AreEqual((int)ClipStatus.Ok, value.Status);
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
        public void Clear_DropsAllEntries()
        {
            var cache = LoudnessCache.Load(_path);
            cache.Put("guid-a", 1000, 5555, Sample());
            cache.Clear();

            Assert.IsFalse(cache.TryGet("guid-a", 1000, 5555, out _));
        }
    }
}
