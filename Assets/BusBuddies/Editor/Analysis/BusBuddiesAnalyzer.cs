using System.Diagnostics;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.BusBuddies.Sim;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Bus Buddies difficulty scorer. Wraps the engine-agnostic 1a simulator stack
    // (BusLevelModel + BusSolver + BusAveragePlayer) and maps it onto the generic
    // ILevelAnalyzer contract. Reports honest, distinct outcomes via
    // LevelAnalysisResult.Status (Solvable / Unsolvable / TimedOut / Faulted /
    // Unknown) and a MEASURED APS (~ 1 / average-player win-rate), flagged
    // uncalibrated until the estimator policy is fitted to real player data.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Analysis/Bus Buddies Analyzer")]
    public sealed class BusBuddiesAnalyzer : LevelAnalyzerAsset
    {
        [SerializeField] private BusBuddiesAnalyzerConfig _config;

        public override LevelAnalysisResult Analyze(LevelDocument doc, GameProfile profile, AnalysisRequest req)
        {
            var sw = Stopwatch.StartNew();
            var result = new LevelAnalysisResult();
            var cfg = _config != null ? _config : ScriptableObject.CreateInstance<BusBuddiesAnalyzerConfig>();

            try
            {
                if (doc == null || doc.Grid == null)
                    return Fault(result, sw, "document or grid is null");

                // Resolve Active Bus Row slots. The level's GameData wins (authored
                // per level), then the request override, then the config default.
                // Reuses YAK's "conveyorCount" key (repurposed) so cloned tooling works.
                int slots;
                var cc = doc.GameData?["conveyorCount"];
                if (cc != null && cc.Type != JTokenType.Null)
                    slots = (int)cc;
                else if (req?.ConveyorCapacityOverride is int ov)
                    slots = ov;
                else
                    slots = cfg.DefaultActiveSlots;
                if (slots < 1) slots = 1;

                // TODO road-block: model reduced usable slots once release semantics are
                // wired. GameData["slotConfigs"] (BusBuddiesSlotConfigs) marks blocked
                // slots that free up only after N buses are clicked; the sim currently
                // treats all `slots` as usable from the start.

                var queue = doc.TopSection?.ToObject<BusQueueData>();

                // Plate covers: map each covered cell to its plate's PixelAmount so the
                // sim keeps it unpickable until that many pixels are picked globally
                // (then the plate opens). 0 = uncovered. Coords are the SAME bottom-left
                // grid space the model indexes with (y*W+x), so no conversion.
                int[] plateReq = null;
                var plates = BusBuddiesPlateConfigs.All(doc);
                if (plates.Count > 0)
                {
                    int gw = doc.Grid.Width;
                    plateReq = new int[gw * doc.Grid.Height];
                    foreach (var p in plates)
                    {
                        int amt = System.Math.Max(1, p.Amount);
                        foreach (var c in BusBuddiesPlateConfigs.CoveredCells(p))
                            if (doc.Grid.InBounds(c.X, c.Y))
                                plateReq[c.Y * gw + c.X] = amt;
                    }
                }

                var model = BusLevelModel.Build(doc.Grid, queue, slots, plateReq);

                // No buses authored yet -> can't answer solvability. Honest Unknown.
                if (model.TotalBuses == 0)
                {
                    result.Status = AnalysisStatus.Unknown;
                    result.Solvable = false;
                    result.FailureReason = "level has no buses to analyze";
                    return Done(result, sw);
                }

                // Cheap structural pre-check: per-color blocks must equal per-color
                // bus capacity, or the level is provably unsolvable.
                if (!model.IsColorBalanced())
                {
                    result.Status = AnalysisStatus.Unsolvable;
                    result.Solvable = false;
                    result.FailureReason = BalanceReason(model);
                    return Done(result, sw);
                }

                // Exact win-path solver — small grids only (no gravity => full 2D
                // state, so it does not scale; large grids fall through to rollout).
                if (model.W * model.H <= cfg.SmallGridThreshold)
                {
                    long nodeBudget = (req != null && req.NodeBudget > 0) ? req.NodeBudget : cfg.NodeBudget;
                    long timeout    = (req != null && req.TimeoutMs  > 0) ? req.TimeoutMs  : cfg.TimeoutMs;
                    var solver = new BusSolver(nodeBudget, timeout);
                    var solve = solver.Solve(model);
                    result.StatesExplored = solve.Nodes;

                    switch (solve.Outcome)
                    {
                        case BusSolver.Outcome.Solvable:
                            result.Status = AnalysisStatus.Solvable;
                            result.Solvable = true;
                            result.WinPathCount = 1; // first-solution search
                            result.WinPath = solve.WinPath;
                            break;
                        case BusSolver.Outcome.Unsolvable:
                            result.Status = AnalysisStatus.Unsolvable;
                            result.Solvable = false;
                            result.FailureReason = "no winning pull order exists";
                            break;
                        default: // BudgetExceeded
                            result.Status = AnalysisStatus.TimedOut;
                            result.Solvable = false;
                            result.CountWasCapped = true;
                            result.FailureReason = $"search budget hit ({solve.Nodes} nodes) — solvability unknown";
                            break;
                    }
                }
                // else: Status stays Unknown; rollout below decides.

                // Measured APS unless the solver already proved Unsolvable.
                if (result.Status != AnalysisStatus.Unsolvable)
                {
                    int runs = (req != null && req.RolloutCount > 0) ? req.RolloutCount : cfg.Runs;
                    int seed = (req != null && req.Seed != 0) ? req.Seed : cfg.RngSeed;
                    var player = new BusAveragePlayer();
                    var est = player.Estimate(model, new BusAveragePlayer.Config
                    {
                        Epsilon = cfg.Epsilon, Lookahead = cfg.Lookahead, Runs = runs, Seed = seed,
                    });
                    result.WinRate = est.WinRate;
                    result.RolloutsRun = est.Runs;
                    result.ApsEstimate = est.Aps; // already capped at BusAveragePlayer.ApsCap
                    result.ApsCalibrated = cfg.ApsCalibrated;
                    result.Band = cfg.BandFor(est.Aps);

                    // Rollout rescue: on large grids the exact solver is skipped
                    // (Status == Unknown) or hits budget (TimedOut). A winning playout
                    // still PROVES solvability, so upgrade to Solvable when any won.
                    // WinPath stays null (no canonical path found) for those cases.
                    if ((result.Status == AnalysisStatus.TimedOut || result.Status == AnalysisStatus.Unknown)
                        && result.WinRate > 0.0)
                    {
                        result.Status = AnalysisStatus.Solvable;
                        result.Solvable = true;
                        result.FailureReason = null;
                    }
                    // Large grid skipped AND rollout found no win — remain Unknown but explain.
                    else if (result.Status == AnalysisStatus.Unknown && result.WinRate == 0.0)
                    {
                        result.FailureReason =
                            $"large grid; rollout found no win in {result.RolloutsRun} runs — solvability unknown";
                    }
                }

                return Done(result, sw);
            }
            catch (System.Exception ex)
            {
                return Fault(result, sw, "analyzer faulted: " + ex.Message);
            }
        }

        // Per-color balance reason. BusLevelModel.IsColorBalanced returns only a
        // bool, so re-derive the offending color here from the model's public
        // readonly arrays (same invariant, surfaced as a clear message).
        private static string BalanceReason(BusLevelModel m)
        {
            var blocks = new int[m.NumColors];
            for (int i = 0; i < m.Grid.Length; i++)
            {
                int c = m.Grid[i];
                if (c >= 0 && c < m.NumColors) blocks[c]++;
            }
            var cap = new int[m.NumColors];
            for (int col = 0; col < m.Columns; col++)
                for (int k = 0; k < m.BusColor[col].Length; k++)
                {
                    int c = m.BusColor[col][k];
                    if (c >= 0 && c < m.NumColors) cap[c] += m.BusCap[col][k];
                }
            for (int k = 0; k < m.NumColors; k++)
                if (blocks[k] != cap[k])
                {
                    string name = (m.ColorNames != null && k < m.ColorNames.Length) ? m.ColorNames[k] : k.ToString();
                    return $"color '{name}' unbalanced: {blocks[k]} blocks vs {cap[k]} bus capacity";
                }
            return "color balance mismatch";
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
