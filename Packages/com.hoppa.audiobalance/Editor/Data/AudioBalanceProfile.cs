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

        /// <summary>
        /// Renames a category AND re-points every clip that referenced it, as one operation.
        ///
        /// <para>
        /// These two halves must never be separated. <see cref="ClipSettings.Category"/> is a
        /// name string, so renaming the category alone orphans every clip in it:
        /// <see cref="FindCategory"/> then misses and falls back to <c>Categories[0]</c>,
        /// silently moving the whole group to another category's offset AND
        /// <see cref="MeasureMode"/>. A designer tidying "SFX" to "SFX (UI)" would move every
        /// one-shot to Music/Integrated and shift its baked gain by several dB, with no
        /// warning and an undo entry that looks like a harmless rename. Exposing this as one
        /// method is what makes that mistake unrepresentable.
        /// </para>
        ///
        /// <para>
        /// No-ops on an empty new name (a half-typed field must not orphan anything) or when
        /// no category has the old name. Matching is exact -- deliberately NOT
        /// <see cref="FindCategory"/>, whose <c>Categories[0]</c> fallback would rename the
        /// wrong category entirely.
        /// </para>
        /// </summary>
        public void RenameCategory(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(newName) || oldName == newName || Categories == null)
            {
                return;
            }

            AudioCategory target = null;
            foreach (var category in Categories)
            {
                if (category != null && category.Name == oldName)
                {
                    target = category;
                    break;
                }
            }

            if (target == null)
            {
                return;
            }

            target.Name = newName;

            foreach (var settings in Clips)
            {
                if (settings != null && settings.Category == oldName)
                {
                    settings.Category = newName;
                }
            }
        }

        /// <summary>
        /// The clip's existing settings, or null if it has never been enrolled. Unlike
        /// <see cref="SettingsFor"/> this NEVER mutates the profile, which makes it the only
        /// safe lookup for read paths -- rendering, measuring, solving. <c>SettingsFor</c>
        /// appends on a miss, so calling it from those paths writes to the asset outside any
        /// Undo scope (and, from <c>OnGUI</c>, once per repaint).
        /// </summary>
        public ClipSettings FindSettings(AudioClip clip)
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

            return null;
        }

        /// <summary>
        /// Returns the clip's settings, creating and storing them on first access -- i.e. this
        /// ENROLS the clip into the profile. Because that is a mutation, call it only from a
        /// path that owns an <c>Undo.RecordObject</c> scope and follows with
        /// <c>EditorUtility.SetDirty</c>; use <see cref="FindSettings"/> everywhere else.
        /// </summary>
        public ClipSettings SettingsFor(AudioClip clip)
        {
            if (clip == null)
            {
                return null;
            }

            var existing = FindSettings(clip);
            if (existing != null)
            {
                return existing;
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
