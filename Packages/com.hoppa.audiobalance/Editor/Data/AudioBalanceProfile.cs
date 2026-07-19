using System.Collections.Generic;
using Hoppa.AudioBalance;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// Authoring state for the Audio Balance window. Editor-only by design: the baked
    /// AudioGainTable carries final gains, so the runtime never needs categories.
    /// </summary>
    [CreateAssetMenu(menuName = "Hoppa/Audio/Audio Balance Profile", fileName = "AudioBalanceProfile")]
    public sealed class AudioBalanceProfile : ScriptableObject
    {
        /// <summary>Project-relative folders to scan, e.g. "Assets/BusBuddies/Audio".</summary>
        public List<string> Folders = new List<string>();

        /// <summary>The reference clip -- usually the background music that runs during levels.</summary>
        public AudioClip Anchor;

        public List<AudioCategory> Categories = new List<AudioCategory>();

        public List<ClipSettings> Clips = new List<ClipSettings>();

        /// <summary>Destination asset for the baked gains.</summary>
        public AudioGainTable Table;

        public void ResetToDefaultCategories()
        {
            Categories = new List<AudioCategory>
            {
                new AudioCategory { Name = "Music", OffsetDb = 0f, Mode = MeasureMode.Integrated },
                new AudioCategory { Name = "SFX", OffsetDb = 3f, Mode = MeasureMode.MomentaryMax },
                new AudioCategory { Name = "UI", OffsetDb = -6f, Mode = MeasureMode.MomentaryMax }
            };
        }

        /// <summary>
        /// The named category, or the first one as a fallback so a renamed category never
        /// silently drops a clip's offset to zero. Null only when no categories exist at all.
        /// </summary>
        public AudioCategory FindCategory(string name)
        {
            if (Categories == null || Categories.Count == 0)
            {
                return null;
            }

            foreach (var category in Categories)
            {
                if (category != null && category.Name == name)
                {
                    return category;
                }
            }

            return Categories[0];
        }

        /// <summary>Returns the clip's settings, creating and storing them on first access.</summary>
        public ClipSettings SettingsFor(AudioClip clip)
        {
            if (clip == null)
            {
                return null;
            }

            foreach (var settings in Clips)
            {
                if (settings != null && settings.Clip == clip)
                {
                    return settings;
                }
            }

            var created = new ClipSettings
            {
                Clip = clip,
                Category = Categories != null && Categories.Count > 0 ? Categories[0].Name : "SFX"
            };

            Clips.Add(created);
            return created;
        }

        public float OffsetDbFor(AudioClip clip)
        {
            var settings = SettingsFor(clip);
            if (settings == null)
            {
                return 0f;
            }

            var category = FindCategory(settings.Category);
            return category?.OffsetDb ?? 0f;
        }

        public float TrimDbFor(AudioClip clip)
        {
            return SettingsFor(clip)?.TrimDb ?? 0f;
        }

        public MeasureMode ModeFor(AudioClip clip)
        {
            var settings = SettingsFor(clip);
            if (settings == null)
            {
                return MeasureMode.Integrated;
            }

            var category = FindCategory(settings.Category);
            return category?.Mode ?? MeasureMode.Integrated;
        }
    }
}
