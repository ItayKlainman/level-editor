namespace Hoppa.LevelEditor.Core.Editor
{
    // Inputs to ILevelAnalyzer.Analyze. Created by the AutofillPanel for standalone
    // analyses and by ILevelCompleter implementations during their reroll loops.
    public sealed class AnalysisRequest
    {
        public AnalysisMode Mode = AnalysisMode.Count;

        // Stop counting once this many distinct winning sequences have been found.
        // The result is reported as the cap value with CountWasCapped = true.
        public int WinPathCap = 10_000;

        // Soft per-call timeout. Analyzers should check elapsed time at every
        // recursion entry and abort with whatever count they have so far.
        public long TimeoutMs = 5_000;

        // Game-specific override for the conveyor / belt / queue capacity.
        // Null = analyzer uses its own default.
        public int? ConveyorCapacityOverride;

        // Number of Monte-Carlo playouts to run for the imperfect-information
        // WinRate metric. 0 = skip rollouts. When Mode == WinRate this is the
        // primary signal; under Mode == Count any positive value also populates
        // WinRate alongside the exact count (one analyzer call, two metrics).
        public int RolloutCount = 0;

        // How many spools past each column's head the simulated player is
        // allowed to "plan against". Hidden spools inside this window are
        // unknown (covered-until-head), so a larger window only helps when
        // spools are visible — this is what makes the hidden ratio bite.
        public int PlayerLookahead = 4;

        // When true the analyzer records one concrete winning tap-ordering (the
        // first the exact solver finds) into LevelAnalysisResult.SolutionSteps.
        // Runs a fast first-solution search; does not affect the win-path count.
        public bool RecordSolution = false;
    }

    public enum AnalysisMode
    {
        // Short-circuit at the first winning leaf; WinPathCount = 0 or 1.
        Solvable,

        // Exhaustive count of winning sequences up to WinPathCap.
        Count,

        // Imperfect-information difficulty: fraction of myopic-player playouts
        // that win. Never caps and scales to arbitrarily large grids. Skips the
        // exhaustive DFS entirely.
        WinRate,
    }
}
