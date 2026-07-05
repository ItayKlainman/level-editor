using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Designer-tunable knobs for BusBuddiesAutofiller. Its own asset, referenced
    // from the autofiller asset (GameProfile stays game-agnostic) — mirrors
    // YAKSpoolAutofillConfig, but bus-scaled and WITHOUT the click-pattern /
    // complexity axis (Bus Buddies 1a has no equivalent; gating is APS-only).
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Analysis/Bus Buddies Auto-fill Config")]
    public sealed class BusBuddiesAutofillConfig : ScriptableObject
    {
        [Header("Bus capacity (balance is summed EXACTLY to per-color blocks)")]
        [Tooltip("Smallest bus capacity. A color whose total is below this becomes one undersized bus (documented exception).")]
        [Min(1)] public int MinCapacity = 3;
        [Tooltip("Largest bus capacity.")]
        [Min(1)] public int MaxCapacity = 12;
        [Tooltip("Preferred bus capacity; partitioning aims here, jittered per attempt.")]
        [Min(1)] public int AvgCapacity = 6;

        [Header("Columns")]
        [Tooltip("Allowed bus-column count (x=min, y=max). Bus Buddies queues support 1–5 columns.")]
        public Vector2Int ColumnRange = new Vector2Int(1, 5);

        [Header("APS target")]
        [Tooltip("Accept a candidate when |measured APS − target| <= this. Target comes from the request (panel APS slider) or DefaultApsTarget.")]
        public float ApsTolerance = 0.6f;
        [Tooltip("APS target used when the request supplies none.")]
        public float DefaultApsTarget = 3f;

        [Header("Active Bus Row")]
        [Tooltip("Active-row slot count used when the level's GameData[\"conveyorCount\"] is absent.")]
        [Min(1)] public int DefaultActiveSlots = 5;

        [Header("Hidden buses")]
        [Tooltip("Fraction of buses marked hidden (0..1). NOTE: the average-player analyzer does not yet model hidden buses, so measured APS will NOT reflect this difficulty (fast-follow).")]
        [Range(0f, 1f)] public float HiddenRatio = 0f;

        [Header("Search budget")]
        [Tooltip("Random candidate arrangements sampled before returning the best-so-far. Kept modest so batch generation stays fast.")]
        [Min(1)] public int MaxAttempts = 60;
        [Tooltip("Monte-Carlo playouts per candidate during the search (kept low for speed; the final pick is reported as-is).")]
        [Min(1)] public int SearchRolloutCount = 120;
        [Tooltip("Solver node budget per candidate.")]
        public long SearchNodeBudget = 50_000;
        [Tooltip("Solver wall-clock budget per candidate (ms).")]
        public long SearchTimeoutMs = 1_500;
        [Tooltip("Hard upper bound on the whole auto-fill loop (ms).")]
        public long TotalTimeoutMs = 30_000;

        private void OnValidate()
        {
            if (MaxCapacity < MinCapacity) MaxCapacity = MinCapacity;
            AvgCapacity = Mathf.Clamp(AvgCapacity, MinCapacity, MaxCapacity);
            ColumnRange.x = Mathf.Max(1, ColumnRange.x);
            ColumnRange.y = Mathf.Max(ColumnRange.x, ColumnRange.y);
        }
    }
}
