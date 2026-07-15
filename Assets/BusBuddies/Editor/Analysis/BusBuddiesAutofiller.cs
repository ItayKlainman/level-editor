using System;
using System.Collections.Generic;
using System.Diagnostics;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Fills a Bus Buddies level's bus queue from its hand-painted grid using the
    // DESIGNER difficulty model (the boss's Excel knobs), not an APS search:
    //
    //   1. Inventory per-color pixel counts.
    //   2. Resolve the six difficulty knobs (GameData → config defaults).
    //   3. Capacity math: Buses Chunks → avg px/bus; Deviation → [min,max].
    //   4. Partition each color into buses summing EXACTLY to its block count,
    //      applying the No-1-bus and Round-to-5 rules.
    //   5. Classify main vs background colors by pixel share.
    //   6. Dig-arrange the buses across the columns for the Difficulty depth,
    //      RELAXING burial (via BusBuddiesAnalyzer) until Solvable or baseline.
    //   7. Attach the measured APS from the final analysis as an informational
    //      read-out (NOT a gate). Honest failure only when no solvable
    //      arrangement exists at all.
    //
    // Deterministic: same settings + seed → identical queue.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Analysis/Bus Buddies Auto-fill")]
    public sealed class BusBuddiesAutofiller : LevelCompleterAsset
    {
        [SerializeField] private BusBuddiesAutofillConfig _config;

        // Exposed so the Difficulty panel can read the fixed mapping constants
        // (ChunksBase/Step) and fresh-level defaults for its live readout.
        public BusBuddiesAutofillConfig Config => _config;

        public override IReadOnlyList<string> MechanicToggles => null;

        public override LevelCompletionResult Complete(LevelDocument doc, GameProfile profile, CompletionRequest req)
        {
            var sw = Stopwatch.StartNew();
            var result = new LevelCompletionResult();
            var cfg = _config != null ? _config : ScriptableObject.CreateInstance<BusBuddiesAutofillConfig>();

            var analyzer = profile != null ? profile.LevelAnalyzer : null;
            if (analyzer == null)
            {
                result.FailureReason = "no analyzer wired on profile (BusBuddiesAutofiller needs BusBuddiesAnalyzer)";
                return Done(result, sw);
            }
            if (doc?.Grid == null)
            {
                result.FailureReason = "document or grid is null";
                return Done(result, sw);
            }

            // ── Inventory: per-color blocks from the grid (via IColoredCell) ──
            var perColor = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var cell in doc.Grid.Cells)
                if (cell is IColoredCell c && !string.IsNullOrEmpty(c.ColorId))
                {
                    perColor.TryGetValue(c.ColorId, out var n);
                    perColor[c.ColorId] = n + 1;
                }

            // ── Resolve the six designer knobs (GameData → config defaults) ──
            var settings = BusBuddiesDifficultySettings.ReadFrom(doc, cfg);
            int columns = Mathf.Clamp(settings.Columns, 1, 5);

            // Empty grid → empty queue, trivially done.
            if (perColor.Count == 0)
            {
                var emptyTop = new BusQueueData();
                for (int i = 0; i < columns; i++) emptyTop.Columns.Add(new BusColumn());
                result.TopSection = JObject.FromObject(emptyTop);
                result.Succeeded = true;
                return Done(result, sw);
            }

            int slots = ResolveSlots(doc, cfg);
            int seed = (req != null && req.Seed != 0) ? req.Seed : new System.Random().Next(1, int.MaxValue);
            result.SeedUsed = seed;
            var rootRng = new System.Random(seed); // deterministic attempt-seed stream

            // ── Capacity math (fixed by the designer knobs — never searched) ──
            int avg = BusBuddiesCapacityMath.Avg(settings.BusesChunks, cfg.ChunksBase, cfg.ChunksStep);
            BusBuddiesCapacityMath.Window(avg, settings.DeviationPercent, out int min, out int max);

            var colorIds = new List<string>(perColor.Keys);
            colorIds.Sort(StringComparer.Ordinal);

            // ── Classify main vs background colors by share (outline excluded). ──
            string outlineId = (profile.ImageToGrid as BusBuddiesImageToGrid)?.OutlineColorId;
            var mainColors = BusBuddiesColorRoles.ClassifyMain(
                perColor, cfg.MainColorShareThreshold, outlineId, cfg.ExcludeOutlineFromMain);

            // ── Constructive, solvable-by-construction arrangement ──
            // Build the queue by simulating a border-inward peel (BusBuddiesConstructiveArranger),
            // which guarantees a winning release order and verifies it by exact replay.
            // Partition is reseeded per attempt only as a robustness retry; construction
            // itself is deterministic and grid-size-independent (no exact-solver-skip
            // regime like the old Monte-Carlo gate). Bounded by the same MaxAttempts /
            // TotalTimeoutMs budget so batch generation stays fast.
            BusQueueData bestQueue = null;
            bool solvable = false;
            int attempts = Math.Max(1, cfg.MaxAttempts);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                if (sw.ElapsedMilliseconds > cfg.TotalTimeoutMs) break;
                int attemptSeed = rootRng.Next(1, int.MaxValue);
                var ar = new System.Random(attemptSeed);

                var buses = new List<BusEntry>();
                foreach (var id in colorIds)
                    foreach (var cap in BusBuddiesCapacityMath.PartitionColor(
                                 perColor[id], min, max, avg, settings.NoSingleBusColor, settings.RoundToFive, ar))
                        buses.Add(new BusEntry { ColorId = id, Capacity = cap, Hidden = false });

                var arr = BusBuddiesConstructiveArranger.Arrange(
                    doc.Grid, buses, columns, slots, settings.Difficulty, mainColors, ar);
                result.CandidatesTried++;
                if (arr.Solvable) { bestQueue = arr.Queue; solvable = true; break; }
                if (bestQueue == null) bestQueue = arr.Queue; // honest fallback
            }

            // Round-to-5 (when the level came out solvable): move round buses toward
            // each column's head, remainders to the tail — guarded so it never breaks
            // solvability. APS below then reads the final, shipped order.
            if (solvable && settings.RoundToFive)
                bestQueue = BusBuddiesConstructiveArranger.SortRoundToHead(
                    bestQueue, doc.Grid, columns, slots);

            result.TopSection = JObject.FromObject(bestQueue);
            result.Succeeded  = solvable;

            // Measured APS read-out (NOT a gate) from the analyzer on the chosen queue.
            var apsDoc = ShallowCopyWithTop(doc, result.TopSection);
            result.Analysis = analyzer.Analyze(apsDoc, profile, new AnalysisRequest
            {
                ConveyorCapacityOverride = slots,
                RolloutCount = cfg.SearchRolloutCount,
                NodeBudget   = cfg.SearchNodeBudget,
                TimeoutMs    = cfg.SearchTimeoutMs,
                Seed         = seed,
            });

            if (!solvable)
                result.FailureReason =
                    "could not construct a solvable arrangement — the grid may be color-unbalanced " +
                    "or contain a fully-enclosed region no bus order can reach";
            return Done(result, sw);
        }

        // ── Balance-by-construction primitive (kept for API parity + direct tests).
        // Delegates to BusBuddiesCapacityMath so there is one implementation. ──
        public static List<int> Partition(int total, int min, int max, int avg, System.Random rng)
            => BusBuddiesCapacityMath.Partition(total, min, max, avg, rng);

        // ── Helpers ───────────────────────────────────────────────────────────

        private static int ResolveSlots(LevelDocument doc, BusBuddiesAutofillConfig cfg)
        {
            var cc = doc.GameData?["conveyorCount"];
            if (cc != null && cc.Type != JTokenType.Null)
            {
                int v = (int)cc;
                if (v >= 1) return v;
            }
            return Mathf.Max(1, cfg.DefaultActiveSlots);
        }

        private static LevelDocument ShallowCopyWithTop(LevelDocument src, JObject top) => new LevelDocument
        {
            SchemaVersion = src.SchemaVersion,
            LevelId = src.LevelId,
            DisplayName = src.DisplayName,
            Metadata = src.Metadata,
            Grid = src.Grid,
            TopSection = top,
            GameData = src.GameData,
        };

        private static LevelCompletionResult Done(LevelCompletionResult r, Stopwatch sw)
        {
            sw.Stop();
            r.ElapsedMs = sw.ElapsedMilliseconds;
            return r;
        }
    }
}
