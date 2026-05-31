using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    // Designer-tunable knobs for YarnTwistSpoolAutofiller. Lives as an asset
    // referenced from YarnTwistSpoolAutofiller.asset (not from GameProfile —
    // per the golden rule that GameProfile stays game-agnostic).
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Analysis/Yarn Twist Spool Auto-fill Config")]
    public sealed class YarnTwistSpoolAutofillConfig : ScriptableObject
    {
        [Header("Win-path target (exact, small levels)")]
        [Tooltip("Target number of distinct win paths per Difficulty. Lower at high Difficulty = harder. Evaluated at the integer Difficulty.")]
        public AnimationCurve WinPathTargetByDifficulty =
            new AnimationCurve(new Keyframe(1f, 2000f), new Keyframe(10f, 5f));

        [Tooltip("Tolerance around the win-path target band, expressed as a fraction. 0.5 = accept any count within ±50% of target.")]
        [Range(0f, 1f)] public float WinPathTolerance = 0.5f;

        [Header("Win-rate target (imperfect-info, large levels)")]
        [Tooltip("Fallback difficulty signal used when the exact win-path count caps (big grids). Target fraction of myopic-player playouts that should win, per Difficulty. Lower at high Difficulty = harder.")]
        public AnimationCurve WinRateTargetByDifficulty =
            new AnimationCurve(new Keyframe(1f, 0.92f), new Keyframe(10f, 0.12f));

        [Tooltip("Tolerance around the win-rate target band (absolute, 0..1). 0.15 = accept any win-rate within ±0.15 of target.")]
        [Range(0f, 1f)] public float WinRateTolerance = 0.15f;

        [Tooltip("Number of Monte-Carlo playouts per candidate. Higher = steadier win-rate estimate, slower. 0 disables the win-rate fallback.")]
        public int RolloutCount = 300;

        [Tooltip("How many spools past each column head the simulated player can plan against. Hidden spools inside the window are unknown, so this is what makes the hidden ratio affect measured difficulty.")]
        public int PlayerLookahead = 4;

        [Header("Hidden spools")]
        [Tooltip("Percentage of generated spools marked hidden, per Difficulty. Hidden spools are unknown to the simulated player until they reach a column head, lowering the measured win-rate.")]
        public AnimationCurve HiddenSpoolRatio =
            new AnimationCurve(new Keyframe(1f, 0f), new Keyframe(10f, 40f));

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
            if (WinPathTargetByDifficulty == null || WinPathTargetByDifficulty.length == 0)
                WinPathTargetByDifficulty = new AnimationCurve(new Keyframe(1f, 2000f), new Keyframe(10f, 5f));
            if (WinRateTargetByDifficulty == null || WinRateTargetByDifficulty.length == 0)
                WinRateTargetByDifficulty = new AnimationCurve(new Keyframe(1f, 0.92f), new Keyframe(10f, 0.12f));
            if (HiddenSpoolRatio == null || HiddenSpoolRatio.length == 0)
                HiddenSpoolRatio = new AnimationCurve(new Keyframe(1f, 0f), new Keyframe(10f, 40f));
        }
    }
}
