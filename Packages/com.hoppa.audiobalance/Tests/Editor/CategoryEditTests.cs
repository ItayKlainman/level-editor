using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    /// <summary>
    /// The category-edit SEQUENCING, extracted out of OnGUI so it can be tested at all.
    ///
    /// <para>
    /// The order of operations here is load-bearing and was previously only expressible as a
    /// comment inside the window: the rename must be APPLIED before the re-measure flag is
    /// folded, so that a rejected rename contributes nothing rather than clearing a mode change
    /// from an earlier frame of the same gesture. An IMGUI harness over the window would have
    /// passed vacuously (it cannot easily produce the interleaving); a pure function cannot.
    /// </para>
    /// </summary>
    public class CategoryEditTests
    {
        private static AudioBalanceProfile Profile()
        {
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.ResetToDefaultCategories();
            return profile;
        }

        private static AudioCategory CategoryNamed(AudioBalanceProfile profile, string name)
        {
            return profile.FindCategory(name);
        }

        [Test]
        public void Apply_WithAFreeName_RenamesAndAsksForAReMeasure()
        {
            var profile = Profile();
            var music = CategoryNamed(profile, "Music");

            var result = CategoryEdit.Apply(profile, music, "Ambience", music.OffsetDb, music.Mode);

            Assert.IsFalse(result.RenameRejected);
            Assert.IsTrue(result.NeedsAnalyze,
                "A rename re-points clips, which can change their effective measure mode.");
            Assert.AreEqual("Ambience", music.Name);
        }

        [Test]
        public void Apply_WithAnOffsetChangeOnly_DoesNotAskForAReMeasure()
        {
            var profile = Profile();
            var music = CategoryNamed(profile, "Music");

            var result = CategoryEdit.Apply(profile, music, music.Name, -4.5f, music.Mode);

            Assert.IsFalse(result.NeedsAnalyze,
                "An offset moves the target, not the measurement -- Resolve is enough.");
            Assert.AreEqual(-4.5f, music.OffsetDb, 1e-4f);
        }

        [Test]
        public void Apply_WithAModeChange_AsksForAReMeasure()
        {
            var profile = Profile();
            var music = CategoryNamed(profile, "Music");
            Assert.AreEqual(MeasureMode.Integrated, music.Mode, "Fixture precondition.");

            var result = CategoryEdit.Apply(profile, music, music.Name, music.OffsetDb,
                MeasureMode.MomentaryMax);

            Assert.IsTrue(result.NeedsAnalyze,
                "A mode change changes the MEASUREMENT, which Resolve cannot do.");
            Assert.AreEqual(MeasureMode.MomentaryMax, music.Mode);
        }

        [Test]
        public void Apply_RenamingOntoAnExistingCategory_IsRejectedAndLeavesTheNameAlone()
        {
            var profile = Profile();
            var music = CategoryNamed(profile, "Music");

            var result = CategoryEdit.Apply(profile, music, "SFX", music.OffsetDb, music.Mode);

            Assert.IsTrue(result.RenameRejected);
            Assert.AreEqual("Music", music.Name, "A silent merge would regroup every Music clip.");
        }

        /// <summary>
        /// The defect this extraction exists to pin. A rejected rename must not cancel a mode
        /// change arriving in the same edit: the old code folded <c>false</c> into the pending
        /// flag on the rejection path, wiping the whole request rather than its own
        /// contribution -- so the commit fell through to <c>Resolve</c>, which cannot
        /// re-measure, and baked a gain from the OLD mode's LUFS.
        /// </summary>
        [Test]
        public void Apply_WithARejectedRenameAndAModeChange_StillAsksForAReMeasure()
        {
            var profile = Profile();
            var music = CategoryNamed(profile, "Music");

            var result = CategoryEdit.Apply(profile, music, "SFX", music.OffsetDb,
                MeasureMode.MomentaryMax);

            Assert.IsTrue(result.RenameRejected);
            Assert.IsTrue(result.NeedsAnalyze,
                "The rejected rename must not cancel the mode change that came with it.");
        }

        [Test]
        public void Apply_WithARejectedRename_StillAppliesTheOffsetAndMode()
        {
            var profile = Profile();
            var music = CategoryNamed(profile, "Music");

            CategoryEdit.Apply(profile, music, "SFX", -7f, MeasureMode.MomentaryMax);

            Assert.AreEqual(-7f, music.OffsetDb, 1e-4f,
                "A name collision is no reason to drop the other values in the same edit.");
            Assert.AreEqual(MeasureMode.MomentaryMax, music.Mode);
        }
    }
}
