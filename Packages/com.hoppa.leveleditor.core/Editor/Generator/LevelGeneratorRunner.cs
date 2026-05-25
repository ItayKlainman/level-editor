using System.Collections.Generic;
using Hoppa.LevelEditor.Core;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Shared helper Layer 2 generators call to evaluate a candidate against the
    // profile's existing validation rules. Returns the full report plus the
    // set of rule ids that emitted at least one Error entry. The generator
    // owns the reroll loop; this helper only evaluates one candidate.
    public static class LevelGeneratorRunner
    {
        public sealed class Evaluation
        {
            public ValidationReport Report;
            // Per-rule Error-entry count for this candidate. Used by the
            // generator to accumulate diagnostics across reroll attempts.
            public Dictionary<string, int> ErrorsByRule = new Dictionary<string, int>();
            public bool HasErrors;
        }

        public static Evaluation Evaluate(LevelDocument candidate, GameProfile profile)
        {
            var ctx      = new ValidationContext(candidate, profile.ColorPalette);
            var registry = profile.BuildValidationRegistry();
            var report   = registry.RunAll(ctx);

            var result = new Evaluation { Report = report, HasErrors = report.HasErrors };
            foreach (var entry in report.Entries)
            {
                if (entry.Severity != ValidationSeverity.Error) continue;
                var id = string.IsNullOrEmpty(entry.RuleId) ? "unknown" : entry.RuleId;
                result.ErrorsByRule.TryGetValue(id, out var n);
                result.ErrorsByRule[id] = n + 1;
            }
            return result;
        }
    }
}
