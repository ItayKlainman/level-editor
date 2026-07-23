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
    }
}
