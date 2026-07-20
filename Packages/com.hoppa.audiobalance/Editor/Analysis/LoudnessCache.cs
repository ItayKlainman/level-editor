using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Persists measurements so reopening the window is instant and only changed clips are
    /// re-analyzed. Lives under Library/ because it is regenerable and must not be committed.
    ///
    /// <para>
    /// <b>Key derivation is structural, not a documented caller contract.</b> Entries are keyed
    /// on a <see cref="LoudnessCacheKey"/> (guid, file length, ticks, measure mode).
    /// <see cref="KeyFor"/> is the single place that computes <c>Ticks</c>: the combined max of
    /// the source asset's AND its <c>.meta</c>'s last-write time, because what is actually
    /// measured is the decoded <c>AudioClip</c>, which depends on <c>.meta</c> importer settings
    /// (Force To Mono, Quality, Sample Rate Override, ...) as much as it does the source bytes.
    /// Earlier revisions of this class took a bare <c>(guid, length, ticks)</c> tuple and left
    /// the <c>.meta</c>-awareness as an XML-doc note on the caller -- a documented contract with
    /// no enforcement, sitting next to a plan document that then handed the next implementer a
    /// complete, wrong, copy-paste-ready derivation. This class reads files (<c>File.Exists</c>,
    /// <c>ReadAllText</c>, <c>WriteAllText</c>, <c>Delete</c>) throughout already, and the Editor
    /// asmdef is Editor-only, so <c>AssetDatabase</c> is fully available here -- there was no
    /// reason the derivation had to live outside this class.
    /// </para>
    ///
    /// <para>
    /// <b>Not in scope:</b> pruning entries for guids that no longer exist in the project (stale
    /// cache growth for deleted clips). That belongs to the caller (Task 10), not this class.
    /// </para>
    ///
    /// <para>
    /// <b>Threading:</b> backed by a plain <see cref="Dictionary{TKey,TValue}"/> with no
    /// synchronization -- confined to the main thread. If a future caller wants to analyze clips
    /// on a worker thread, that caller is responsible for marshalling back to the main thread
    /// before touching this cache.
    /// </para>
    /// </summary>
    public sealed class LoudnessCache
    {
        /// <summary>
        /// Default on-disk location, used when <see cref="Load"/> is called with no path.
        /// Under <c>Library/</c> because the cache is regenerable and must never be committed.
        /// </summary>
        public const string DefaultPath = "Library/HoppaAudioBalance/loudness-cache.json";

        [Serializable]
        private sealed class Entry
        {
            public string Guid;
            public int Mode;
            public long Length;
            public long Ticks;
            public CachedLoudness Value;
        }

        [Serializable]
        private sealed class Store
        {
            public List<Entry> Entries = new List<Entry>();
        }

        private readonly string _path;
        private readonly Dictionary<(string Guid, int Mode), Entry> _entries =
            new Dictionary<(string Guid, int Mode), Entry>();

        private LoudnessCache(string path)
        {
            _path = path;
        }

        /// <summary>
        /// Loads the cache from <paramref name="path"/>, or <see cref="DefaultPath"/> when
        /// <paramref name="path"/> is null or empty. A missing file returns an empty cache. A
        /// corrupt or unreadable file also degrades to an empty cache (logged as a warning)
        /// rather than throwing -- the cache is regenerable, so losing it is not fatal, but a
        /// silently-broken cache that keeps re-analyzing the whole project on every window open
        /// is worth flagging.
        /// </summary>
        public static LoudnessCache Load(string path = null)
        {
            var cache = new LoudnessCache(string.IsNullOrEmpty(path) ? DefaultPath : path);

            try
            {
                if (!File.Exists(cache._path))
                {
                    return cache;
                }

                var store = JsonUtility.FromJson<Store>(File.ReadAllText(cache._path));
                if (store?.Entries == null)
                {
                    return cache;
                }

                foreach (var entry in store.Entries)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.Guid))
                    {
                        cache._entries[(entry.Guid, entry.Mode)] = entry;
                    }
                }
            }
            catch (Exception exception)
            {
                // A corrupt or unreadable cache is not worth failing over -- it is
                // regenerable by definition. Fall back to a full re-analysis, but say so:
                // otherwise a permanently-broken cache silently re-analyzes the whole
                // project on every open with no clue why.
                Debug.LogWarning($"[AudioBalance] Could not read the loudness cache, starting fresh: {exception.Message}");
                cache._entries.Clear();
            }

            return cache;
        }

        /// <summary>
        /// Derives the key for <paramref name="clip"/> under <paramref name="mode"/>, reading
        /// real timestamps from disk. This is the single place that computes <c>Ticks</c> as the
        /// combined max of the source asset's and its <c>.meta</c>'s last-write time -- see the
        /// class doc for why. A clip with no asset path (e.g. a procedural clip built in a test)
        /// returns an invalid key (<see cref="LoudnessCacheKey.IsValid"/> false); so does a null
        /// clip.
        /// </summary>
        public static LoudnessCacheKey KeyFor(AudioClip clip, MeasureMode mode)
        {
            if (clip == null)
            {
                return default;
            }

            var assetPath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(assetPath))
            {
                return default;
            }

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                return default;
            }

            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            var absoluteAssetPath = Path.Combine(projectRoot, assetPath);
            var absoluteMetaPath = absoluteAssetPath + ".meta";

            return KeyForPaths(guid, absoluteAssetPath, absoluteMetaPath, mode);
        }

        /// <summary>
        /// Pure, file-system-only key derivation, factored out of <see cref="KeyFor"/> so the
        /// <c>.meta</c>-aware timestamp logic can be exercised directly against real temp files
        /// in tests, without needing an AssetDatabase-imported clip. <see cref="KeyFor"/> is a
        /// thin AssetDatabase-to-path adapter over this method -- there is exactly one place
        /// that computes <see cref="LoudnessCacheKey.Ticks"/>, here.
        /// A missing <paramref name="assetPath"/> returns an invalid key. A missing
        /// <paramref name="metaPath"/> (unusual, but not impossible mid-import) falls back to the
        /// asset's own timestamp rather than throwing.
        /// </summary>
        public static LoudnessCacheKey KeyForPaths(string guid, string assetPath, string metaPath, MeasureMode mode)
        {
            if (string.IsNullOrEmpty(guid) || string.IsNullOrEmpty(assetPath))
            {
                return default;
            }

            FileInfo assetInfo;
            try
            {
                assetInfo = new FileInfo(assetPath);
            }
            catch (Exception)
            {
                return default;
            }

            if (!assetInfo.Exists)
            {
                return default;
            }

            var ticks = assetInfo.LastWriteTimeUtc.Ticks;

            try
            {
                var metaInfo = new FileInfo(metaPath ?? string.Empty);
                if (metaInfo.Exists)
                {
                    ticks = Math.Max(ticks, metaInfo.LastWriteTimeUtc.Ticks);
                }
            }
            catch (Exception)
            {
                // No .meta readable -- fall back to the asset's own timestamp rather than
                // failing the whole lookup over a diagnostic-only concern.
            }

            return new LoudnessCacheKey(guid, assetInfo.Length, ticks, mode);
        }

        /// <summary>
        /// Looks up a cached measurement by <paramref name="key"/>. Returns false (and a null
        /// <paramref name="value"/>) on any mismatch: an invalid key, an unknown
        /// (guid, mode) pair, or a changed <see cref="LoudnessCacheKey.Length"/> /
        /// <see cref="LoudnessCacheKey.Ticks"/>. The returned value is a copy -- mutating it
        /// cannot leak back into the cache's stored entry.
        /// </summary>
        public bool TryGet(LoudnessCacheKey key, out CachedLoudness value)
        {
            value = null;

            if (!key.IsValid)
            {
                return false;
            }

            if (!_entries.TryGetValue((key.Guid, (int)key.Mode), out var entry))
            {
                return false;
            }

            if (entry.Length != key.Length || entry.Ticks != key.Ticks)
            {
                return false;
            }

            if (entry.Value == null)
            {
                return false;
            }

            value = Copy(entry.Value);
            return true;
        }

        /// <summary>
        /// Stores (or overwrites) the measurement for <paramref name="key"/>. An invalid
        /// <paramref name="key"/> or a null <paramref name="value"/> is ignored rather than
        /// stored: <c>JsonUtility</c> would round-trip a stored null as a zero-filled, non-null
        /// <see cref="CachedLoudness"/> after a save/load cycle -- i.e. a fake "measured
        /// successfully at 0 LUFS" -- so accepting null here would let a failed measurement
        /// resurrect as a false positive on reload. The value is copied on the way in, so a
        /// caller mutating its own instance afterwards cannot leak into the cache.
        /// </summary>
        public void Put(LoudnessCacheKey key, CachedLoudness value)
        {
            if (!key.IsValid || value == null)
            {
                return;
            }

            _entries[(key.Guid, (int)key.Mode)] = new Entry
            {
                Guid = key.Guid,
                Mode = (int)key.Mode,
                Length = key.Length,
                Ticks = key.Ticks,
                Value = Copy(value)
            };
        }

        /// <summary>
        /// Drops every entry, both in memory and on disk (deletes the cache file, and any
        /// leftover <c>.tmp</c> file from an interrupted <see cref="Save"/>, if present).
        /// Deleting the file too matters: without it, a "Clear cache" action followed by a
        /// domain reload (without an intervening <see cref="Save"/>) would silently reload the
        /// old file and resurrect everything the user was trying to discard.
        /// </summary>
        public void Clear()
        {
            _entries.Clear();

            try
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[AudioBalance] Could not delete the loudness cache file: {exception.Message}");
            }

            TryDeleteOrphanTemp();
        }

        /// <summary>
        /// Writes every current entry to disk, creating the parent directory on first run if it
        /// does not exist yet (true for the default <c>Library/</c> path on a fresh clone). The
        /// write is crash-safe: content lands in a sibling <c>.tmp</c> file first, and the swap
        /// into place uses <see cref="File.Replace(string,string,string)"/> (atomic on NTFS) when
        /// a previous file exists, or a plain <see cref="File.Move(string,string)"/> on first
        /// write. Unlike a naive delete-then-move, there is never a window where neither file
        /// exists. If the swap itself fails (e.g. a transient lock from AV / the search indexer),
        /// the exception is caught, logged, and the orphan <c>.tmp</c> is cleaned up rather than
        /// left behind.
        /// </summary>
        public void Save()
        {
            var tempPath = _path + ".tmp";

            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var store = new Store();
                store.Entries.AddRange(_entries.Values);

                File.WriteAllText(tempPath, JsonUtility.ToJson(store));

                if (File.Exists(_path))
                {
                    File.Replace(tempPath, _path, null);
                }
                else
                {
                    File.Move(tempPath, _path);
                }
            }
            catch (Exception exception)
            {
                TryDeleteOrphanTemp();
                Debug.LogWarning($"[AudioBalance] Could not write the loudness cache: {exception.Message}");
            }
        }

        private void TryDeleteOrphanTemp()
        {
            try
            {
                var tempPath = _path + ".tmp";
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception)
            {
                // Best-effort cleanup only -- a leftover .tmp is harmless clutter, not a
                // correctness bug, and is overwritten by the next successful Save() anyway.
            }
        }

        private static CachedLoudness Copy(CachedLoudness source)
        {
            return new CachedLoudness
            {
                Status = source.Status,
                Lufs = source.Lufs,
                PeakDb = source.PeakDb
            };
        }
    }
}
