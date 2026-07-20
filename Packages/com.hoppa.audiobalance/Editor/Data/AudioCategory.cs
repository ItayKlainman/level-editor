using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// A group of clips that share an intended level relative to the anchor. The offset is
    /// what stops everything collapsing to the same loudness: SFX are meant to sit above the
    /// music bed, UI blips below it.
    ///
    /// <para>
    /// <see cref="Name"/> is deliberately read-only from outside this assembly. It is a
    /// <b>foreign key</b>: <see cref="ClipSettings.Category"/> stores it as a string, so
    /// writing it in isolation orphans every clip in the group --
    /// <see cref="AudioBalanceProfile.FindCategory"/> then misses and falls back to
    /// <c>Categories[0]</c>, silently moving the whole group to another category's offset AND
    /// <see cref="MeasureMode"/>. Renaming therefore only exists as
    /// <see cref="AudioBalanceProfile.RenameCategory"/>, which re-points the clips in the same
    /// operation. A public settable field made the broken half independently reachable, and a
    /// comment asking callers not to use it is not enforcement.
    /// </para>
    ///
    /// <para>
    /// <b>Residual gap, stated plainly:</b> <see cref="SetNameUnchecked"/> is <c>internal</c>,
    /// so code inside <c>Hoppa.AudioBalance.Editor</c> can still bypass the re-point. C# has no
    /// friend-of-one-type access, so this is the tightest enforcement available without moving
    /// the type. It closes the package's public surface -- which is what any consumer sees --
    /// and leaves a single in-assembly caller (<c>RenameCategory</c>) to keep honest.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class AudioCategory
    {
        // Was a public field literally called "Name" before it became a property backed by
        // _name. A filesystem sweep found zero AudioBalanceProfile assets anywhere, so nothing
        // in this repo needs migrating -- but a profile sitting in someone's stash or side
        // branch would silently deserialize every category name as "SFX" without this. One
        // line, and the question stops mattering.
        [FormerlySerializedAs("Name")]
        [Tooltip("The group's name. Renaming it moves every clip in the group with it. Two " +
                 "groups cannot share a name.")]
        [SerializeField] private string _name = "SFX";

        [Tooltip("How loud this group should sit relative to the others, in dB. Positive is " +
                 "louder: SFX at +3 sits above the music bed, UI blips at -6 sit below it.")]
        public float OffsetDb;

        [Tooltip("How clips in this group are measured. Use the whole-clip average for music " +
                 "and long loops, and the loudest moment for short one-shots. Changing this " +
                 "re-measures every clip in the group.")]
        public MeasureMode Mode = MeasureMode.MomentaryMax;

        public AudioCategory()
        {
        }

        public AudioCategory(string name, float offsetDb, MeasureMode mode)
        {
            _name = name;
            OffsetDb = offsetDb;
            Mode = mode;
        }

        /// <summary>
        /// The category's name, which doubles as the key <see cref="ClipSettings.Category"/>
        /// refers to. Change it only via <see cref="AudioBalanceProfile.RenameCategory"/>.
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// Sets the name WITHOUT re-pointing the clips that reference it. The only legitimate
        /// caller is <see cref="AudioBalanceProfile.RenameCategory"/>, which does the
        /// re-pointing itself; anything else orphans the group. Named to be uncomfortable to
        /// call by accident.
        /// </summary>
        internal void SetNameUnchecked(string value)
        {
            _name = value;
        }
    }
}
