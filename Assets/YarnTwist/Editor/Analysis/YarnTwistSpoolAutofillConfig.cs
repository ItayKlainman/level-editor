using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    // Designer-tunable knobs for YarnTwistSpoolAutofiller. Lives as an asset
    // referenced from YarnTwistSpoolAutofiller.asset (not from GameProfile —
    // per the golden rule that GameProfile stays game-agnostic).
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Analysis/Yarn Twist Spool Auto-fill Config")]
    public sealed class YarnTwistSpoolAutofillConfig : ScriptableObject
    {
        [Header("APS target — Attempts Per Solve (primary)")]
        [Tooltip("Target number of distinct win paths per APS (1–6). APS 1 = many ways to win (easy); APS 6 = few. Evaluated at the integer APS. Used when the request supplies a TargetAPS.")]
        public AnimationCurve WinPathTargetByAPS =
            new AnimationCurve(new Keyframe(1f, 1500f), new Keyframe(6f, 2f));

        [Tooltip("Target win-rate per APS (1–6), used when the exact path count caps on big grids. Defaults to ≈ 1/APS (APS 1 ⇒ ~95% win, APS 6 ⇒ ~17%).")]
        public AnimationCurve WinRateTargetByAPS = new AnimationCurve(
            new Keyframe(1f, 0.95f), new Keyframe(2f, 0.5f), new Keyframe(3f, 0.33f),
            new Keyframe(4f, 0.25f), new Keyframe(5f, 0.2f),  new Keyframe(6f, 0.167f));

        [Header("Difficulty target (legacy fallback when no APS supplied)")]
        [Tooltip("Legacy: target win paths per Difficulty (1–10). Only used when a request omits TargetAPS.")]
        public AnimationCurve WinPathTargetByDifficulty =
            new AnimationCurve(new Keyframe(1f, 2000f), new Keyframe(10f, 5f));

        [Tooltip("Tolerance around the win-path target band, expressed as a fraction. 0.5 = accept any count within ±50% of target.")]
        [Range(0f, 1f)] public float WinPathTolerance = 0.5f;

        [Tooltip("Legacy: target win-rate per Difficulty (1–10). Only used when a request omits TargetAPS.")]
        public AnimationCurve WinRateTargetByDifficulty =
            new AnimationCurve(new Keyframe(1f, 0.92f), new Keyframe(10f, 0.12f));

        [Tooltip("Tolerance around the win-rate target band (absolute, 0..1). 0.15 = accept any win-rate within ±0.15 of target.")]
        [Range(0f, 1f)] public float WinRateTolerance = 0.15f;

        [Tooltip("Number of Monte-Carlo playouts per candidate. Higher = steadier win-rate estimate, slower. 0 disables the win-rate fallback.")]
        public int RolloutCount = 300;

        [Tooltip("How many spools past each column head the simulated player can plan against. Hidden spools inside the window are unknown, so this is what makes hidden spools affect measured difficulty.")]
        public int PlayerLookahead = 4;

        [Header("Mechanics — only applied when the matching panel toggle is checked")]
        [Tooltip("Percentage of generated spools marked hidden when the 'Hidden Spools' toggle is on. No longer tied to Difficulty/APS — the toggle decides on/off, this decides how many.")]
        [Range(0f, 100f)] public float HiddenSpoolPercent = 30f;

        [Tooltip("How many connected-spool pairs the auto-fill creates when the 'Connected Spools' toggle is on. Each pair links two spools in adjacent columns; pairs that would soft-lock the level are skipped, so a cramped layout yields fewer.")]
        public int ConnectedSpoolPairs = 2;

        [Header("Search budget")]
        [Tooltip("Maximum number of random candidate arrangements sampled in stage 1 before falling back to guided refinement / best-so-far.")]
        public int MaxRerollAttempts = 400;

        [Tooltip("Degree of parallelism for the stage-1 sampling sweep. 0 = use Environment.ProcessorCount.")]
        public int MaxDegreeOfParallelism = 0;

        [Tooltip("Maximum guided (hill-climbing) mutation steps in stage 2 when stage 1 fails to hit the band. 0 disables guided refinement.")]
        public int GuidedSteps = 300;

        [Tooltip("Analyzer's WinPathCap when called from the autofill loop.")]
        public int WinPathCap = 100_000;

        [Tooltip("Analyzer's TimeoutMs per autofill candidate.")]
        public int PerCandidateTimeoutMs = 1_500;

        [Tooltip("Hard upper bound on the autofill outer loop's total wall-clock time (ms).")]
        public int TotalTimeoutMs = 60_000;

        [Tooltip("Conveyor capacity used when the CompletionRequest doesn't override it.")]
        public int DefaultConveyorCapacity = 24;

        private void OnEnable()
        {
            // Defensive: Unity sometimes deserialises an AnimationCurve as
            // empty when the SO YAML doesn't include keyframes. Re-seed if so.
            if (WinPathTargetByAPS == null || WinPathTargetByAPS.length == 0)
                WinPathTargetByAPS = new AnimationCurve(new Keyframe(1f, 1500f), new Keyframe(6f, 2f));
            if (WinRateTargetByAPS == null || WinRateTargetByAPS.length == 0)
                WinRateTargetByAPS = new AnimationCurve(
                    new Keyframe(1f, 0.95f), new Keyframe(2f, 0.5f), new Keyframe(3f, 0.33f),
                    new Keyframe(4f, 0.25f), new Keyframe(5f, 0.2f),  new Keyframe(6f, 0.167f));
            if (WinPathTargetByDifficulty == null || WinPathTargetByDifficulty.length == 0)
                WinPathTargetByDifficulty = new AnimationCurve(new Keyframe(1f, 2000f), new Keyframe(10f, 5f));
            if (WinRateTargetByDifficulty == null || WinRateTargetByDifficulty.length == 0)
                WinRateTargetByDifficulty = new AnimationCurve(new Keyframe(1f, 0.92f), new Keyframe(10f, 0.12f));
        }
    }
}
