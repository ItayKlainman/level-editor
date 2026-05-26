using System.Text;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Output of ILevelAnalyzer.Analyze. Game-agnostic: every analyzer reports
    // the same shape so the AutofillPanel can render results uniformly.
    public sealed class LevelAnalysisResult
    {
        public bool   Solvable;
        public long   WinPathCount;
        public bool   CountWasCapped;
        public long   StatesExplored;
        public long   ElapsedMs;
        public string FailureReason;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("Win paths: ");
            sb.Append(CountWasCapped ? "≥" + WinPathCount : WinPathCount.ToString());
            sb.Append(" · ");
            sb.Append(Solvable ? "solvable" : "unsolvable");
            sb.Append(" · ");
            sb.Append(StatesExplored).Append(" states · ").Append(ElapsedMs).Append(" ms");
            if (!string.IsNullOrEmpty(FailureReason))
                sb.Append(" · ").Append(FailureReason);
            return sb.ToString();
        }
    }
}
