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
        [Tooltip("Folders of audio to include, relative to the project root. Prefer the " +
                 "Add Folder button in the Audio Balance window over typing paths here.")]
        public List<string> Folders = new List<string>();

        /// <summary>The reference clip -- usually the background music that runs during levels.</summary>
        [Tooltip("Your reference track, usually the background music that plays during " +
                 "levels. Changing it will NOT move any clip's gain: it only decides which " +
                 "clips get flagged as outliers. Category offsets set relative placement.")]
        public AudioClip Anchor;

        [Tooltip("The groups clips are balanced into, such as Music, SFX and UI. Each group " +
                 "carries how loud it should sit relative to the others, and how it is measured.")]
        public List<AudioCategory> Categories = new List<AudioCategory>();

        [Tooltip("Every clip this profile has seen, with its group and its manual trim. The " +
                 "window fills this in when you press Analyze; you rarely need to edit it here.")]
        public List<ClipSettings> Clips = new List<ClipSettings>();

        /// <summary>Destination asset for the baked gains.</summary>
        [Tooltip("The Audio Gain Table asset that Write Table bakes into. That table is the " +
                 "only piece of this system your game reads at runtime.")]
        public AudioGainTable Table;

        public void ResetToDefaultCategories()
        {
            Categories = new List<AudioCategory>
            {
                new AudioCategory("Music", 0f, MeasureMode.Integrated),
                new AudioCategory("SFX", 3f, MeasureMode.MomentaryMax),
                new AudioCategory("UI", -6f, MeasureMode.MomentaryMax)
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
        /// warning and an undo entry that looks like a harmless rename. Pairing them here, plus
        /// <see cref="AudioCategory.Name"/> having no public setter, is what keeps the broken
        /// half from being independently reachable.
        /// </para>
        ///
        /// <para>
        /// <b>The target is passed by reference, not by name.</b> An earlier revision resolved
        /// it by first exact name match, which silently renamed the WRONG row whenever two
        /// categories shared a name -- three clicks away, because "Add Category" creates every
        /// new row as literally "New". The caller already holds the object it drew, so there is
        /// no reason to look it up at all.
        /// </para>
        ///
        /// <para>
        /// Returns false and changes nothing when the rename is rejected: an empty name (a
        /// half-typed field must not orphan anything), a no-op rename, a target not in this
        /// profile, or a name already taken by a DIFFERENT category. That last guard matters --
        /// renaming "SFX" to "Music" would otherwise leave two categories called "Music",
        /// re-point every SFX clip to a name that now resolves to the FIRST one, and hand them
        /// its offset and MeasureMode: the exact silent regrouping this method exists to
        /// prevent, arriving through a different door. Merging may be a reasonable feature one
        /// day, but it must be explicit, not a side effect of typing.
        /// </para>
        /// </summary>
        public bool RenameCategory(AudioCategory target, string newName)
        {
            if (target == null || string.IsNullOrEmpty(newName) || Categories == null)
            {
                return false;
            }

            var oldName = target.Name;
            if (oldName == newName)
            {
                return false;
            }

            var found = false;
            foreach (var category in Categories)
            {
                if (ReferenceEquals(category, target))
                {
                    found = true;
                }
                else if (category != null && category.Name == newName)
                {
                    // Name collision with a different category -- see the doc above.
                    return false;
                }
            }

            if (!found)
            {
                return false;
            }

            target.SetNameUnchecked(newName);

            foreach (var settings in Clips)
            {
                if (settings != null && settings.Category == oldName)
                {
                    settings.Category = newName;
                }
            }

            return true;
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
        ///
        /// <para>
        /// <c>internal</c> on purpose: this is the package's only remaining mutating lookup,
        /// and leaving it public meant any consumer -- or any future render path -- could write
        /// to the asset just by reading from it. The sole production caller is the window's
        /// <c>RunAnalysis</c>, which owns the Undo scope. Tests reach it via
        /// <c>InternalsVisibleTo</c> (see <c>AssemblyInfo.cs</c>) rather than by widening the
        /// API for their convenience.
        /// </para>
        /// </summary>
        internal ClipSettings SettingsFor(AudioClip clip)
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

        // The three accessors below are READS and are now implemented as such. They previously
        // routed through SettingsFor, so merely asking a clip's offset enrolled it -- which is
        // how the render and solve paths came to write to the asset. Fixing them here removes
        // the hazard at source rather than hiding it behind an access modifier: an un-enrolled
        // clip simply answers with the neutral default instead of being created on the spot.

        public float OffsetDbFor(AudioClip clip)
        {
            var settings = FindSettings(clip);
            if (settings == null)
            {
                return 0f;
            }

            var category = FindCategory(settings.Category);
            return category?.OffsetDb ?? 0f;
        }

        public float TrimDbFor(AudioClip clip)
        {
            return FindSettings(clip)?.TrimDb ?? 0f;
        }

        public MeasureMode ModeFor(AudioClip clip)
        {
            var settings = FindSettings(clip);
            if (settings == null)
            {
                return MeasureMode.Integrated;
            }

            var category = FindCategory(settings.Category);
            return category?.Mode ?? MeasureMode.Integrated;
        }
    }
}
