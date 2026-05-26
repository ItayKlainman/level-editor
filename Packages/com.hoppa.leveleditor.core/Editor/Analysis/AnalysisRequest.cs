using System;

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
    }

    public enum AnalysisMode
    {
        // Short-circuit at the first winning leaf; WinPathCount = 0 or 1.
        Solvable,

        // Exhaustive count of winning sequences up to WinPathCap.
        Count,
    }
}
