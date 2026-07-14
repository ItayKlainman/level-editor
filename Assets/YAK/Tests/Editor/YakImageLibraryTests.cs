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
        public void ParseStylePrompt_DropsCommentsAndRejoinsWrappedLines()
        {
            string raw =
                "# the art narrative\n" +
                "# another comment\n" +
                "A single centered {idea}, drawn as a\n" +
                "premium pixel-art collectible icon.\n";
            string style = YAKImageLibraryCore.ParseStylePrompt(raw);

            Assert.AreEqual("A single centered {idea}, drawn as a premium pixel-art collectible icon.", style);
        }

        [Test]
        public void ParseStylePrompt_NoStyleText_ReturnsEmpty_SoCallerFallsBack()
        {
            Assert.AreEqual(string.Empty, YAKImageLibraryCore.ParseStylePrompt(null));
            Assert.AreEqual(string.Empty, YAKImageLibraryCore.ParseStylePrompt("# only comments\n\n"));
        }

        [Test]
        public void ParsedStyleAsset_DrivesTheBuiltPrompt()
        {
            // The art narrative is designer-edited in prompts.txt; whatever it says must
            // be what actually reaches the model, with {idea} substituted.
            string asset = "# style\nA chunky pixel-art {idea} with oversized eyes.";
            string style = YAKImageLibraryCore.ParseStylePrompt(asset);

            string p = YAKImageLibraryCore.BuildPrompt("a grumpy cat", null, style);

            Assert.AreEqual("A chunky pixel-art a grumpy cat with oversized eyes.", p);
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
