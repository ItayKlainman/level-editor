using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    // Designer-tunable knobs for YarnTwistSpoolAutofiller. Lives as an asset
    // referenced from YarnTwistSpoolAutofiller.asset (not from GameProfile —
    // per the golden rule that GameProfile stays game-agnostic).
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Analysis/Yarn Twist Spool Auto-fill Config")]
    public sealed class YarnTwistSpoolAutofillConfig : ScriptableObject
    {
        [Tooltip("Target number of distinct win paths per Difficulty. Lower at high Difficulty = harder. Evaluated at the integer Difficulty.")]
        public AnimationCurve WinPathTargetByDifficulty =
            new AnimationCurve(new Keyframe(1f, 2000f), new Keyframe(10f, 5f));

        [Tooltip("Tolerance around the target band, expressed as a fraction. 0.5 = accept any count within ±50% of target.")]
        [Range(0f, 1f)] public float WinPathTolerance = 0.5f;

        [Tooltip("Percentage of generated spools marked hidden, per Difficulty. Hidden flags are placed by the autofiller but ignored by the analyzer's path counting.")]
        public AnimationCurve HiddenSpoolRatio =
            new AnimationCurve(new Keyframe(1f, 0f), new Keyframe(10f, 40f));

        [Tooltip("Maximum number of candidate spool arrangements to try before giving up and returning the best-so-far.")]
        public int MaxRerollAttempts = 100;

        [Tooltip("Analyzer's WinPathCap when called from the autofill loop.")]
        public int WinPathCap = 10_000;

        [Tooltip("Analyzer's TimeoutMs per autofill candidate.")]
        public int PerCandidateTimeoutMs = 500;

        [Tooltip("Hard upper bound on the autofill outer loop's total wall-clock time (ms).")]
        public int TotalTimeoutMs = 30_000;

        [Tooltip("Conveyor capacity used when the CompletionRequest doesn't override it.")]
        public int DefaultConveyorCapacity = 24;

        private void OnEnable()
        {
            // Defensive: Unity sometimes deserialises an AnimationCurve as
            // empty when the SO YAML doesn't include keyframes. Re-seed if so.
            if (WinPathTargetByDifficulty == null || WinPathTargetByDifficulty.length == 0)
                WinPathTargetByDifficulty = new AnimationCurve(new Keyframe(1f, 2000f), new Keyframe(10f, 5f));
            if (HiddenSpoolRatio == null || HiddenSpoolRatio.length == 0)
                HiddenSpoolRatio = new AnimationCurve(new Keyframe(1f, 0f), new Keyframe(10f, 40f));
        }
    }
}
