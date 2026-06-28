using System;
using System.Collections.Generic;
using System.Diagnostics;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.YAK.Editor
{
    // Fills a YAK level's spool section (the bottom section) from its hand-painted
    // grid so that:
    //   • per-color capacity sums EXACTLY to per-color wool (balance by construction), and
    //   • the resulting puzzle is analyzer-solvable with a measured APS near the target.
    //
    // Search is a seeded random sweep gated on YAKLevelAnalyzer (NOT on static
    // rules): build a candidate spool layout → analyze → keep it only if solvable,
    // accept on the first in-band APS, else return the closest solvable best-effort
    // (or an honest "no solvable arrangement" / "target unreachable"). Mirrors the
    // YarnTwist autofiller's wiring; the partition/assignment logic is YAK's own.
    [CreateAssetMenu(menuName = "Hoppa/YAK/Analysis/YAK Spool Auto-fill")]
    public sealed class YAKSpoolAutofiller : LevelCompleterAsset
    {
        [SerializeField] private YAKSpoolAutofillConfig _config;

        // YAK v1 has no spool-side mechanics (no hidden/connected), so no toggles.
        public override IReadOnlyList<string> MechanicToggles => null;

        public override LevelCompletionResult Complete(LevelDocument doc, GameProfile profile, CompletionRequest req)
        {
            var sw = Stopwatch.StartNew();
            var result = new LevelCompletionResult();
            var cfg = _config != null ? _config : ScriptableObject.CreateInstance<YAKSpoolAutofillConfig>();

            var analyzer = profile != null ? profile.LevelAnalyzer : null;
            if (analyzer == null)
            {
                result.FailureReason = "no analyzer wired on profile (YAKSpoolAutofiller needs YAKLevelAnalyzer)";
                return Done(result, sw);
            }
            if (doc?.Grid == null)
            {
                result.FailureReason = "document or grid is null";
                return Done(result, sw);
            }

            // ── Inventory: per-color wool from the grid (via IColoredCell) ──
            var perColor = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var cell in doc.Grid.Cells)
                if (cell is IColoredCell c && !string.IsNullOrEmpty(c.ColorId))
                {
                    perColor.TryGetValue(c.ColorId, out var n);
                    perColor[c.ColorId] = n + 1;
                }

            // Empty grid → empty spool section, trivially done.
            if (perColor.Count == 0)
            {
                var emptyTop = new YAKTopSectionData();
                int emptyCols = Mathf.Clamp(2, cfg.ColumnRange.x, cfg.ColumnRange.y);
                for (int i = 0; i < emptyCols; i++) emptyTop.Columns.Add(new YAKSpoolColumn());
                result.TopSection = JObject.FromObject(emptyTop);
                result.Succeeded = true;
                return Done(result, sw);
            }

            int slots = ResolveSlots(doc, cfg);
            float targetAps = (req != null && req.TargetAPS.HasValue) ? req.TargetAPS.Value : cfg.DefaultApsTarget;
            int targetComplexity = (req != null && req.TargetComplexity.HasValue)
                ? Mathf.Clamp(req.TargetComplexity.Value, 1, 10) : cfg.DefaultComplexity;
            int rootSeed = (req != null && req.Seed != 0) ? req.Seed : new System.Random().Next(1, int.MaxValue);
            result.SeedUsed = rootSeed;

            int colMin = Mathf.Max(1, cfg.ColumnRange.x);
            int colMax = Mathf.Max(colMin, cfg.ColumnRange.y);

            var rng = new System.Random(rootSeed);
            YAKTopSectionData best = null; LevelAnalysisResult bestAnalysis = null; double bestDist = double.PositiveInfinity;
            YAKTopSectionData firstUnsolvable = null; LevelAnalysisResult firstUnsolvableAnalysis = null;

            int attempts = Math.Max(1, cfg.MaxAttempts);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                if (sw.ElapsedMilliseconds > cfg.TotalTimeoutMs) break;

                int attemptSeed = rng.Next(1, int.MaxValue);
                var ar = new System.Random(attemptSeed);
                int columns = colMin + ar.Next(colMax - colMin + 1);

                var top = BuildCandidate(perColor, columns, targetComplexity, cfg, ar);
                var candDoc = ShallowCopyWithTop(doc, JObject.FromObject(top));
                var analysis = analyzer.Analyze(candDoc, profile, new AnalysisRequest
                {
                    ConveyorCapacityOverride = slots,
                    RolloutCount = cfg.SearchRolloutCount,
                    NodeBudget   = cfg.SearchNodeBudget,
                    TimeoutMs    = cfg.SearchTimeoutMs,
                    Seed         = attemptSeed,
                    MeasureComplexity = true,
                });
                result.CandidatesTried++;

                if (analysis.Status != AnalysisStatus.Solvable)
                {
                    if (firstUnsolvable == null) { firstUnsolvable = top; firstUnsolvableAnalysis = analysis; }
                    continue;
                }

                double apsDist  = Math.Abs(analysis.ApsEstimate - targetAps);
                double cplxDist = Math.Abs(analysis.ComplexityEstimate - targetComplexity);
                bool apsOk  = apsDist  <= cfg.ApsTolerance;
                bool cplxOk = cplxDist <= cfg.ComplexityTolerance;
                if (apsOk && cplxOk)
                {
                    result.TopSection = JObject.FromObject(top);
                    result.Analysis = analysis;
                    result.Succeeded = true;
                    return Done(result, sw);
                }
                // Combined normalized distance for best-effort ranking.
                double combined = apsDist / Math.Max(1e-3, cfg.ApsTolerance)
                                + cplxDist / Math.Max(1e-3, cfg.ComplexityTolerance);
                if (combined < bestDist) { bestDist = combined; best = top; bestAnalysis = analysis; }
            }

            // Out of budget — return an honest best-effort.
            if (best != null)
            {
                result.TopSection = JObject.FromObject(best);
                result.Analysis = bestAnalysis;
                result.Succeeded = false;
                result.FailureReason =
                    $"closest solvable APS {bestAnalysis.ApsEstimate:0.0}/target {targetAps:0.0} (±{cfg.ApsTolerance:0.0}), " +
                    $"complexity {bestAnalysis.ComplexityEstimate:0.0}/target {targetComplexity} (±{cfg.ComplexityTolerance:0.0}) — target may be unreachable for this grid";
            }
            else if (firstUnsolvable != null)
            {
                result.TopSection = JObject.FromObject(firstUnsolvable);
                result.Analysis = firstUnsolvableAnalysis;
                result.Succeeded = false;
                result.FailureReason = "no solvable spool arrangement found for this grid (try fewer colors or more conveyor slots)";
            }
            else
            {
                result.Succeeded = false;
                result.FailureReason = "no candidate produced";
            }
            return Done(result, sw);
        }

        // ── Candidate construction ────────────────────────────────────────────

        private static YAKTopSectionData BuildCandidate(
            Dictionary<string, int> perColor, int columns, int complexity,
            YAKSpoolAutofillConfig cfg, System.Random rng)
        {
            // One flat spool list across all colors, capacities summing exactly per color.
            var spools = new List<YAKSpoolEntry>();
            foreach (var kv in perColor)
                foreach (var cap in Partition(kv.Value, cfg.MinCapacity, cfg.MaxCapacity, cfg.AvgCapacity, rng))
                    spools.Add(new YAKSpoolEntry { ColorId = kv.Key, Capacity = cap, Hidden = false });

            Shuffle(spools, rng); // color mixing across columns

            // Mark a deterministic fraction hidden (post-shuffle → uniform random selection).
            // NOTE: YakAveragePlayer does not yet consult Hidden, so this does NOT move the
            // measured APS — HiddenRatio is a manual difficulty knob until analyzer hidden
            // support lands (fast-follow). No silent capping here.
            int hide = Mathf.Clamp(Mathf.RoundToInt(cfg.HiddenRatio * spools.Count), 0, spools.Count);
            for (int i = 0; i < hide; i++) spools[i].Hidden = true;

            var top = new YAKTopSectionData();
            for (int i = 0; i < columns; i++) top.Columns.Add(new YAKSpoolColumn());

            // Pattern-first: build the intended click pattern, then assign the k-th
            // spool to the column the pattern names (R26). Replaces round-robin i%cols.
            int[] pattern = Hoppa.YAK.Sim.YakClickPattern.Build(columns, spools.Count, complexity, rng);
            for (int i = 0; i < spools.Count; i++)
                top.Columns[pattern[i]].Spools.Add(spools[i]);
            return top;
        }

        // Split `total` into capacities in [min,max] (jittered around avg) summing
        // EXACTLY to total. If total < min, returns one undersized spool (= total),
        // the documented exception. Never strands a remainder below min.
        public static List<int> Partition(int total, int min, int max, int avg, System.Random rng)
        {
            var caps = new List<int>();
            if (total <= 0) return caps;
            if (max < min) max = min;
            avg = Mathf.Clamp(avg, min, max);

            if (total <= max)
            {
                caps.Add(total); // single spool (may be < min only when total < min — accepted)
                return caps;
            }

            int remaining = total;
            while (remaining > max)
            {
                // jitter ±1 around avg, but keep enough so the remainder can still
                // form a valid spool (>= min).
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

        private static int ResolveSlots(LevelDocument doc, YAKSpoolAutofillConfig cfg)
        {
            var cc = doc.GameData?["conveyorCount"];
            if (cc != null && cc.Type != JTokenType.Null)
            {
                int v = (int)cc;
                if (v >= 1) return v;
            }
            return Mathf.Max(1, cfg.DefaultConveyorSlots);
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
