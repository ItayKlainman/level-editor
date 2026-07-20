using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Persists measurements so reopening the window is instant and only changed clips are
    /// re-analyzed. Lives under Library/ because it is regenerable and must not be committed.
    /// </summary>
    public sealed class LoudnessCache
    {
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
            catch (Exception)
            {
                // A corrupt or unreadable cache is not worth failing over -- it is
                // regenerable by definition. Fall back to a full re-analysis.
                cache._byGuid.Clear();
            }

            return cache;
        }

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

        public void Put(string guid, long length, long ticks, CachedLoudness value)
        {
            if (string.IsNullOrEmpty(guid))
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

        public void Clear()
        {
            _byGuid.Clear();
        }

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

                File.WriteAllText(_path, JsonUtility.ToJson(store));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[AudioBalance] Could not write the loudness cache: {exception.Message}");
            }
        }
    }
}
