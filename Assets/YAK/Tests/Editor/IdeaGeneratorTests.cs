using NUnit.Framework;
using Hoppa.YAK.Editor;

namespace Hoppa.YAK.Editor.Tests
{
    public sealed class IdeaGeneratorTests
    {
        private const string MiniJson = @"{
          ""subjects"":[{""name"":""Animals"",""entries"":[""fox"",""owl""]},{""name"":""Music"",""entries"":[""guitar""]}],
          ""modifiers"":[{""name"":""Accessory"",""guidance"":""one worn item""}],
          ""compositionTypes"":[""subject only""],
          ""complexityDistribution"":[{""name"":""Simple"",""percent"":25},{""name"":""Medium"",""percent"":30}],
          ""designRules"":[""Keep silhouettes clear.""]
        }";

        [Test]
        public void Parse_ReadsSubjectsModifiersDistributionRules()
        {
            var kb = IdeaKnowledgeBase.Parse(MiniJson);
            Assert.AreEqual(2, kb.Subjects.Count);
            Assert.AreEqual("Animals", kb.Subjects[0].Name);
            CollectionAssert.AreEqual(new[]{"fox","owl"}, kb.Subjects[0].Entries);
            Assert.AreEqual("Accessory", kb.Modifiers[0].Name);
            Assert.AreEqual(2, kb.ComplexityDistribution.Count);
            Assert.AreEqual(30, kb.ComplexityDistribution[1].Percent);
            Assert.AreEqual(1, kb.DesignRules.Count);
        }

        [Test]
        public void BuildPrompt_IncludesSubjectsModifiersAmountRulesAndExisting()
        {
            var kb = IdeaKnowledgeBase.Parse(MiniJson);
            var p = IdeaGeneratorCore.BuildPrompt(kb,
                new[]{"Animals","Music"}, new[]{"Accessory"}, 20,
                new[]{"a red fox"});
            StringAssert.Contains("Animals", p);
            StringAssert.Contains("Music", p);
            StringAssert.Contains("Accessory", p);
            StringAssert.Contains("20", p);
            StringAssert.Contains("Keep silhouettes clear.", p);   // a design rule
            StringAssert.Contains("a red fox", p);                  // existing-idea uniqueness context
            StringAssert.Contains("Simple", p);                     // complexity distribution
        }

        [Test]
        public void ParseResponse_GroupsBySubject_SkipsNoiseAndNumbering()
        {
            var text = "## Animals\n1. a red fox\n- an owl with big eyes\n\n## Music\na smiling guitar\nrandom preamble that isn't a header line? keep as idea only under a subject";
            var groups = IdeaGeneratorCore.ParseResponse(text);
            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual("Animals", groups[0].Subject);
            CollectionAssert.Contains(groups[0].Ideas, "a red fox");   // numbering "1." stripped
            CollectionAssert.Contains(groups[0].Ideas, "an owl with big eyes"); // "- " stripped
            Assert.AreEqual("Music", groups[1].Subject);
            CollectionAssert.Contains(groups[1].Ideas, "a smiling guitar");
        }

        [Test]
        public void ParseResponse_LinesBeforeAnyHeaderAreIgnored()
        {
            var groups = IdeaGeneratorCore.ParseResponse("intro line\n## Music\na guitar");
            Assert.AreEqual(1, groups.Count);
            CollectionAssert.DoesNotContain(groups[0].Ideas, "intro line");
        }

        [Test]
        public void Normalize_IgnoresArticleCasePunctuation()
        {
            Assert.AreEqual(IdeaGeneratorCore.NormalizeIdea("A Red Fox."),
                            IdeaGeneratorCore.NormalizeIdea("red fox"));
        }

        [Test]
        public void MarkAndFilterDuplicates_RemovesAgainstExistingAndWithinBatch()
        {
            var groups = new System.Collections.Generic.List<IdeaGeneratorCore.IdeaGroup> {
                new IdeaGeneratorCore.IdeaGroup { Subject="Animals",
                    Ideas = new System.Collections.Generic.List<string>{ "a red fox", "an owl", "a RED fox" } }
            };
            var kept = IdeaGeneratorCore.MarkAndFilterDuplicates(groups, new[]{"the owl"}, out var dupes);
            CollectionAssert.AreEqual(new[]{"a red fox"}, kept[0].Ideas); // owl dropped (existing), 2nd fox dropped (within-batch)
            Assert.AreEqual(2, dupes);
        }

        [Test]
        public void BuildAppendBlock_EmitsStyleBatchAndPerSubjectHeaders()
        {
            var groups = new System.Collections.Generic.List<IdeaGeneratorCore.IdeaGroup> {
                new IdeaGeneratorCore.IdeaGroup { Subject="Animals", Ideas=new System.Collections.Generic.List<string>{"a red fox"} },
                new IdeaGeneratorCore.IdeaGroup { Subject="Music",   Ideas=new System.Collections.Generic.List<string>{"a smiling guitar"} },
            };
            var block = IdeaGeneratorCore.BuildAppendBlock(groups, 3);
            StringAssert.Contains("# @style: collectible", block);
            StringAssert.Contains("# @batch: 3", block);
            StringAssert.Contains("# Animals", block);
            StringAssert.Contains("a red fox", block);
            StringAssert.Contains("# Music", block);
            StringAssert.Contains("a smiling guitar", block);
        }

        [Test]
        public void NextBatchNumber_IsOneMoreThanMaxBatch_ForThatStyleOnly()
        {
            var raw =
                "# @style: animals\n# @batch: 5\na fox\n" +
                "# @style: collectible\n# @batch: 1\na cat\n# @batch: 2\na dog\n" +
                "# @style: objects\n# @batch: 9\na mug\n";
            // ignores animals(5) and objects(9); collectible max is 2 -> next is 3
            Assert.AreEqual(3, IdeaGeneratorCore.NextBatchNumber(raw, "collectible"));
        }

        [Test]
        public void NextBatchNumber_NoSuchStyleSection_ReturnsOne()
        {
            var raw = "# @style: animals\n# @batch: 3\na fox\n";
            Assert.AreEqual(1, IdeaGeneratorCore.NextBatchNumber(raw, "collectible"));
        }

        [Test]
        public void BuildChatRequestJson_HasModelAndBothMessages()
        {
            var json = YAKOpenAIChatClient.BuildChatRequestJson("SYS", "USER", "gpt-4o-mini");
            var o = Newtonsoft.Json.Linq.JObject.Parse(json);
            Assert.AreEqual("gpt-4o-mini", (string)o["model"]);
            var msgs = (Newtonsoft.Json.Linq.JArray)o["messages"];
            Assert.AreEqual(2, msgs.Count);
            Assert.AreEqual("system", (string)msgs[0]["role"]);
            Assert.AreEqual("SYS", (string)msgs[0]["content"]);
            Assert.AreEqual("user", (string)msgs[1]["role"]);
            Assert.AreEqual("USER", (string)msgs[1]["content"]);
        }
    }
}
