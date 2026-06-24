using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.YAK.Editor;
using UnityEngine;

namespace Hoppa.YAK.Editor.Tests
{
    public sealed class YakImageLibraryTests
    {
        [Test]
        public void ParseIdeas_TrimsSkipsCommentsAndDedupes()
        {
            string raw = "monkey eating banana\n  dolphin  \n\n# a comment\npopsicle\nDolphin\n";
            var ideas = YAKImageLibraryCore.ParseIdeas(raw);

            Assert.AreEqual(new List<string> { "monkey eating banana", "dolphin", "popsicle" }, ideas,
                "blank lines and # comments dropped; case-insensitive de-dupe keeps first 'dolphin'");
        }

        [Test]
        public void IdeaToFileName_IsDeterministic_SlugifiesAndAvoidsCollisions()
        {
            string a1 = YAKImageLibraryCore.IdeaToFileName("Monkey eating a Banana!");
            string a2 = YAKImageLibraryCore.IdeaToFileName("Monkey eating a Banana!");
            string b  = YAKImageLibraryCore.IdeaToFileName("monkey eating a banana");

            Assert.AreEqual(a1, a2, "same idea -> same filename");
            Assert.AreNotEqual(a1, b, "different ideas -> different filenames (hash suffix)");
            StringAssert.IsMatch("^[a-z0-9-]+_[0-9a-f]{8}\\.png$", a1);
            StringAssert.StartsWith("monkey-eating-a-banana_", a1);
        }

        [Test]
        public void FindMissing_ReturnsOnlyIdeasWithoutAFile()
        {
            var ideas = new[] { "dolphin", "popsicle", "rocket" };
            string have = YAKImageLibraryCore.IdeaToFileName("popsicle");
            var existing = new[] { "C:/x/" + have, "unrelated.png" };

            var missing = YAKImageLibraryCore.FindMissing(ideas, existing);

            Assert.AreEqual(new List<string> { "dolphin", "rocket" }, missing);
        }

        [Test]
        public void BuildPrompt_InjectsIdeaAndColorsAndConstraints()
        {
            var colors = new List<string> { "Red #ff0000", "Sky Blue #66ccff" };
            string p = YAKImageLibraryCore.BuildPrompt("a popsicle", colors, null);

            StringAssert.Contains("a popsicle", p);
            StringAssert.Contains("Red #ff0000", p);
            StringAssert.Contains("Sky Blue #66ccff", p);
            StringAssert.Contains("No gradients", p);     // from default preamble
            StringAssert.DoesNotContain("{idea}", p);     // placeholder was substituted
        }

        [Test]
        public void BuildPrompt_NoColors_OmitsColorClause()
        {
            string p = YAKImageLibraryCore.BuildPrompt("a rocket", new List<string>(), null);
            StringAssert.Contains("a rocket", p);
            StringAssert.DoesNotContain("Use only these flat solid colors", p);
        }
    }
}
