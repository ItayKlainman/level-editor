using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Level Editor/String-Int Mapping", order = 10)]
    public sealed class StringIntMapping : ScriptableObject
    {
        [SerializeField] private List<StringIntEntry> _entries = new List<StringIntEntry>();

        public IReadOnlyList<StringIntEntry> Entries => _entries;

        public bool TryGet(string key, out int value)
        {
            foreach (var entry in _entries)
            {
                if (entry.Key == key)
                {
                    value = entry.Value;
                    return true;
                }
            }
            value = 0;
            return false;
        }

        public int Get(string key, int fallback = 0)
            => TryGet(key, out int v) ? v : fallback;

        public void Add(string key, int value)
            => _entries.Add(new StringIntEntry { Key = key, Value = value });
    }

    [Serializable]
    public sealed class StringIntEntry
    {
        public string Key;
        public int    Value;
    }
}
