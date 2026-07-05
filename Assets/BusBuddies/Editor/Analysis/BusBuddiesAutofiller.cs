using System;
using System.Collections.Generic;
using System.Diagnostics;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Fills a Bus Buddies level's bus queue (the top/bottom section) from its
    // hand-painted grid so that:
    //   • per-color bus capacity sums EXACTLY to per-color blocks (balance by
    //     construction), and
    //   • the resulting puzzle is analyzer-solvable with a measured APS near the
    //     target.
    //
    // Search is a seeded random sweep gated on BusBuddiesAnalyzer (NOT on static
    // rules): build a candidate queue → analyze → keep it only if Solvable, accept
    // on the first in-band APS, else return the closest solvable best-effort ranked
    // by APS distance (or an honest failure). Mirrors YAKSpoolAutofiller; the
    // partition/assignment logic is copied from YAK. Bus Buddies 1a has no
    // click-pattern axis, so this uses plain shuffle + round-robin assignment and
    // APS-only gating (no complexity target).
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Analysis/Bus Buddies Auto-fill")]
    public sealed class BusBuddiesAutofiller : LevelCompleterAsset
    {
        [SerializeField] private BusBuddiesAutofillConfig _config;

        // Bus Buddies authoring carries no per-mechanic toggles here (Hidden is a
        // config knob; connected-bus pairing is deferred / data-only).
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

            int colMin = Mathf.Max(1, cfg.ColumnRange.x);
            int colMax = Mathf.Max(colMin, cfg.ColumnRange.y);

            // Empty grid → empty queue, trivially done.
            if (perColor.Count == 0)
            {
                var emptyTop = new BusQueueData();
                int emptyCols = Mathf.Clamp(colMin, colMin, colMax);
                for (int i = 0; i < emptyCols; i++) emptyTop.Columns.Add(new BusColumn());
                result.TopSection = JObject.FromObject(emptyTop);
                result.Succeeded = true;
                return Done(result, sw);
            }

            int slots = ResolveSlots(doc, cfg);
            float targetAps = (req != null && req.TargetAPS.HasValue) ? req.TargetAPS.Value : cfg.DefaultApsTarget;
            int rootSeed = (req != null && req.Seed != 0) ? req.Seed : new System.Random().Next(1, int.MaxValue);
            result.SeedUsed = rootSeed;

            var rng = new System.Random(rootSeed);
            BusQueueData best = null; LevelAnalysisResult bestAnalysis = null; double bestDist = double.PositiveInfinity;
            BusQueueData firstUnsolvable = null; LevelAnalysisResult firstUnsolvableAnalysis = null;

            int attempts = Math.Max(1, cfg.MaxAttempts);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                if (sw.ElapsedMilliseconds > cfg.TotalTimeoutMs) break;

                int attemptSeed = rng.Next(1, int.MaxValue);
                var ar = new System.Random(attemptSeed);
                int columns = colMin + ar.Next(colMax - colMin + 1);

                var top = BuildCandidate(perColor, columns, cfg, ar);
                var candDoc = ShallowCopyWithTop(doc, JObject.FromObject(top));
                var analysis = analyzer.Analyze(candDoc, profile, new AnalysisRequest
                {
                    ConveyorCapacityOverride = slots,
                    RolloutCount = cfg.SearchRolloutCount,
                    NodeBudget   = cfg.SearchNodeBudget,
                    TimeoutMs    = cfg.SearchTimeoutMs,
                    Seed         = attemptSeed,
                });
                result.CandidatesTried++;

                if (analysis.Status != AnalysisStatus.Solvable)
                {
                    if (firstUnsolvable == null) { firstUnsolvable = top; firstUnsolvableAnalysis = analysis; }
                    continue;
                }

                double apsDist = Math.Abs(analysis.ApsEstimate - targetAps);
                if (apsDist <= cfg.ApsTolerance)
                {
                    result.TopSection = JObject.FromObject(top);
                    result.Analysis = analysis;
                    result.Succeeded = true;
                    return Done(result, sw);
                }
                if (apsDist < bestDist) { bestDist = apsDist; best = top; bestAnalysis = analysis; }
            }

            // Out of budget — return an honest best-effort.
            if (best != null)
            {
                result.TopSection = JObject.FromObject(best);
                result.Analysis = bestAnalysis;
                result.Succeeded = false;
                result.FailureReason =
                    $"closest solvable APS {bestAnalysis.ApsEstimate:0.0}/target {targetAps:0.0} " +
                    $"(±{cfg.ApsTolerance:0.0}) — target may be unreachable for this grid";
            }
            else if (firstUnsolvable != null)
            {
                result.TopSection = JObject.FromObject(firstUnsolvable);
                result.Analysis = firstUnsolvableAnalysis;
                result.Succeeded = false;
                result.FailureReason = "no solvable bus arrangement found for this grid (try fewer colors or more active slots)";
            }
            else
            {
                result.Succeeded = false;
                result.FailureReason = "no candidate produced";
            }
            return Done(result, sw);
        }

        // ── Candidate construction ────────────────────────────────────────────

        private static BusQueueData BuildCandidate(
            Dictionary<string, int> perColor, int columns, BusBuddiesAutofillConfig cfg, System.Random rng)
        {
            // One flat bus list across all colors, capacities summing exactly per color.
            var buses = new List<BusEntry>();
            foreach (var kv in perColor)
                foreach (var cap in Partition(kv.Value, cfg.MinCapacity, cfg.MaxCapacity, cfg.AvgCapacity, rng))
                    buses.Add(new BusEntry { ColorId = kv.Key, Capacity = cap, Hidden = false });

            Shuffle(buses, rng); // color mixing across columns

            // Mark a deterministic fraction hidden (post-shuffle → uniform selection).
            // NOTE: BusAveragePlayer does not yet consult Hidden, so this does NOT move
            // the measured APS — HiddenRatio is a manual difficulty knob until analyzer
            // hidden support lands (fast-follow).
            int hide = Mathf.Clamp(Mathf.RoundToInt(cfg.HiddenRatio * buses.Count), 0, buses.Count);
            for (int i = 0; i < hide; i++) buses[i].Hidden = true;

            var top = new BusQueueData();
            for (int i = 0; i < columns; i++) top.Columns.Add(new BusColumn());

            // No click-pattern axis in Bus Buddies 1a: plain round-robin across columns.
            for (int i = 0; i < buses.Count; i++)
                top.Columns[i % columns].Buses.Add(buses[i]);
            return top;
        }

        // Split `total` into capacities in [min,max] (jittered around avg) summing
        // EXACTLY to total. If total < min, returns one undersized bus (= total),
        // the documented exception. Never strands a remainder below min.
        // Copied verbatim from YAKSpoolAutofiller.Partition.
        public static List<int> Partition(int total, int min, int max, int avg, System.Random rng)
        {
            var caps = new List<int>();
            if (total <= 0) return caps;
            if (max < min) max = min;
            avg = Mathf.Clamp(avg, min, max);

            if (total <= max)
            {
                caps.Add(total); // single bus (may be < min only when total < min — accepted)
                return caps;
            }

            int remaining = total;
            while (remaining > max)
            {
                // jitter ±1 around avg, but keep enough so the remainder can still
                // form a valid bus (>= min).
                int jitter = rng.Next(-1, 2);
                int take = Mathf.Clamp(avg + jitter, min, Math.Min(max, remaining - min));
                if (take < min) take = min;
                caps.Add(take);
                remaining -= take;
            }
            caps.Add(remaining); // final chunk is in [min,max] by construction
            return caps;
        }

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

        private static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
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
