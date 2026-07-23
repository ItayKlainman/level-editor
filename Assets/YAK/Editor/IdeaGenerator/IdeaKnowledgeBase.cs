using System.Collections.Generic;
using Newtonsoft.Json;

namespace Hoppa.YAK.Editor
{
    public sealed class IdeaKnowledgeBase
    {
        public List<SubjectLibrary> Subjects = new List<SubjectLibrary>();
        public List<Modifier> Modifiers = new List<Modifier>();
        public List<string> CompositionTypes = new List<string>();
        public List<ComplexityLevel> ComplexityDistribution = new List<ComplexityLevel>();
        public List<string> DesignRules = new List<string>();

        public static IdeaKnowledgeBase Parse(string json)
            => JsonConvert.DeserializeObject<IdeaKnowledgeBase>(json) ?? new IdeaKnowledgeBase();
    }

    public sealed class SubjectLibrary { public string Name; public List<string> Entries = new List<string>(); }
    public sealed class Modifier { public string Name; public string Guidance; }
    public sealed class ComplexityLevel { public string Name; public int Percent; }
}
