using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Persists measurements so reopening the window is instant and only changed clips are
    /// re-analyzed. Lives under Library/ because it is regenerable and must not be committed.
    ///
    /// <para>
    /// <b>Key contract (important for callers):</b> an entry is keyed on
    /// <c>(guid, fileLength, ticks)</c>. <c>ticks</c> is NOT just the source asset's
    /// <c>lastWriteTicks</c> -- what is actually measured is the decoded <c>AudioClip</c>, which
    /// is a product of the asset's <c>.meta</c> importer settings (Force To Mono, Quality, Sample
    /// Rate Override, ...). Changing only the importer settings (e.g. Force To Mono) leaves the
    /// source file byte-identical -- same length, same mtime -- while producing a materially
    /// different decoded clip. Callers MUST pass
    /// <c>ticks = Math.Max(assetLastWriteTicks, metaLastWriteTicks)</c> so a meta-only edit still
    /// invalidates the cached entry. This class does not read files itself and cannot enforce
    /// this -- it is a contract on the caller (Task 10's analyzer).
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
        private readonly Dictionary<string, Entry> _byGuid = new Dictionary<string, Entry>();

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
                        cache._byGuid[entry.Guid] = entry;
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
                cache._byGuid.Clear();
            }

            return cache;
        }

        /// <summary>
        /// Looks up a cached measurement by file identity. Returns false (and a null
        /// <paramref name="value"/>) on any mismatch: unknown guid, changed
        /// <paramref name="length"/>, or changed <paramref name="ticks"/>. See the class doc for
        /// the required <paramref name="ticks"/> contract -- it must be the combined max of the
        /// source asset's and its <c>.meta</c>'s last-write ticks, not the asset's alone.
        /// </summary>
        public bool TryGet(string guid, long length, long ticks, out CachedLoudness value)
        {
            value = null;

            if (string.IsNullOrEmpty(guid) || !_byGuid.TryGetValue(guid, out var entry))
            {
                return false;
            }

            if (entry.Length != length || entry.Ticks != ticks)
            {
                return false;
            }

            value = entry.Value;
            return value != null;
        }

        /// <summary>
        /// Stores (or overwrites) the measurement for <paramref name="guid"/> under the given
        /// file identity. See the class doc for the required <paramref name="ticks"/> contract.
        /// A null <paramref name="value"/> is ignored rather than stored: <c>JsonUtility</c>
        /// would round-trip it as a zero-filled, non-null <see cref="CachedLoudness"/> after a
        /// save/load cycle -- i.e. a fake "measured successfully at 0 LUFS" -- so accepting null
        /// here would let a failed measurement resurrect as a false positive on reload.
        /// </summary>
        public void Put(string guid, long length, long ticks, CachedLoudness value)
        {
            if (string.IsNullOrEmpty(guid) || value == null)
            {
                return;
            }

            _byGuid[guid] = new Entry
            {
                Guid = guid,
                Length = length,
                Ticks = ticks,
                Value = value
            };
        }

        /// <summary>
        /// Drops every entry, both in memory and on disk (deletes the cache file if present).
        /// Deleting the file too matters: without it, a "Clear cache" action followed by a
        /// domain reload (without an intervening <see cref="Save"/>) would silently reload the
        /// old file and resurrect everything the user was trying to discard.
        /// </summary>
        public void Clear()
        {
            _byGuid.Clear();

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
        }

        /// <summary>
        /// Writes every current entry to disk, creating the parent directory on first run if it
        /// does not exist yet (true for the default <c>Library/</c> path on a fresh clone). The
        /// write is crash-safe: it lands in a sibling <c>.tmp</c> file first and is only swapped
        /// into place once fully flushed, so a crash mid-write cannot truncate the committed
        /// cache file.
        /// </summary>
        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var store = new Store();
                store.Entries.AddRange(_byGuid.Values);

                var tempPath = _path + ".tmp";
                File.WriteAllText(tempPath, JsonUtility.ToJson(store));

                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }

                File.Move(tempPath, _path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[AudioBalance] Could not write the loudness cache: {exception.Message}");
            }
        }
    }
}
