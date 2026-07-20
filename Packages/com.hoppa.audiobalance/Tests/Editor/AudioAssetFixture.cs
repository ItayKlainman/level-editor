using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    /// <summary>
    /// Creates and cleans up temporary asset-backed <see cref="AudioClip"/>s for tests that need
    /// a real AssetDatabase identity (guid + path). <see cref="AudioClip.Create"/> -- what every
    /// other test in this package uses -- builds a procedural clip with no asset path, so
    /// <see cref="LoudnessCache.KeyFor"/> always returns an invalid key for it (see
    /// <see cref="LoudnessCacheKey.IsValid"/>), which structurally bypasses the cache. This
    /// fixture writes a minimal 16-bit PCM WAV into a uniquely-named temp folder under
    /// <c>Assets/</c>, imports it with <see cref="AssetDatabase.ImportAsset(string, ImportAssetOptions)"/>,
    /// and deletes the whole folder (every clip it created, plus every <c>.meta</c> sidecar, in
    /// one call) when disposed. Nothing it creates is ever committed.
    ///
    /// <para>
    /// One instance owns one temp folder, named with a fresh GUID so a run that fails mid-way
    /// and leaves a folder behind can never collide with a later run, another test class, or any
    /// real project asset.
    /// </para>
    /// </summary>
    public sealed class AudioAssetFixture : IDisposable
    {
        private readonly string _folder;
        private bool _disposed;

        public AudioAssetFixture()
        {
            var name = "HoppaAudioBalanceTestTemp_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name);
            _folder = "Assets/" + name;
        }

        /// <summary>This fixture's temp root folder, e.g. for passing to <c>LoudnessAnalyzer.FindClips</c>.</summary>
        public string FolderPath => _folder;

        /// <summary>
        /// Creates (if missing) a subfolder of this fixture's temp root and returns its
        /// project-relative path. Lets a test build overlapping / sibling folder structures for
        /// <c>FindClips</c> coverage without hand-managing paths.
        /// </summary>
        public string CreateSubFolder(string name)
        {
            if (!AssetDatabase.IsValidFolder(_folder + "/" + name))
            {
                AssetDatabase.CreateFolder(_folder, name);
            }

            return _folder + "/" + name;
        }

        /// <summary>
        /// Writes <paramref name="interleaved"/> as a 16-bit PCM WAV named
        /// <paramref name="clipName"/>.wav under <paramref name="folder"/> (this fixture's root
        /// when null/empty), imports it, and returns the resulting asset-backed
        /// <see cref="AudioClip"/>.
        /// </summary>
        public AudioClip CreateClip(string clipName, float[] interleaved, int channels, int sampleRate,
            string folder = null)
        {
            var targetFolder = string.IsNullOrEmpty(folder) ? _folder : folder;
            var relativePath = targetFolder + "/" + clipName + ".wav";

            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            var absolutePath = Path.Combine(projectRoot, relativePath);

            WriteWav(absolutePath, interleaved, channels, sampleRate);
            AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceSynchronousImport);

            return AssetDatabase.LoadAssetAtPath<AudioClip>(relativePath);
        }

        /// <summary>Convenience wrapper: a <see cref="SignalFactory.Sine"/> tone as a WAV clip.</summary>
        public AudioClip CreateTone(string clipName, double peakDbfs, double seconds, int channels = 2,
            int sampleRate = 48000, string folder = null)
        {
            return CreateClip(clipName, SignalFactory.Sine(peakDbfs, seconds, channels, sampleRate),
                channels, sampleRate, folder);
        }

        /// <summary>Convenience wrapper: silence as a WAV clip.</summary>
        public AudioClip CreateSilence(string clipName, double seconds, int channels = 2,
            int sampleRate = 48000, string folder = null)
        {
            return CreateClip(clipName, SignalFactory.Silence(seconds, channels, sampleRate),
                channels, sampleRate, folder);
        }

        /// <summary>
        /// Deletes this fixture's whole temp folder -- every clip it created, every subfolder,
        /// every <c>.meta</c> sidecar, and the root folder itself -- in one call. Idempotent, so
        /// it is safe to call from a <c>[TearDown]</c> even after a test failed mid-way: there is
        /// nothing partial left to leak, and calling it twice is a no-op the second time.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (AssetDatabase.IsValidFolder(_folder))
            {
                AssetDatabase.DeleteAsset(_folder);
            }
        }

        private static void WriteWav(string absolutePath, float[] interleaved, int channels, int sampleRate)
        {
            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var dataSize = interleaved.Length * 2;
            var byteRate = sampleRate * channels * 2;
            var blockAlign = (short)(channels * 2);

            using (var stream = new FileStream(absolutePath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataSize);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));

                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16); // PCM fmt chunk size
                writer.Write((short)1); // PCM format tag
                writer.Write((short)channels);
                writer.Write(sampleRate);
                writer.Write(byteRate);
                writer.Write(blockAlign);
                writer.Write((short)16); // bits per sample

                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataSize);

                foreach (var sample in interleaved)
                {
                    var clamped = Mathf.Clamp(sample, -1f, 1f);
                    writer.Write((short)Mathf.RoundToInt(clamped * short.MaxValue));
                }
            }
        }
    }
}
