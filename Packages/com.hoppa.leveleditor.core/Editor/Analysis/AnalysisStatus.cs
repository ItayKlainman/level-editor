namespace Hoppa.LevelEditor.Core.Editor
{
    // Outcome classification for an analysis run. Kept distinct so callers never
    // confuse "proven unsolvable" with "ran out of budget" or "threw" — a
    // confident wrong answer is worse than an honest "unknown".
    //
    // The legacy bool LevelAnalysisResult.Solvable is retained for back-compat;
    // new analyzers should set Status as the authoritative signal and keep
    // Solvable in sync (Solvable == Status == AnalysisStatus.Solvable).
    public enum AnalysisStatus
    {
        Unknown,     // not determined (default; e.g. search budget hit before a verdict)
        Solvable,    // a winning sequence exists (or was found)
        Unsolvable,  // proven no winning sequence exists within the reachable space
        TimedOut,    // wall-clock budget exhausted before a verdict
        Faulted,     // analysis threw, or input was malformed
    }
}
