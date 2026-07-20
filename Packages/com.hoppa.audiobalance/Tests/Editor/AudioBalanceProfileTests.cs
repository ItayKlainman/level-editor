using Hoppa.AudioBalance.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.AudioBalance.Editor.Tests
{
    public class AudioBalanceProfileTests
    {
        private static AudioClip MakeClip(string name)
        {
            return AudioClip.Create(name, 128, 1, 44100, false);
        }

        private static AudioBalanceProfile MakeProfile()
        {
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.ResetToDefaultCategories();
            return profile;
        }

        [Test]
        public void ResetToDefaultCategories_SeedsMusicSfxAndUi()
        {
            var profile = MakeProfile();

            Assert.AreEqual(3, profile.Categories.Count);
            Assert.AreEqual("Music", profile.Categories[0].Name);
            Assert.AreEqual(0f, profile.Categories[0].OffsetDb, 1e-4f);
            Assert.AreEqual(MeasureMode.Integrated, profile.Categories[0].Mode);

            Assert.AreEqual("SFX", profile.Categories[1].Name);
            Assert.AreEqual(3f, profile.Categories[1].OffsetDb, 1e-4f);
            Assert.AreEqual(MeasureMode.MomentaryMax, profile.Categories[1].Mode);

            Assert.AreEqual("UI", profile.Categories[2].Name);
            Assert.AreEqual(-6f, profile.Categories[2].OffsetDb, 1e-4f);
            Assert.AreEqual(MeasureMode.MomentaryMax, profile.Categories[2].Mode);
        }

        [Test]
        public void SettingsFor_CreatesAnEntryOnFirstCallAndReusesItAfter()
        {
            var profile = MakeProfile();
            var clip = MakeClip("kick");

            var first = profile.SettingsFor(clip);
            var second = profile.SettingsFor(clip);

            Assert.AreSame(first, second);
            Assert.AreEqual(1, profile.Clips.Count);
        }

        [Test]
        public void OffsetDbFor_UsesTheClipsAssignedCategory()
        {
            var profile = MakeProfile();
            var clip = MakeClip("blip");
            profile.SettingsFor(clip).Category = "UI";

            Assert.AreEqual(-6f, profile.OffsetDbFor(clip), 1e-4f);
        }

        [Test]
        public void ModeFor_UsesTheClipsAssignedCategory()
        {
            var profile = MakeProfile();
            var music = MakeClip("loop");
            profile.SettingsFor(music).Category = "Music";

            Assert.AreEqual(MeasureMode.Integrated, profile.ModeFor(music));
        }

        [Test]
        public void TrimDbFor_StacksOnTopOfTheCategoryOffset()
        {
            var profile = MakeProfile();
            var clip = MakeClip("trimmed");
            var settings = profile.SettingsFor(clip);
            settings.Category = "SFX";
            settings.TrimDb = -2.5f;

            Assert.AreEqual(3f, profile.OffsetDbFor(clip), 1e-4f);
            Assert.AreEqual(-2.5f, profile.TrimDbFor(clip), 1e-4f);
        }

        [Test]
        public void FindCategory_FallsBackToTheFirstCategoryForAnUnknownName()
        {
            var profile = MakeProfile();

            var found = profile.FindCategory("DoesNotExist");

            Assert.IsNotNull(found);
            Assert.AreEqual("Music", found.Name);
        }

        [Test]
        public void FindCategory_ReturnsNullWhenNoCategoriesExist()
        {
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.Categories.Clear();

            Assert.IsNull(profile.FindCategory("Music"));
        }

        [Test]
        public void OffsetDbFor_IsZeroWhenNoCategoriesExist()
        {
            var profile = ScriptableObject.CreateInstance<AudioBalanceProfile>();
            profile.Categories.Clear();

            Assert.AreEqual(0f, profile.OffsetDbFor(MakeClip("orphan")), 1e-4f);
        }

        [Test]
        public void SettingsFor_HandlesNullClipWithoutThrowing()
        {
            var profile = MakeProfile();

            Assert.IsNull(profile.SettingsFor(null));
            Assert.AreEqual(0f, profile.OffsetDbFor(null), 1e-4f);
            Assert.AreEqual(0f, profile.TrimDbFor(null), 1e-4f);
        }
    }
}
