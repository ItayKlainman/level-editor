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

        // Authoritative outcome classification. Distinct from a bare bool so
        // callers can tell "proven unsolvable" from "budget hit" / "faulted".
        // Defaults to Unknown; legacy analyzers that only set Solvable leave it
        // Unknown. Keep Solvable in sync when you set this.
        public AnalysisStatus Status = AnalysisStatus.Unknown;

        // Imperfect-information difficulty signal (0..1): fraction of simulated
        // myopic-player playouts that won. Populated only when RolloutsRun > 0.
        // Lower = harder. Unlike WinPathCount this never caps and reflects
        // hidden spools.
        public double WinRate;
        public long   RolloutsRun;

        // Measured difficulty: Attempts-Per-Solve for an average player
        // (≈ 1 / WinRate). 0 = not computed. ApsCalibrated is false until the
        // estimator's policy has been fitted to real player data — surface the
        // uncalibrated state, don't present it as ground truth.
        public float ApsEstimate;
        public bool  ApsCalibrated;

        // Measured click-pattern complexity (1..10) of average-player winning runs.
        // 0 = not computed (request did not set MeasureComplexity, or no win). Like
        // APS this is MEASURED-but-uncalibrated — do not present as ground truth.
        public float ComplexityEstimate;

        // Game-defined difficulty band derived from APS (e.g. 1..N on a curve).
        // 0 = unset. The mapping is game-specific; Layer 1 just carries it.
        public int Band;

        // One concrete winning action sequence as ordered action indices
        // (game-defined: e.g. grid item indices, or column taps). Stable/
        // canonical when the analyzer can produce it; null otherwise. This is
        // the machine-readable companion to SolutionSteps.
        public IReadOnlyList<int> WinPath;

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
            if (ApsEstimate > 0f)
            {
                sb.Append(" · APS ").Append(ApsEstimate.ToString("0.0"));
                if (!ApsCalibrated) sb.Append(" (uncalibrated)");
            }
            if (ComplexityEstimate > 0f)
                sb.Append(" · cplx ").Append(ComplexityEstimate.ToString("0.0"));
            sb.Append(" · ");
            sb.Append(StatesExplored).Append(" states · ").Append(ElapsedMs).Append(" ms");
            if (!string.IsNullOrEmpty(FailureReason))
                sb.Append(" · ").Append(FailureReason);
            return sb.ToString();
        }
    }
}
