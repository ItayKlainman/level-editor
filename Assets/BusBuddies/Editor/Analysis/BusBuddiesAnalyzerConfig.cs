using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Designer-tunable knobs for BusBuddiesAnalyzer. Its own asset, referenced from
    // the analyzer asset (GameProfile stays game-agnostic) — mirrors YAKAnalyzerConfig.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Analysis/Bus Buddies Analyzer Config")]
    public sealed class BusBuddiesAnalyzerConfig : ScriptableObject
    {
        [Header("Active Bus Row")]
        [Tooltip("Active-row slot count used when neither the request nor GameData[\"conveyorCount\"] supplies one. Surfaced to designers as 'Active Bus Slots'.")]
        [Min(1)] public int DefaultActiveSlots = 5;

        [Header("Exact solver (small grids only)")]
        [Tooltip("Run the exact BusSolver only when W*H <= this. Larger grids rely on Monte-Carlo rollout for solvability (rollout rescue).")]
        [Min(1)] public int SmallGridThreshold = 64;
        [Tooltip("Max search nodes the win-path solver may expand before giving up (a hit reports TimedOut, never Unsolvable).")]
        public long NodeBudget = 200_000;
        [Tooltip("Wall-clock budget (ms) for the win-path solver.")]
        public long TimeoutMs = 5_000;

        [Header("Average-player APS estimator")]
        [Tooltip("Carelessness e: probability of a random (non-greedy) pull per turn.")]
        [Range(0f, 1f)] public float Epsilon = 0.1f;
        [Tooltip("Greedy planning depth in plies. 1 = react to the current board only.")]
        [Min(1)] public int Lookahead = 1;
        [Tooltip("Monte-Carlo playout count per analysis. Higher = steadier win-rate, slower.")]
        [Min(1)] public int Runs = 400;
        [Tooltip("Base RNG seed for reproducible playouts (overridden by AnalysisRequest.Seed when non-zero).")]
        public int RngSeed = 12345;

        [Tooltip("FALSE until e/lookahead have been fitted to real player APS data. While false, results are flagged 'uncalibrated' so APS is never presented as ground truth.")]
        public bool ApsCalibrated = false;

        [Header("Difficulty bands")]
        [Tooltip("Ascending APS boundaries. Band = number of boundaries the measured APS meets or exceeds, plus 1. Default {2,4,6,8} -> bands 1..5.")]
        public float[] ApsBandThresholds = { 2f, 4f, 6f, 8f };

        private void OnEnable()
        {
            if (ApsBandThresholds == null || ApsBandThresholds.Length == 0)
                ApsBandThresholds = new[] { 2f, 4f, 6f, 8f };
        }

        public int BandFor(float aps)
        {
            int band = 1;
            if (ApsBandThresholds != null)
                foreach (var t in ApsBandThresholds)
                    if (aps >= t) band++;
            return band;
        }
    }
}
