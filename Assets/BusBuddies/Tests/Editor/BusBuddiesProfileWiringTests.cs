using NUnit.Framework;
using UnityEditor;
using Hoppa.LevelEditor.Core.Editor;

namespace Hoppa.BusBuddies.Editor.Tests
{
    // Regression guard for the "no bus-editing panel" bug (2026-07-12):
    // the BusBuddiesProfile is configured with SpoolsBelowGrid = true, which makes
    // LevelEditorWindow relocate the TOP-section panel below the grid and leave the
    // BOTTOM-section slot empty. The bus-queue panel must therefore live in the
    // top-section slot, or nothing renders below the grid.
    public sealed class BusBuddiesProfileWiringTests
    {
        private const string ProfilePath =
            "Assets/BusBuddies/Data/Config/BusBuddiesProfile.asset";

        private static GameProfile LoadProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<GameProfile>(ProfilePath);
            Assert.IsNotNull(profile, $"BusBuddiesProfile not found at {ProfilePath}");
            return profile;
        }

        [Test]
        public void Profile_UsesSpoolsBelowGridLayout()
        {
            Assert.IsTrue(LoadProfile().SpoolsBelowGrid,
                "BusBuddies relies on the grid-on-top / queue-below layout.");
        }

        [Test]
        public void QueuePanel_IsRenderedBelowGrid()
        {
            var profile = LoadProfile();

            // In the SpoolsBelowGrid layout the window draws CreateTopSection() below
            // the grid; the bottom slot is intentionally left empty. So the queue panel
            // must be the top-section, and it must not fall back to an empty panel.
            var rendered = profile.CreateTopSection();
            Assert.IsInstanceOf<BusBuddiesQueuePanel>(rendered,
                "The bus-queue panel must be wired into the top-section slot so it " +
                "renders below the grid under SpoolsBelowGrid.");
        }
    }
}
