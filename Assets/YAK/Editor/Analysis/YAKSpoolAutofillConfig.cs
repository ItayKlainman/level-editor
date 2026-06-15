using UnityEngine;

namespace Hoppa.YAK.Editor
{
    // Designer-tunable knobs for YAKSpoolAutofiller. Separate asset referenced
    // from the autofiller asset (GameProfile stays game-agnostic), mirroring the
    // YarnTwist analyzer/autofill config split.
    [CreateAssetMenu(menuName = "Hoppa/YAK/Analysis/YAK Spool Auto-fill Config")]
    public sealed class YAKSpoolAutofillConfig : ScriptableObject
    {
        [Header("Spool capacity (balance is summed EXACTLY to per-color wool)")]
        [Tooltip("Smallest spool capacity. A color whose total is below this becomes one undersized spool (documented exception).")]
        [Min(1)] public int MinCapacity = 10;
        [Tooltip("Largest spool capacity.")]
        [Min(1)] public int MaxCapacity = 30;
        [Tooltip("Preferred spool capacity; partitioning aims here, jittered per attempt.")]
        [Min(1)] public int AvgCapacity = 20;

        [Header("Columns")]
        [Tooltip("Allowed spool-column count (x=min, y=max). YAK game supports 2–5.")]
        public Vector2Int ColumnRange = new Vector2Int(2, 5);

        [Header("APS target")]
        [Tooltip("Accept a candidate when |measured APS − target| <= this. Target comes from the request (panel APS slider) or DefaultApsTarget.")]
        public float ApsTolerance = 0.6f;
        [Tooltip("APS target used when the request supplies none.")]
        public float DefaultApsTarget = 3f;

        [Header("Conveyor")]
        [Tooltip("Belt slot count used when the level's GameData[\"conveyorCount\"] is absent.")]
        [Min(1)] public int DefaultConveyorSlots = 5;

        [Header("Search budget")]
        [Tooltip("Random candidate arrangements sampled before returning the best-so-far.")]
        [Min(1)] public int MaxAttempts = 40;
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
