using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Output of ILevelCompleter.Complete. TopSection is the JSON blob the
    // caller can drop straight into LevelDocument.TopSection. Analysis is the
    // win-path analysis of the chosen fill, so the panel never has to re-run
    // the analyzer to display results.
    public sealed class LevelCompletionResult
    {
        public JObject               TopSection;
        public LevelAnalysisResult   Analysis;
        public bool                  Succeeded;
        public int                   CandidatesTried;
        public int                   SeedUsed;
        public long                  ElapsedMs;
        public Dictionary<int, int>  CandidatePathCountHistogram = new Dictionary<int, int>();
        public string                FailureReason;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Succeeded ? "Hit band" : "Best-effort");
            sb.Append(" · ").Append(CandidatesTried).Append(" candidate(s) · seed ").Append(SeedUsed);
            sb.Append(" · ").Append(ElapsedMs).Append(" ms");
            if (Analysis != null)
                sb.Append(" · win paths: ").Append(Analysis.CountWasCapped ? "≥" + Analysis.WinPathCount : Analysis.WinPathCount.ToString());
            if (!string.IsNullOrEmpty(FailureReason))
                sb.Append(" · ").Append(FailureReason);
            return sb.ToString();
        }
    }
}
