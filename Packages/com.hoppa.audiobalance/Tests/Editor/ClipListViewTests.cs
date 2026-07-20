using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class ClipListViewTests
    {
        private static AudioBalanceRow Row(string name, float lufs, float gainDb,
            ClipStatus status = ClipStatus.Ok, bool outlier = false)
        {
            var clip = AudioClip.Create(name, 128, 1, 44100, false);
            return new AudioBalanceRow
            {
                Clip = clip,
                Analysis = status == ClipStatus.Ok
                    ? ClipAnalysis.Ok(clip, lufs, -3f)
                    : new ClipAnalysis(clip, status, 0f, -80f, "reason"),
                Gain = new GainResult(clip, status, gainDb, gainDb, outlier)
            };
        }

        private static AudioBalanceProfile Profile(params AudioBalanceRow[] rows)
        {
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.ResetToDefaultCategories();
            foreach (var row in rows)
            {
                profile.SettingsFor(row.Clip);
            }

            return profile;
        }

        /// <summary>
        /// The read-only category lookup BuildVisible now takes. Resolving settings up front
        /// is exactly what the window does -- BuildVisible must not be able to touch the
        /// profile at all.
        /// </summary>
        private static Func<AudioBalanceRow, string> Lookup(AudioBalanceProfile profile)
        {
            return row => profile == null || row?.Clip == null
                ? string.Empty
                : profile.SettingsFor(row.Clip)?.Category ?? string.Empty;
        }

        [Test]
        public void BuildVisible_WithNoFilter_ReturnsEveryRow()
        {
            var rows = new List<AudioBalanceRow> { Row("kick", -20f, -3f), Row("snare", -18f, -1f) };

            var visible = ClipListView.BuildVisible(rows, string.Empty, ClipSortMode.Name, true,
                Lookup(Profile(rows.ToArray())));

            Assert.AreEqual(2, visible.Count);
        }

        [Test]
        public void BuildVisible_FiltersByNameCaseInsensitively()
        {
            var rows = new List<AudioBalanceRow> { Row("Kick_Heavy", -20f, -3f), Row("snare", -18f, -1f) };

            var visible = ClipListView.BuildVisible(rows, "kick", ClipSortMode.Name, true,
                Lookup(Profile(rows.ToArray())));

            Assert.AreEqual(1, visible.Count);
            Assert.AreEqual("Kick_Heavy", visible[0].Clip.name);
        }

        [Test]
        public void BuildVisible_SortsByNameAscendingAndDescending()
        {
            var rows = new List<AudioBalanceRow> { Row("zebra", -20f, -3f), Row("apple", -18f, -1f) };
            var profile = Profile(rows.ToArray());

            var ascending = ClipListView.BuildVisible(rows, string.Empty, ClipSortMode.Name, true, Lookup(profile));
            var descending = ClipListView.BuildVisible(rows, string.Empty, ClipSortMode.Name, false, Lookup(profile));

            Assert.AreEqual("apple", ascending[0].Clip.name);
            Assert.AreEqual("zebra", descending[0].Clip.name);
        }

        [Test]
        public void BuildVisible_SortsByLoudness()
        {
            var rows = new List<AudioBalanceRow> { Row("loud", -12f, -6f), Row("quiet", -30f, 0f) };

            var visible = ClipListView.BuildVisible(rows, string.Empty, ClipSortMode.Loudness, true,
                Lookup(Profile(rows.ToArray())));

            Assert.AreEqual("quiet", visible[0].Clip.name, "Ascending loudness puts the quietest first.");
        }

        [Test]
        public void BuildVisible_SortsByGain()
        {
            var rows = new List<AudioBalanceRow> { Row("a", -20f, -1f), Row("b", -20f, -12f) };

            var visible = ClipListView.BuildVisible(rows, string.Empty, ClipSortMode.Gain, true,
                Lookup(Profile(rows.ToArray())));

            Assert.AreEqual("b", visible[0].Clip.name);
        }

        [Test]
        public void BuildVisible_SortsByCategory()
        {
            // Names deliberately in the OPPOSITE order to the categories. With the plan's
            // original "bed"/"blip" fixture this test passed against a build that ignored the
            // sort mode entirely and always sorted by name -- verified by mutation. Equal LUFS
            // and equal gain likewise make it fail if either of those keys is used instead.
            var music = Row("zzz_bed", -20f, -3f);
            var ui = Row("aaa_blip", -20f, -3f);
            var profile = Profile(music, ui);
            profile.SettingsFor(music.Clip).Category = "Music";
            profile.SettingsFor(ui.Clip).Category = "UI";

            var visible = ClipListView.BuildVisible(new List<AudioBalanceRow> { ui, music },
                string.Empty, ClipSortMode.Category, true, Lookup(profile));

            Assert.AreEqual("Music", profile.SettingsFor(visible[0].Clip).Category);
        }

        [Test]
        public void BuildVisible_SortIsStableForEqualKeys()
        {
            var rows = new List<AudioBalanceRow> { Row("first", -20f, -3f), Row("second", -20f, -3f) };

            var visible = ClipListView.BuildVisible(rows, string.Empty, ClipSortMode.Loudness, true,
                Lookup(Profile(rows.ToArray())));

            Assert.AreEqual("first", visible[0].Clip.name);
            Assert.AreEqual("second", visible[1].Clip.name);
        }

        [Test]
        public void BuildVisible_SortIsStableForEqualKeysWhenDescendingToo()
        {
            // The regression this pins: an earlier revision sorted ascending and then called
            // List.Reverse() to get descending. Reverse inverts tie GROUPS as well as the
            // overall order, so two rows with equal keys came back in reverse discovery
            // order -- stable ascending, unstable descending. Only the ascending case was
            // ever tested, so it survived review.
            var rows = new List<AudioBalanceRow> { Row("first", -20f, -3f), Row("second", -20f, -3f) };

            var visible = ClipListView.BuildVisible(rows, string.Empty, ClipSortMode.Loudness, false,
                Lookup(Profile(rows.ToArray())));

            Assert.AreEqual("first", visible[0].Clip.name,
                "Equal keys must keep discovery order in BOTH directions.");
            Assert.AreEqual("second", visible[1].Clip.name);
        }

        [Test]
        public void BuildVisible_DoesNotMutateTheProfile()
        {
            // BuildVisible is documented as pure. It used to call profile.SettingsFor(),
            // which APPENDS a ClipSettings on a miss -- writing to the asset from inside
            // OnGUI. Passing a lookup instead makes that structurally impossible; this test
            // fails loudly if anyone reintroduces a profile parameter.
            var known = Row("known", -20f, -3f);
            var stranger = Row("stranger", -20f, -3f);
            var profile = Profile(known);
            var countBefore = profile.Clips.Count;

            ClipListView.BuildVisible(
                new List<AudioBalanceRow> { known, stranger },
                string.Empty, ClipSortMode.Category, true,
                row => "Music");

            Assert.AreEqual(countBefore, profile.Clips.Count,
                "Building the visible list must not add settings for unknown clips.");
        }

        [Test]
        public void BuildVisible_HandlesNullRowsWithoutThrowing()
        {
            Assert.AreEqual(0, ClipListView.BuildVisible(null, string.Empty, ClipSortMode.Name, true, null).Count);
        }

        [Test]
        public void StatusIcon_MarksOutliersAndBrokenClipsButNotHealthyOnes()
        {
            Assert.AreEqual(string.Empty, ClipListView.StatusIcon(Row("fine", -20f, -3f)));
            Assert.AreNotEqual(string.Empty, ClipListView.StatusIcon(Row("odd", -20f, -20f, ClipStatus.Ok, true)));
            Assert.AreNotEqual(string.Empty, ClipListView.StatusIcon(Row("mute", 0f, 0f, ClipStatus.Silent)));
            Assert.AreNotEqual(string.Empty,
                ClipListView.StatusIcon(Row("broken", 0f, 0f, ClipStatus.Unanalyzable)));
        }

        [Test]
        public void BulkAssignCategory_SetsTheCategoryOnEverySuppliedRow()
        {
            var a = Row("a", -20f, -3f);
            var b = Row("b", -20f, -3f);
            var profile = Profile(a, b);

            ClipListView.BulkAssignCategory(new[] { a, b }, profile, "UI");

            Assert.AreEqual("UI", profile.SettingsFor(a.Clip).Category);
            Assert.AreEqual("UI", profile.SettingsFor(b.Clip).Category);
        }

        [Test]
        public void BulkAssignCategory_IgnoresNullInputsWithoutThrowing()
        {
            var profile = Profile();

            Assert.DoesNotThrow(() => ClipListView.BulkAssignCategory(null, profile, "UI"));
            Assert.DoesNotThrow(() => ClipListView.BulkAssignCategory(new AudioBalanceRow[0], null, "UI"));
        }

        // ---------------------------------------------------------------------------------
        // BuildSettingsLookup -- the read-only map the window resolves ONCE per row-set change
        // and hands to BuildVisible/the row drawer. It is the render path's only route to a
        // ClipSettings, so it must never enrol.
        // ---------------------------------------------------------------------------------

        [Test]
        public void BuildSettingsLookup_MapsEveryEnrolledRowToItsSettings()
        {
            var a = Row("a", -20f, -3f);
            var b = Row("b", -20f, -3f);
            var profile = Profile(a, b);

            var lookup = ClipListView.BuildSettingsLookup(profile, new List<AudioBalanceRow> { a, b });

            Assert.AreSame(profile.FindSettings(a.Clip), lookup[a.Clip],
                "The map must hand back the LIVE settings object, not a copy -- the row drawer " +
                "writes TrimDb through it.");
            Assert.AreSame(profile.FindSettings(b.Clip), lookup[b.Clip]);
        }

        [Test]
        public void BuildSettingsLookup_DoesNotEnrolUnknownClips()
        {
            // Rows always originate from profile.Clips, so a miss should be impossible -- but
            // this map is built from OnGUI, and SettingsFor appends on a miss. Resolving via
            // FindSettings makes "enrolled during a repaint, outside any Undo scope" structurally
            // unreachable rather than merely unlikely.
            var known = Row("known", -20f, -3f);
            var stranger = Row("stranger", -20f, -3f);
            var profile = Profile(known);
            var countBefore = profile.Clips.Count;

            var lookup = ClipListView.BuildSettingsLookup(profile,
                new List<AudioBalanceRow> { known, stranger });

            Assert.AreEqual(countBefore, profile.Clips.Count,
                "Resolving settings for rendering must never write to the profile asset.");
            Assert.IsFalse(lookup.ContainsKey(stranger.Clip),
                "An un-enrolled clip has no settings; it must be absent, not invented.");
        }
    }
}
