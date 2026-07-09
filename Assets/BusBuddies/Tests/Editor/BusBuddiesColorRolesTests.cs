using System.Collections.Generic;
using NUnit.Framework;
using Hoppa.BusBuddies.Editor;

namespace Hoppa.BusBuddies.Editor.Tests
{
    // Task 5: main-vs-background classification by pixel share, with the outline
    // color excluded from "main" when configured. Uses the spec's Excel example.
    public sealed class BusBuddiesColorRolesTests
    {
        private static Dictionary<string, int> Excel() => new Dictionary<string, int>
        {
            { "Purple", 500 }, { "Green", 200 }, { "Black", 100 }, { "Brown", 30 }, { "White", 20 },
        }; // total 850

        [Test]
        public void Classify_PicksMainColors_OutlineIncludedWhenFlagOff()
        {
            var main = BusBuddiesColorRoles.ClassifyMain(Excel(), 0.10f, "Black", false);
            Assert.IsTrue(main.Contains("Purple"));
            Assert.IsTrue(main.Contains("Green"));
            Assert.IsTrue(main.Contains("Black")); // 100/850 = 11.8% >= 10%
            Assert.IsFalse(main.Contains("Brown"));
            Assert.IsFalse(main.Contains("White"));
        }

        [Test]
        public void Classify_ExcludesOutline_WhenFlagOn()
        {
            var main = BusBuddiesColorRoles.ClassifyMain(Excel(), 0.10f, "Black", true);
            Assert.IsTrue(main.Contains("Purple"));
            Assert.IsTrue(main.Contains("Green"));
            Assert.IsFalse(main.Contains("Black"), "outline color must not be 'main' when excluded");
        }

        [Test]
        public void Classify_OutlineMatch_IsCaseInsensitive()
        {
            var main = BusBuddiesColorRoles.ClassifyMain(Excel(), 0.10f, "black", true);
            Assert.IsFalse(main.Contains("Black"));
        }

        [Test]
        public void Classify_EmptyOrNull_ReturnsEmpty()
        {
            Assert.AreEqual(0, BusBuddiesColorRoles.ClassifyMain(new Dictionary<string, int>(), 0.1f, null, true).Count);
            Assert.AreEqual(0, BusBuddiesColorRoles.ClassifyMain(null, 0.1f, null, true).Count);
        }
    }
}
