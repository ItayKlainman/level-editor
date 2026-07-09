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

        // LOW-4: outline matching is Ordinal (exact), consistent with how colors
        // are keyed everywhere else in the pipeline — no case-insensitive fallback.
        [Test]
        public void Classify_OutlineMatch_IsOrdinal_NotCaseInsensitive()
        {
            var main = BusBuddiesColorRoles.ClassifyMain(Excel(), 0.10f, "black", true);
            Assert.IsTrue(main.Contains("Black"), "\"black\" != \"Black\" under Ordinal — outline exclusion must not apply");
        }

        [Test]
        public void Classify_OutlineMatch_ExactCase_IsExcluded()
        {
            var main = BusBuddiesColorRoles.ClassifyMain(Excel(), 0.10f, "Black", true);
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
