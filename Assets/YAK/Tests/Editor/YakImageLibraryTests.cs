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

        [Test]
        public void ParsePrompts_SplitsBlankLineBlocks_StripsEnumerators_Dedupes()
        {
            string raw =
                "# theme file\n" +
                "1) Cute animal with a funny face\nover two lines\n" +
                "\n" +
                "- A single everyday object as a character\n" +
                "\n" +
                "2. Cute animal with a funny face\nover two lines\n";   // dup of block 1 after strip+join
            var prompts = YAKImageLibraryCore.ParsePrompts(raw);

            Assert.AreEqual(2, prompts.Count, "two distinct themes; enumerators stripped so blocks 1 & 3 collide");
            Assert.AreEqual("Cute animal with a funny face over two lines", prompts[0]);
            Assert.AreEqual("A single everyday object as a character", prompts[1]);
        }

        [Test]
        public void ParsePrompts_EmptyOrBlank_ReturnsEmpty()
        {
            Assert.AreEqual(0, YAKImageLibraryCore.ParsePrompts(null).Count);
            Assert.AreEqual(0, YAKImageLibraryCore.ParsePrompts("\n\n#only a comment\n\n").Count);
        }

        [Test]
        public void BuildThemePrompt_InjectsThemeAndColors_KeepsConvertRules()
        {
            var colors = new List<string> { "Red #ff0000" };
            string p = YAKImageLibraryCore.BuildThemePrompt("cute forest spirit", colors, null);

            StringAssert.Contains("cute forest spirit", p);
            StringAssert.Contains("Red #ff0000", p);
            StringAssert.Contains("no outline of any color", p);      // convert rule from default theme preamble
            StringAssert.Contains("not a busy scene", p);             // single-subject rule
            StringAssert.DoesNotContain("{theme}", p);                // placeholder substituted
        }

        [Test]
        public void BuildThemePrompt_NoPlaceholder_AppendsTheme()
        {
            string p = YAKImageLibraryCore.BuildThemePrompt("dragons", null, "Flat art only.");
            StringAssert.Contains("dragons", p);
        }

        [Test]
        public void ThemeToFileName_SameThemeDistinctTokens_YieldDistinctNames()
        {
            string a = YAKImageLibraryCore.ThemeToFileName("Cute animal with a funny face", "tokenA");
            string b = YAKImageLibraryCore.ThemeToFileName("Cute animal with a funny face", "tokenB");
            string a2 = YAKImageLibraryCore.ThemeToFileName("Cute animal with a funny face", "tokenA");

            Assert.AreNotEqual(a, b, "same theme, different token -> different file (batches accumulate)");
            Assert.AreEqual(a, a2, "deterministic for a given (theme, token)");
            StringAssert.IsMatch("^[a-z0-9-]+_[0-9a-f]{8}\\.png$", a);
            StringAssert.StartsWith("cute-animal-with-a-funny-face_", a);
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
