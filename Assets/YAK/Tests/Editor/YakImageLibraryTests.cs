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
            StringAssert.Contains("no outline of any color", p);  // outline ban (from default preamble)
            StringAssert.DoesNotContain("{idea}", p);     // placeholder was substituted
        }

        [Test]
        public void BuildPrompt_NoColors_OmitsColorClause()
        {
            string p = YAKImageLibraryCore.BuildPrompt("a rocket", new List<string>(), null);
            StringAssert.Contains("a rocket", p);
            StringAssert.DoesNotContain("Use only these flat solid colors", p);
        }

        private const string PromptsAsset =
            "# comment\n" +
            "[rules]\n" +
            "No outline.\n" +
            "\n" +
            "[animals]\n" +
            "A cute {idea} with\n" +
            "oversized eyes.\n" +
            "\n" +
            "[hybrids]\n" +
            "A whimsical hybrid {idea}.\n" +
            "\n" +
            "[default]\n" +
            "A pixel-art {idea}.\n";

        private const string IdeasAsset =
            "# the old list, before any @style tag\n" +
            "a red apple\n" +
            "\n" +
            "# @style: animals\n" +
            "a grumpy cat\n" +
            "# a plain comment inside the section — must NOT close it\n" +
            "a sleepy owl\n" +
            "\n" +
            "# @style: hybrids\n" +
            "a turtle with a watermelon shell\n";

        [Test]
        public void ParseStyleBlocks_NamesBlocksAndRejoinsWrappedLines()
        {
            var blocks = YAKImageLibraryCore.ParseStyleBlocks(PromptsAsset);

            Assert.AreEqual(4, blocks.Count);
            Assert.AreEqual("A cute {idea} with oversized eyes.", blocks["animals"]);
            Assert.AreEqual("No outline.", blocks["rules"]);
        }

        [Test]
        public void ParseIdeaEntries_BindsEachIdeaToItsSectionsPrompt()
        {
            var entries = YAKImageLibraryCore.ParseIdeaEntries(IdeasAsset);

            Assert.AreEqual(4, entries.Count);
            Assert.AreEqual("", entries[0].StyleKey, "an idea before any tag has no key -> [default]");
            Assert.AreEqual("animals", entries[1].StyleKey);   // a grumpy cat
            Assert.AreEqual("animals", entries[2].StyleKey);   // a plain '#' comment must NOT close the section
            Assert.AreEqual("hybrids", entries[3].StyleKey);
        }

        [Test]
        public void ResolveStyle_PicksTheSectionsPrompt_AndAlwaysAppendsRules()
        {
            var blocks = YAKImageLibraryCore.ParseStyleBlocks(PromptsAsset);

            string animals = YAKImageLibraryCore.ResolveStyle(blocks, "animals", null);
            string hybrids = YAKImageLibraryCore.ResolveStyle(blocks, "hybrids", null);

            Assert.AreEqual("A cute {idea} with oversized eyes. No outline.", animals);
            Assert.AreEqual("A whimsical hybrid {idea}. No outline.", hybrids);
        }

        [Test]
        public void ResolveStyle_UnknownOrMissingKey_FallsBackToDefault_ThenToCallerPreamble()
        {
            var blocks = YAKImageLibraryCore.ParseStyleBlocks(PromptsAsset);

            // A typo'd tag must not silently drop the rules — it lands on [default].
            Assert.AreEqual("A pixel-art {idea}. No outline.", YAKImageLibraryCore.ResolveStyle(blocks, "typo", null));
            Assert.AreEqual("A pixel-art {idea}. No outline.", YAKImageLibraryCore.ResolveStyle(blocks, "", null));

            // No asset at all -> the caller's own preamble.
            var empty = YAKImageLibraryCore.ParseStyleBlocks("");
            Assert.AreEqual("fallback", YAKImageLibraryCore.ResolveStyle(empty, "animals", "fallback"));
        }

        [Test]
        public void EachIdeaIsBuiltWithItsOwnSectionsPrompt()
        {
            // The whole point: the five prompts split the idea set.
            var blocks  = YAKImageLibraryCore.ParseStyleBlocks(PromptsAsset);
            var entries = YAKImageLibraryCore.ParseIdeaEntries(IdeasAsset);

            string cat = YAKImageLibraryCore.BuildPrompt(
                entries[1].Idea, null, YAKImageLibraryCore.ResolveStyle(blocks, entries[1].StyleKey, null));
            string turtle = YAKImageLibraryCore.BuildPrompt(
                entries[3].Idea, null, YAKImageLibraryCore.ResolveStyle(blocks, entries[3].StyleKey, null));

            Assert.AreEqual("A cute a grumpy cat with oversized eyes. No outline.", cat);
            Assert.AreEqual("A whimsical hybrid a turtle with a watermelon shell. No outline.", turtle);
        }

        [Test]
        public void DefaultStylePreamble_IsPixelArtCollectible_AndStillBansOutlines()
        {
            // The outline ban is load-bearing: an outline smears into a thick dark ring
            // when the image is downscaled to the level grid.
            string p = YAKImageLibraryCore.BuildPrompt("a duck", null, null);

            StringAssert.Contains("pixel-art collectible icon", p);   // the boss's art narrative
            StringAssert.Contains("oversized expressive eyes", p);
            StringAssert.Contains("no outline of any color", p);      // must survive the pixel-art rewrite
            StringAssert.Contains("one flat uniform solid color", p); // single background
        }

        // Guards the SHIPPED files, not a fixture: a typo'd "# @style:" tag or an
        // unbalanced section would quietly generate a batch with the wrong prompt.
        [Test]
        public void ShippedFiles_EveryBossBriefSectionBindsToARealPrompt_With20IdeasEach()
        {
            string dir = System.IO.Path.Combine(Application.dataPath, "YAK", "SourceImages");
            var blocks  = YAKImageLibraryCore.ParseStyleBlocks(
                System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "prompts.txt")));
            var entries = YAKImageLibraryCore.ParseIdeaEntries(
                System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "ideas.txt")));

            var expected = new[] { "animals", "objects", "hybrids", "fantasy", "funny" };
            foreach (var key in expected)
            {
                Assert.IsTrue(blocks.ContainsKey(key), $"prompts.txt is missing the [{key}] prompt");
                int n = entries.FindAll(e => string.Equals(e.StyleKey, key, System.StringComparison.OrdinalIgnoreCase)).Count;
                Assert.AreEqual(20, n, $"the '{key}' section should hold exactly 20 ideas (one prompt's share)");
            }

            Assert.IsTrue(blocks.ContainsKey("rules"),   "prompts.txt must keep the shared [rules] block");
            Assert.IsTrue(blocks.ContainsKey("default"), "prompts.txt must keep a [default] prompt for untagged ideas");

            // Every tag actually names a prompt.
            foreach (var e in entries)
                if (!string.IsNullOrEmpty(e.StyleKey))
                    Assert.IsTrue(blocks.ContainsKey(e.StyleKey),
                        $"idea '{e.Idea}' is tagged [{e.StyleKey}], which is not a prompt in prompts.txt");

            // The outline ban must survive edits to the prompt text — it is what keeps
            // a converted level from gaining a thick dark ring.
            StringAssert.Contains("Do NOT draw ANY outline", blocks["rules"]);
        }

        [Test]
        public void ApiKey_EnvVarTakesPrecedence_ThenEditorPrefs()
        {
            string prevEnv = System.Environment.GetEnvironmentVariable(YAKImageApiKey.EnvVar);
            try
            {
                YAKImageApiKey.ClearEditorPrefKey();
                System.Environment.SetEnvironmentVariable(YAKImageApiKey.EnvVar, "env-key");
                Assert.AreEqual("env-key", YAKImageApiKey.Resolve());
                Assert.AreEqual("env", YAKImageApiKey.Source());

                System.Environment.SetEnvironmentVariable(YAKImageApiKey.EnvVar, null);
                YAKImageApiKey.SetEditorPrefKey("pref-key");
                Assert.AreEqual("pref-key", YAKImageApiKey.Resolve());
                Assert.AreEqual("EditorPrefs", YAKImageApiKey.Source());
            }
            finally
            {
                System.Environment.SetEnvironmentVariable(YAKImageApiKey.EnvVar, prevEnv);
                YAKImageApiKey.ClearEditorPrefKey();
            }
        }

        [Test]
        public void BuildRequestJson_ContainsModelPromptSizeQualityAndN1()
        {
            string json = YAKOpenAIImageClient.BuildRequestJson("draw a cat", "gpt-image-1", "1024x1024", "medium");
            var o = Newtonsoft.Json.Linq.JObject.Parse(json);

            Assert.AreEqual("gpt-image-1", (string)o["model"]);
            Assert.AreEqual("draw a cat", (string)o["prompt"]);
            Assert.AreEqual("1024x1024", (string)o["size"]);
            Assert.AreEqual("medium", (string)o["quality"]);
            Assert.AreEqual(1, (int)o["n"]);
        }
    }
}
