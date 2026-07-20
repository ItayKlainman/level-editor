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
        ///
        /// <para>
        /// <b>FindSettings, not SettingsFor.</b> This helper backs eight BuildVisible tests, and
        /// building it out of the MUTATING accessor meant all eight exercised the tool against
        /// a lookup that can append to the asset -- precisely the hazard the read-only lookup
        /// parameter was introduced to make structurally impossible. Production's
        /// <c>CategoryOf</c> reads a pre-resolved map and cannot write; the fixture must match
        /// it, or the tests are not testing the shipped shape.
        /// </para>
        /// </summary>
        private static Func<AudioBalanceRow, string> Lookup(AudioBalanceProfile profile)
        {
            return row => profile == null || row?.Clip == null
                ? string.Empty
                : profile.FindSettings(row.Clip)?.Category ?? string.Empty;
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

        // DELETED: BuildVisible_DoesNotMutateTheProfile. It could not fail. BuildVisible has no
        // profile parameter, so it cannot reach profile.Clips BY THE TYPE SYSTEM -- and the
        // regression it claimed to guard ("anyone reintroduces a profile parameter") would be a
        // COMPILE ERROR in this test file, not a red test. BuildSettingsLookup_DoesNotEnrol
        // UnknownClips below is the real version of this assertion and covers the live path.

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

        // ---------------------------------------------------------------------------------
        // Gaps the Task 12 review asked for.
        // ---------------------------------------------------------------------------------

        [Test]
        public void BulkAssignCategory_LeavesRowsItWasNotGivenAlone()
        {
            var selected = Row("selected", -20f, -3f);
            var untouched = Row("untouched", -20f, -3f);
            var profile = Profile(selected, untouched);
            profile.SettingsFor(untouched.Clip).Category = "Music";

            ClipListView.BulkAssignCategory(new[] { selected }, profile, "UI");

            Assert.AreEqual("UI", profile.FindSettings(selected.Clip).Category);
            Assert.AreEqual("Music", profile.FindSettings(untouched.Clip).Category,
                "A bulk assign must touch only the rows it was handed.");
        }

        [Test]
        public void BulkAssignCategory_WithAnEmptyCategory_ChangesNothing()
        {
            var row = Row("row", -20f, -3f);
            var profile = Profile(row);
            profile.SettingsFor(row.Clip).Category = "Music";

            ClipListView.BulkAssignCategory(new[] { row }, profile, string.Empty);

            Assert.AreEqual("Music", profile.FindSettings(row.Clip).Category,
                "An empty category is not a category -- assigning it would orphan the clip.");
        }

        [Test]
        public void StatusIcon_OnANullRow_IsEmptyRatherThanThrowing()
        {
            Assert.AreEqual(string.Empty, ClipListView.StatusIcon(null));
        }

        [Test]
        public void BuildVisible_SortsByCategoryDescending()
        {
            // Names deliberately contradict category order, so a build that ignores the sort
            // mode and falls back to Name cannot pass this.
            var aaa = Row("aaa_ui", -20f, -3f);
            var zzz = Row("zzz_music", -20f, -3f);
            var profile = Profile(aaa, zzz);
            profile.SettingsFor(aaa.Clip).Category = "UI";
            profile.SettingsFor(zzz.Clip).Category = "Music";

            var visible = ClipListView.BuildVisible(
                new List<AudioBalanceRow> { zzz, aaa }, string.Empty,
                ClipSortMode.Category, false, Lookup(profile));

            Assert.AreEqual("aaa_ui", visible[0].Clip.name, "UI sorts after Music, so it leads descending.");
            Assert.AreEqual("zzz_music", visible[1].Clip.name);
        }

        // ---------------------------------------------------------------------------------
        // CategoryPopupOptions -- an orphaned category used to be a DEAD-END control: a clip
        // whose Category matched nothing clamped to index 0, displaying as the first category,
        // and picking that first category to "fix" it was a no-op because the value had not
        // changed. The user could see the wrong category and had no way to correct it.
        // ---------------------------------------------------------------------------------

        [Test]
        public void CategoryPopupOptions_ForAKnownCategory_SelectsItAndAddsNoExtraEntry()
        {
            var options = ClipListView.CategoryPopupOptions(
                new[] { "Music", "SFX", "UI" }, "SFX", out var index);

            Assert.AreEqual(3, options.Length, "A resolvable category needs no placeholder.");
            Assert.AreEqual(1, index);
        }

        [Test]
        public void CategoryPopupOptions_ForAnOrphanedCategory_AppendsAPlaceholderAndSelectsIt()
        {
            var options = ClipListView.CategoryPopupOptions(
                new[] { "Music", "SFX" }, "Deleted", out var index);

            Assert.AreEqual(3, options.Length);
            Assert.AreEqual(2, index,
                "The orphan must select the placeholder, NOT clamp to index 0 -- clamping shows " +
                "a category the clip is not in, and re-picking it is a no-op.");
            StringAssert.Contains("Deleted", options[2],
                "Naming the missing category is what makes the state diagnosable.");
        }

        [Test]
        public void CategoryPopupOptions_WithNoCategoriesAtAll_DoesNotThrow()
        {
            var options = ClipListView.CategoryPopupOptions(new string[0], "Music", out var index);

            Assert.AreEqual(1, options.Length);
            Assert.AreEqual(0, index);
        }

        // ---------------------------------------------------------------------------------
        // BulkCategoryCaption -- selection deliberately survives filtering, so the button can
        // act on rows that are not on screen. The count alone hides that.
        // ---------------------------------------------------------------------------------

        [Test]
        public void BulkCategoryCaption_WhenEverySelectedRowIsVisible_ShowsOnlyTheCount()
        {
            Assert.AreEqual("Set Category (3)", ClipListView.BulkCategoryCaption(3, 3));
        }

        [Test]
        public void BulkCategoryCaption_WhenTheFilterHidesSomeSelection_SaysHowMany()
        {
            Assert.AreEqual("Set Category (12, 10 hidden)", ClipListView.BulkCategoryCaption(12, 2),
                "Acting on 10 rows the user cannot see must be stated, not implied by a number.");
        }
    }
}
