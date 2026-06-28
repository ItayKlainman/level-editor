using System.Collections.Generic;
using System.Diagnostics;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YAK.Sim;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.YAK.Editor
{
    // YAK difficulty scorer. Wraps the engine-agnostic simulator stack
    // (YakLevelModel + YakSolver + YakAveragePlayer) and maps it onto the
    // generic ILevelAnalyzer contract.
    //
    // Reports honest, distinct outcomes via LevelAnalysisResult.Status:
    //   Solvable / Unsolvable / TimedOut / Faulted / Unknown
    // and a MEASURED APS (≈ 1 / average-player win-rate), flagged uncalibrated
    // until the estimator policy is fitted to real player data.
    [CreateAssetMenu(menuName = "Hoppa/YAK/Analysis/YAK Level Analyzer")]
    public sealed class YAKLevelAnalyzer : LevelAnalyzerAsset
    {
        [SerializeField] private YAKAnalyzerConfig _config;

        public override LevelAnalysisResult Analyze(LevelDocument doc, GameProfile profile, AnalysisRequest req)
        {
            var sw = Stopwatch.StartNew();
            var result = new LevelAnalysisResult();
            var cfg = _config != null ? _config : ScriptableObject.CreateInstance<YAKAnalyzerConfig>();

            try
            {
                if (doc == null || doc.Grid == null)
                {
                    return Fault(result, sw, "document or grid is null");
                }

                // Resolve conveyor slots. YAK's belt slot count is an authored
                // per-level property, so the level's GameData wins; the request
                // override is a fallback (the shared AutofillPanel's 24/30 belt
                // dropdown is YarnTwist-flavoured and must NOT mis-drive YAK).
                int slots;
                var cc = doc.GameData?["conveyorCount"];
                if (cc != null && cc.Type != JTokenType.Null)
                    slots = (int)cc;
                else if (req?.ConveyorCapacityOverride is int ov)
                    slots = ov;
                else
                    slots = cfg.DefaultConveyorSlots;
                if (slots < 1) slots = 1;

                var top = doc.TopSection?.ToObject<YAKTopSectionData>();
                var model = YakLevelModel.Build(doc.Grid, top, slots);

                // No spools authored yet → can't answer the solvability question.
                // Honest "unknown / incomplete", not a false "unsolvable".
                if (model.TotalSpools == 0)
                {
                    result.Status = AnalysisStatus.Unknown;
                    result.Solvable = false;
                    result.FailureReason = "level has no spools to analyze";
                    return Done(result, sw);
                }

                // Cheap structural pre-check: per-color wool must equal per-color
                // capacity, or the level is provably unsolvable (a spool that can
                // never fill). This is the same invariant YAKColorBalanceRule
                // enforces, surfaced here as a clear reason before searching.
                if (!BalanceOk(model, out string balReason))
                {
                    result.Status = AnalysisStatus.Unsolvable;
                    result.Solvable = false;
                    result.FailureReason = balReason;
                    return Done(result, sw);
                }

                // ── Win-path solver ──
                long nodeBudget = (req != null && req.NodeBudget > 0) ? req.NodeBudget : cfg.NodeBudget;
                long timeout    = (req != null && req.TimeoutMs  > 0) ? req.TimeoutMs  : cfg.TimeoutMs;
                var solver = new YakSolver(nodeBudget, timeout);
                var solve = solver.Solve(model);
                result.StatesExplored = solve.Nodes;

                switch (solve.Outcome)
                {
                    case YakSolver.Outcome.Solvable:
                        result.Status = AnalysisStatus.Solvable;
                        result.Solvable = true;
                        result.WinPathCount = 1; // first-solution search; not an exhaustive count
                        result.WinPath = solve.WinPath;
                        if (req != null && req.RecordSolution)
                            result.SolutionSteps = FormatSolution(model, solve.WinPath);
                        break;
                    case YakSolver.Outcome.Unsolvable:
                        result.Status = AnalysisStatus.Unsolvable;
                        result.Solvable = false;
                        result.FailureReason = "no winning send order exists";
                        break;
                    default: // BudgetExceeded
                        result.Status = AnalysisStatus.TimedOut;
                        result.Solvable = false;
                        result.CountWasCapped = true;
                        result.FailureReason = $"search budget hit ({solve.Nodes} nodes) — solvability unknown";
                        break;
                }

                // ── Measured APS (only when a win is at least possible) ──
                if (result.Status == AnalysisStatus.Solvable || result.Status == AnalysisStatus.TimedOut)
                {
                    int runs = (req != null && req.RolloutCount > 0) ? req.RolloutCount : cfg.Runs;
                    int seed = (req != null && req.Seed != 0) ? req.Seed : cfg.RngSeed;
                    var player = new YakAveragePlayer();
                    var est = player.Estimate(model, new YakAveragePlayer.Config
                    {
                        Epsilon = cfg.Epsilon, Lookahead = cfg.Lookahead, Runs = runs, Seed = seed,
                        MeasureComplexity = req != null && req.MeasureComplexity,
                    });
                    result.WinRate = est.WinRate;
                    result.RolloutsRun = est.Runs;
                    float aps = float.IsPositiveInfinity(est.Aps) ? YakAveragePlayer.ApsCap : est.Aps;
                    result.ApsEstimate = aps;
                    result.ApsCalibrated = cfg.ApsCalibrated;
                    result.Band = cfg.BandFor(aps);
                    result.ComplexityEstimate = est.ComplexityEstimate;

                    // Rollout rescue: on large grids (e.g. 30×30) the exact solver
                    // hits its node budget and returns TimedOut — exhaustive search
                    // doesn't scale. But a winning playout still PROVES the level is
                    // solvable, so upgrade TimedOut → Solvable when any playout won.
                    // WinPath stays null (no canonical path was found within budget);
                    // Save-Solution therefore works best on small/medium levels.
                    if (result.Status == AnalysisStatus.TimedOut && result.WinRate > 0.0)
                    {
                        result.Status = AnalysisStatus.Solvable;
                        result.Solvable = true;
                        result.FailureReason = null;
                    }
                }

                return Done(result, sw);
            }
            catch (System.Exception ex)
            {
                return Fault(result, sw, "analyzer faulted: " + ex.Message);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static bool BalanceOk(YakLevelModel m, out string reason)
        {
            var wool = new int[m.NumColors];
            var cap  = new int[m.NumColors];
            for (int c = 0; c < m.Width; c++)
                foreach (var col in m.GridCols[c]) wool[col]++;
            for (int c = 0; c < m.Columns; c++)
                for (int s = 0; s < m.SpoolColor[c].Length; s++)
                    cap[m.SpoolColor[c][s]] += m.SpoolCap[c][s];

            for (int k = 0; k < m.NumColors; k++)
                if (wool[k] != cap[k])
                {
                    reason = $"color '{m.ColorNames[k]}' unbalanced: {wool[k]} wool vs {cap[k]} spool capacity";
                    return false;
                }
            reason = null;
            return true;
        }

        private static List<string> FormatSolution(YakLevelModel m, int[] winPath)
        {
            if (winPath == null) return null;
            // Replay to annotate each tap with the spool color it sent.
            var qHead = new int[m.Columns];
            var steps = new List<string>(winPath.Length);
            for (int i = 0; i < winPath.Length; i++)
            {
                int col = winPath[i];
                string color = "?";
                if (col >= 0 && col < m.Columns && qHead[col] < m.SpoolColor[col].Length)
                {
                    color = m.ColorNames[m.SpoolColor[col][qHead[col]]];
                    qHead[col]++;
                }
                steps.Add($"{i + 1}. Tap column {col + 1} → send {color} spool");
            }
            return steps;
        }

        private static LevelAnalysisResult Fault(LevelAnalysisResult r, Stopwatch sw, string reason)
        {
            r.Status = AnalysisStatus.Faulted;
            r.Solvable = false;
            r.FailureReason = reason;
            return Done(r, sw);
        }

        private static LevelAnalysisResult Done(LevelAnalysisResult r, Stopwatch sw)
        {
            sw.Stop();
            r.ElapsedMs = sw.ElapsedMilliseconds;
            return r;
        }
    }
}
