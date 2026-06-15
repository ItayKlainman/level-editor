using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YAK;
using Hoppa.YAK.Sim;
using Hoppa.YAK.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.YAK.Editor.Tests
{
    // Phase A verification: the engine-agnostic simulator stack (YakLevelModel /
    // YakSolver / YakAveragePlayer) and the YAKLevelAnalyzer that wraps them.
    public sealed class YakAnalyzerTests
    {
        // ── Fixtures ──────────────────────────────────────────────────────────

        // Single grid column [A,A,A]; one spool {A, cap3}. Trivially solvable.
        private static (GridData<ICellData> grid, YAKTopSectionData top) Trivial()
        {
            var grid = new GridData<ICellData>(1, 3);
            for (int y = 0; y < 3; y++) grid.Set(0, y, new YAKWoolCell { ColorId = "A" });
            var top = Top(Col(("A", 3)));
            return (grid, top);
        }

        // Order-sensitive, balanced 2x2:
        //   col0 (bottom→top): A,B   col1: B,A   spools: {A,2} | {B,2}
        // Unsolvable with 1 belt slot (forced deadlock), solvable with 2.
        private static (GridData<ICellData> grid, YAKTopSectionData top) OrderSensitive()
        {
            var grid = new GridData<ICellData>(2, 2);
            grid.Set(0, 0, new YAKWoolCell { ColorId = "A" });
            grid.Set(0, 1, new YAKWoolCell { ColorId = "B" });
            grid.Set(1, 0, new YAKWoolCell { ColorId = "B" });
            grid.Set(1, 1, new YAKWoolCell { ColorId = "A" });
            var top = Top(Col(("A", 2)), Col(("B", 2)));
            return (grid, top);
        }

        private static YAKSpoolColumn Col(params (string color, int cap)[] spools)
        {
            var c = new YAKSpoolColumn();
            foreach (var (color, cap) in spools)
                c.Spools.Add(new YAKSpoolEntry { ColorId = color, Capacity = cap, Hidden = false });
            return c;
        }

        private static YAKTopSectionData Top(params YAKSpoolColumn[] cols)
        {
            var t = new YAKTopSectionData();
            t.Columns.AddRange(cols);
            return t;
        }

        private static YAKLevelAnalyzer MakeAnalyzer(out YAKAnalyzerConfig cfg, int runs = 120)
        {
            cfg = ScriptableObject.CreateInstance<YAKAnalyzerConfig>();
            cfg.Runs = runs;
            var ana = ScriptableObject.CreateInstance<YAKLevelAnalyzer>();
            typeof(YAKLevelAnalyzer)
                .GetField("_config", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(ana, cfg);
            return ana;
        }

        private static LevelDocument Doc(GridData<ICellData> grid, YAKTopSectionData top, int conveyor)
            => new LevelDocument
            {
                SchemaVersion = "yak.v1",
                LevelId = "test",
                Grid = grid,
                TopSection = top != null ? JObject.FromObject(top) : null,
                GameData = new JObject { ["conveyorCount"] = conveyor },
            };

        // ── Simulator + solver ────────────────────────────────────────────────

        [Test]
        public void Solver_TrivialLevel_IsSolvable_WithSingleTap()
        {
            var (grid, top) = Trivial();
            var model = YakLevelModel.Build(grid, top, conveyorSlots: 1);
            var res = new YakSolver(0, 0).Solve(model);

            Assert.AreEqual(YakSolver.Outcome.Solvable, res.Outcome);
            Assert.AreEqual(1, res.WinPath.Length);
            Assert.AreEqual(0, res.WinPath[0]);
        }

        [Test]
        public void Solver_OrderSensitive_OneSlotDeadlocks_TwoSlotsSolves()
        {
            var (grid, top) = OrderSensitive();

            var oneSlot = new YakSolver(0, 0).Solve(YakLevelModel.Build(grid, top, 1));
            Assert.AreEqual(YakSolver.Outcome.Unsolvable, oneSlot.Outcome,
                "1 belt slot should force a deadlock on this level");

            var twoSlot = new YakSolver(0, 0).Solve(YakLevelModel.Build(grid, top, 2));
            Assert.AreEqual(YakSolver.Outcome.Solvable, twoSlot.Outcome,
                "2 belt slots should make this level solvable");
        }

        [Test]
        public void Solver_BudgetHit_ReportsBudgetExceeded_NotUnsolvable()
        {
            var (grid, top) = OrderSensitive();
            var model = YakLevelModel.Build(grid, top, 2); // genuinely solvable, needs depth >= 2
            var res = new YakSolver(maxNodes: 1, timeoutMs: 60_000).Solve(model);

            Assert.AreEqual(YakSolver.Outcome.BudgetExceeded, res.Outcome,
                "a node-budget cut must never masquerade as Unsolvable");
        }

        // ── Average player (APS) ──────────────────────────────────────────────

        [Test]
        public void AveragePlayer_Trivial_AlwaysWins_ApsIsOne()
        {
            var (grid, top) = Trivial();
            var model = YakLevelModel.Build(grid, top, 1);
            var est = new YakAveragePlayer().Estimate(model, YakAveragePlayer.Config.Default);

            Assert.AreEqual(1f, est.WinRate, 1e-6f);
            Assert.AreEqual(1f, est.Aps, 1e-6f);
        }

        [Test]
        public void AveragePlayer_Deterministic_SameSeedSameWinRate()
        {
            var (grid, top) = OrderSensitive();
            var model = YakLevelModel.Build(grid, top, 2);
            var cfg = new YakAveragePlayer.Config { Epsilon = 0.2f, Lookahead = 1, Runs = 200, Seed = 777 };

            var a = new YakAveragePlayer().Estimate(model, cfg);
            var b = new YakAveragePlayer().Estimate(model, cfg);

            Assert.AreEqual(a.WinRate, b.WinRate, 1e-9f);
        }

        [Test]
        public void AveragePlayer_AddingSlack_LowersAps()
        {
            var (grid, top) = OrderSensitive();
            var player = new YakAveragePlayer();
            var cfg = new YakAveragePlayer.Config { Epsilon = 0.1f, Lookahead = 1, Runs = 200, Seed = 42 };

            var tight = player.Estimate(YakLevelModel.Build(grid, top, 1), cfg); // unsolvable → winrate 0
            var loose = player.Estimate(YakLevelModel.Build(grid, top, 2), cfg); // solvable

            Assert.Greater(loose.WinRate, tight.WinRate);
            Assert.Less(loose.Aps, tight.Aps, "more belt slack must not raise APS");
        }

        // ── Analyzer end-to-end ───────────────────────────────────────────────

        [Test]
        public void Analyzer_Trivial_SolvableWithMeasuredUncalibratedAps()
        {
            var ana = MakeAnalyzer(out _);
            var (grid, top) = Trivial();
            var res = ana.Analyze(Doc(grid, top, 1), null,
                new AnalysisRequest { RecordSolution = true, RolloutCount = 120 });

            Assert.AreEqual(AnalysisStatus.Solvable, res.Status);
            Assert.IsTrue(res.Solvable);
            Assert.Greater(res.ApsEstimate, 0f);
            Assert.IsFalse(res.ApsCalibrated, "APS must be flagged uncalibrated until fitted to real data");
            Assert.IsNotNull(res.WinPath);
            Assert.IsNotNull(res.SolutionSteps);
            Assert.AreEqual(1, res.SolutionSteps.Count);
        }

        [Test]
        public void Analyzer_BalanceMismatch_IsUnsolvableWithReason()
        {
            var ana = MakeAnalyzer(out _);
            var grid = new GridData<ICellData>(1, 3);
            for (int y = 0; y < 3; y++) grid.Set(0, y, new YAKWoolCell { ColorId = "A" }); // 3 wool A
            var top = Top(Col(("A", 2)));                                                   // only 2 capacity

            var res = ana.Analyze(Doc(grid, top, 1), null, new AnalysisRequest());

            Assert.AreEqual(AnalysisStatus.Unsolvable, res.Status);
            StringAssert.Contains("unbalanced", res.FailureReason);
        }

        [Test]
        public void Analyzer_NoSpools_IsUnknown()
        {
            var ana = MakeAnalyzer(out _);
            var grid = new GridData<ICellData>(1, 1);
            grid.Set(0, 0, new YAKWoolCell { ColorId = "A" });

            var res = ana.Analyze(Doc(grid, new YAKTopSectionData(), 1), null, new AnalysisRequest());

            Assert.AreEqual(AnalysisStatus.Unknown, res.Status);
            Assert.IsFalse(res.Solvable);
        }

        [Test]
        public void Analyzer_NullGrid_FaultsCleanly()
        {
            var ana = MakeAnalyzer(out _);
            var res = ana.Analyze(new LevelDocument { Grid = null }, null, new AnalysisRequest());
            Assert.AreEqual(AnalysisStatus.Faulted, res.Status);
        }

        // ── Smoke: the existing hand-made TestConfigs load + analyze (no crash) ──

        [Test]
        public void Analyzer_HandMadeTestConfigs_RunWithoutFaulting()
        {
            var profile = LoadYakProfile();
            if (profile == null) { Assert.Ignore("YAKProfile.asset not found"); return; }
            var registry = profile.BuildRegistry();
            var serializer = new JsonLevelSerializer();
            var ana = MakeAnalyzer(out _, runs: 60);

            string dir = Path.Combine(Application.dataPath, "YAK", "TestConfigs");
            foreach (var path in new[] { "level_001.json", "level_999_All_colors.json" })
            {
                string full = Path.Combine(dir, path);
                if (!File.Exists(full)) continue;
                var doc = serializer.Load(File.ReadAllText(full), registry);
                var res = ana.Analyze(doc, profile, new AnalysisRequest { RolloutCount = 60 });
                Assert.AreNotEqual(AnalysisStatus.Faulted, res.Status,
                    $"{path} analysis faulted: {res.FailureReason}");
            }
        }

        private static GameProfile LoadYakProfile()
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameProfile>(
                "Assets/YAK/Data/Config/YAKProfile.asset");
#else
            return null;
#endif
        }
    }
}
