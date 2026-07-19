using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hoppa.AudioBalance
{
    /// <summary>
    /// Baked output of the Audio Balance window: a per-clip gain in decibels, relative to
    /// the anchor clip. Every gain is at or below 0 dB (see the headroom pass in the editor
    /// solver), so applying one can never push a source past AudioSource.volume's 1.0 cap.
    /// </summary>
    [CreateAssetMenu(menuName = "Hoppa/Audio/Audio Gain Table", fileName = "AudioGainTable")]
    public sealed class AudioGainTable : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public AudioClip Clip;
            public float GainDb;
        }

        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        private Dictionary<AudioClip, float> _lookup;

        public IReadOnlyList<Entry> Entries => _entries;

        public void SetEntries(IEnumerable<Entry> entries)
        {
            _entries = entries == null ? Array.Empty<Entry>() : new List<Entry>(entries).ToArray();
            _lookup = null;
        }

        /// <summary>Gain in dB for the clip, or 0 dB (unity) when the clip is not in the table.</summary>
        public float GetGainDb(AudioClip clip)
        {
            if (clip == null)
            {
                return 0f;
            }

            EnsureLookup();
            return _lookup.TryGetValue(clip, out var db) ? db : 0f;
        }

        /// <summary>Linear multiplier for the clip, or 1.0 when the clip is not in the table.</summary>
        public float GetGain(AudioClip clip)
        {
            return AudioGainMath.LinearFromDb(GetGainDb(clip));
        }

        private void EnsureLookup()
        {
            if (_lookup != null)
            {
                return;
            }

            _lookup = new Dictionary<AudioClip, float>(_entries.Length);
            foreach (var entry in _entries)
            {
                if (entry.Clip != null)
                {
                    _lookup[entry.Clip] = entry.GainDb;
                }
            }
        }

        private void OnEnable()
        {
            _lookup = null;
        }

        private void OnValidate()
        {
            _lookup = null;
        }
    }
}
