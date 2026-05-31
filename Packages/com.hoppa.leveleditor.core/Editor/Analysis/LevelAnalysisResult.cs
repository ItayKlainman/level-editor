using System.Collections.Generic;
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

        // Imperfect-information difficulty signal (0..1): fraction of simulated
        // myopic-player playouts that won. Populated only when RolloutsRun > 0.
        // Lower = harder. Unlike WinPathCount this never caps and reflects
        // hidden spools.
        public double WinRate;
        public long   RolloutsRun;

        // One concrete winning tap-ordering, as game-formatted human-readable
        // lines (e.g. "1. Tap pink Box (3,2)"). Populated only when the request
        // set RecordSolution and a solution exists; null/empty otherwise.
        public List<string> SolutionSteps;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("Win paths: ");
            sb.Append(CountWasCapped ? "≥" + WinPathCount : WinPathCount.ToString());
            sb.Append(" · ");
            sb.Append(Solvable ? "solvable" : "unsolvable");
            if (RolloutsRun > 0)
                sb.Append(" · win-rate ").Append((WinRate * 100.0).ToString("0.#")).Append('%');
            sb.Append(" · ");
            sb.Append(StatesExplored).Append(" states · ").Append(ElapsedMs).Append(" ms");
            if (!string.IsNullOrEmpty(FailureReason))
                sb.Append(" · ").Append(FailureReason);
            return sb.ToString();
        }
    }
}
