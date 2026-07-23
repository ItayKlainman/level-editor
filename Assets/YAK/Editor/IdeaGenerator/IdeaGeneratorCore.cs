using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Hoppa.YAK.Editor
{
    public static class IdeaGeneratorCore
    {
        public static string BuildPrompt(IdeaKnowledgeBase kb,
            IReadOnlyList<string> subjects, IReadOnlyList<string> modifiers,
            int amount, IReadOnlyList<string> existingIdeas)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Generate exactly {amount} pixel-art collectible idea lines.");
            sb.AppendLine("Flow per idea: pick one subject from the allowed libraries, pick a composition type, pick a complexity, add ONLY necessary modifiers, ensure uniqueness.");
            sb.AppendLine();
            sb.AppendLine("ALLOWED SUBJECT LIBRARIES (with example entries):");
            foreach (var s in kb.Subjects.Where(s => subjects.Contains(s.Name)))
                sb.AppendLine($"- {s.Name}: {string.Join(", ", s.Entries.Take(40))}");
            sb.AppendLine();
            sb.AppendLine("ALLOWED MODIFIERS (optional, use only when appropriate):");
            foreach (var m in kb.Modifiers.Where(m => modifiers.Contains(m.Name)))
                sb.AppendLine($"- {m.Name}: {m.Guidance}");
            sb.AppendLine();
            sb.AppendLine("COMPOSITION TYPES: " + string.Join("; ", kb.CompositionTypes));
            sb.AppendLine("TARGET COMPLEXITY DISTRIBUTION: " +
                string.Join(", ", kb.ComplexityDistribution.Select(c => $"{c.Name} {c.Percent}%")));
            sb.AppendLine();
            sb.AppendLine("DESIGN RULES:");
            foreach (var r in kb.DesignRules) sb.AppendLine($"- {r}");
            sb.AppendLine();
            sb.AppendLine("Do NOT repeat or lightly reword any of these EXISTING ideas:");
            foreach (var e in existingIdeas) sb.AppendLine($"- {e}");
            sb.AppendLine();
            sb.AppendLine("OUTPUT FORMAT: group by subject library. For each library used, emit a line '## <Library>' then one idea per line (lowercase, no trailing period, no numbering).");
            return sb.ToString();
        }
    }
}
