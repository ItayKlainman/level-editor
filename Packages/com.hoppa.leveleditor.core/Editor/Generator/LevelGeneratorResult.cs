using System.Collections.Generic;
using Hoppa.LevelEditor.Core;

namespace Hoppa.LevelEditor.Core.Editor
{
    // What the generator returns. Document may be the last partial candidate
    // when reroll attempts are exhausted; check Succeeded to know whether all
    // profile.Rules passed for the returned candidate.
    public sealed class LevelGeneratorResult
    {
        public LevelDocument Document;
        public bool Succeeded;          // true = final candidate passed all Error-severity rules.
        public int SeedUsed;            // the actual root seed (resolved from Request.Seed; 0 → random).
        public int CandidatesTried;     // number of candidates evaluated before giving up / succeeding.
        public long ElapsedMs;
        public Dictionary<string, int> RuleRejectCounts = new Dictionary<string, int>();

        public override string ToString()
        {
            var reasons = string.Empty;
            if (RuleRejectCounts != null && RuleRejectCounts.Count > 0)
            {
                var parts = new List<string>(RuleRejectCounts.Count);
                foreach (var kv in RuleRejectCounts) parts.Add($"{kv.Key}×{kv.Value}");
                reasons = " · rejected: " + string.Join(", ", parts);
            }
            return $"Generated in {ElapsedMs} ms · {CandidatesTried} candidate(s){reasons} · seed {SeedUsed}";
        }
    }
}
