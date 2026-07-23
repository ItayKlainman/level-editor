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

        public sealed class IdeaGroup { public string Subject; public List<string> Ideas = new List<string>(); }

        public static List<IdeaGroup> ParseResponse(string modelText)
        {
            var groups = new List<IdeaGroup>();
            IdeaGroup current = null;
            foreach (var rawLine in (modelText ?? string.Empty).Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("##"))
                {
                    current = new IdeaGroup { Subject = line.TrimStart('#', ' ').Trim() };
                    groups.Add(current);
                    continue;
                }
                if (current == null) continue; // ignore preamble before first header
                var idea = StripLeadingMarker(line);
                if (idea.Length > 0) current.Ideas.Add(idea);
            }
            return groups;
        }

        private static string StripLeadingMarker(string s)
        {
            s = s.TrimStart();
            // strip "- ", "* ", "1. ", "12) "
            int i = 0;
            if (i < s.Length && (s[i] == '-' || s[i] == '*')) { i++; while (i < s.Length && s[i] == ' ') i++; return s.Substring(i).Trim(); }
            int d = i; while (d < s.Length && char.IsDigit(s[d])) d++;
            if (d > i && d < s.Length && (s[d] == '.' || s[d] == ')')) { d++; while (d < s.Length && s[d] == ' ') d++; return s.Substring(d).Trim(); }
            return s.Trim();
        }

        public static string NormalizeIdea(string idea)
        {
            var s = (idea ?? string.Empty).Trim().ToLowerInvariant().TrimEnd('.', '!', '?', ' ');
            foreach (var art in new[]{"a ","an ","the "})
                if (s.StartsWith(art)) { s = s.Substring(art.Length); break; }
            return s.Trim();
        }

        public static List<IdeaGroup> MarkAndFilterDuplicates(
            List<IdeaGroup> groups, IEnumerable<string> existing, out int dupeCount)
        {
            var seen = new HashSet<string>();
            foreach (var e in existing) seen.Add(NormalizeIdea(e));
            dupeCount = 0;
            foreach (var g in groups)
            {
                var kept = new List<string>();
                foreach (var idea in g.Ideas)
                {
                    var key = NormalizeIdea(idea);
                    if (key.Length == 0 || seen.Contains(key)) { dupeCount++; continue; }
                    seen.Add(key);
                    kept.Add(idea);
                }
                g.Ideas = kept;
            }
            return groups;
        }

        public static string BuildAppendBlock(List<IdeaGroup> groups, int batchNumber, string styleKey = "collectible")
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"# @style: {styleKey}");
            sb.AppendLine($"# @batch: {batchNumber}");
            foreach (var g in groups)
            {
                if (g.Ideas.Count == 0) continue;
                sb.AppendLine($"# {g.Subject}");
                foreach (var idea in g.Ideas) sb.AppendLine(idea);
            }
            return sb.ToString();
        }

        // Next batch number for a SPECIFIC @style section: the max "# @batch: N"
        // tag found while inside a "# @style: <styleKey>" section, plus one. Batch
        // tags under other styles are ignored, so a brand-new (absent/empty) section
        // starts at 1 even when other styles already have higher-numbered batches.
        public static int NextBatchNumber(string existingIdeasRaw, string styleKey)
        {
            const string styleTag = "# @style:";
            const string batchTag = "# @batch:";
            int max = 0;
            bool inSection = false;
            foreach (var raw in (existingIdeasRaw ?? string.Empty).Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith(styleTag))
                {
                    var name = line.Substring(styleTag.Length).Trim();
                    inSection = string.Equals(name, styleKey, System.StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (inSection && line.StartsWith(batchTag) &&
                    int.TryParse(line.Substring(batchTag.Length).Trim(), out var n))
                    max = System.Math.Max(max, n);
            }
            return max + 1;
        }
    }
}
