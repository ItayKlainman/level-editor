using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    // Tuning ScriptableObject for YarnTwistLevelGenerator. Every Difficulty-driven
    // knob is an AnimationCurve so designers hand-tune the shape in the inspector
    // without code edits. Override fields short-circuit a curve when set to >0.
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Generator/Yarn Twist Generator Config")]
    public sealed class YarnTwistGeneratorConfig : ScriptableObject
    {
        // ── Curves (Difficulty 1..10 → knob value) ────────────────────────

        [Header("Grid")]
        [Tooltip("Grid width in cells. Default: 5 → 9.")]
        public AnimationCurve GridWidth   = AnimationCurve.Linear(1f, 5f, 10f, 9f);

        [Tooltip("Grid height in cells. Default: 5 → 10.")]
        public AnimationCurve GridHeight  = AnimationCurve.Linear(1f, 5f, 10f, 10f);

        [Tooltip("Wall density as percent of total cells. Default: 0 → 25.")]
        public AnimationCurve WallDensity = AnimationCurve.Linear(1f, 0f, 10f, 25f);

        [Header("Cells")]
        [Tooltip("Colored-cell ratio as percent of non-wall, non-tunnel cells. Default: 60 → 35 (more empty space at higher Diff).")]
        public AnimationCurve BoxRatio       = AnimationCurve.Linear(1f, 60f, 10f, 35f);

        [Tooltip("Arrow-box ratio as percent of colored cells. Default: 0 → 30.")]
        public AnimationCurve ArrowBoxRatio  = AnimationCurve.Linear(1f, 0f, 10f, 30f);

        [Tooltip("Number of tunnels to place. Default: 0 → 3.")]
        public AnimationCurve TunnelCount    = AnimationCurve.Linear(1f, 0f, 10f, 3f);

        [Header("Colors & spools")]
        [Tooltip("Number of distinct colors used (sampled as the first N entries of the profile's palette). Default: 2 → 6.")]
        public AnimationCurve ColorCount       = AnimationCurve.Linear(1f, 2f, 10f, 6f);

        [Tooltip("Percentage of generated spools marked hidden. Default: 0 → 40.")]
        public AnimationCurve HiddenSpoolRatio = AnimationCurve.Linear(1f, 0f, 10f, 40f);

        [Header("Rewards")]
        [Tooltip("Coin reward written to gameData.coinReward. Default: 20 → 200.")]
        public AnimationCurve CoinReward = AnimationCurve.Linear(1f, 20f, 10f, 200f);

        // ── Generation control ────────────────────────────────────────────

        [Header("Generation control")]
        [Tooltip("Max number of full rerolls (with a derived sub-seed) before the generator gives up and returns the last partial candidate.")]
        [Min(1)] public int MaxRerollAttempts = 50;

        [Tooltip("Max queue length per tunnel. Caps random queue size to prevent runaway color totals.")]
        [Min(1)] public int MaxTunnelQueueLength = 3;

        // ── Advanced overrides (0 = use curve) ────────────────────────────

        [Header("Advanced overrides (0 = use curve)")]
        [Tooltip("Force a specific grid width. 0 = use GridWidth curve.")]
        [Min(0)] public int GridWidthOverride;

        [Tooltip("Force a specific grid height. 0 = use GridHeight curve.")]
        [Min(0)] public int GridHeightOverride;

        [Tooltip("Force a specific color count. 0 = use ColorCount curve.")]
        [Min(0)] public int ColorCountOverride;

        // Defensive: a freshly-created asset (or one whose YAML doesn't include
        // every curve) gets default linear curves so the generator never reads
        // an empty curve that evaluates to 0 everywhere.
        private void OnEnable()
        {
            EnsureCurve(ref GridWidth,        1f, 5f,  10f, 9f);
            EnsureCurve(ref GridHeight,       1f, 5f,  10f, 10f);
            EnsureCurve(ref WallDensity,      1f, 0f,  10f, 25f);
            EnsureCurve(ref BoxRatio,         1f, 60f, 10f, 35f);
            EnsureCurve(ref ArrowBoxRatio,    1f, 0f,  10f, 30f);
            EnsureCurve(ref TunnelCount,      1f, 0f,  10f, 3f);
            EnsureCurve(ref ColorCount,       1f, 2f,  10f, 6f);
            EnsureCurve(ref HiddenSpoolRatio, 1f, 0f,  10f, 40f);
            EnsureCurve(ref CoinReward,       1f, 20f, 10f, 200f);
            if (MaxRerollAttempts    < 1) MaxRerollAttempts    = 50;
            if (MaxTunnelQueueLength < 1) MaxTunnelQueueLength = 3;
        }

        private static void EnsureCurve(ref AnimationCurve curve,
            float t0, float v0, float t1, float v1)
        {
            if (curve == null || curve.length == 0)
                curve = AnimationCurve.Linear(t0, v0, t1, v1);
        }
    }
}
