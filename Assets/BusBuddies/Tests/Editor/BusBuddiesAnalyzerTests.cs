using System.Reflection;
using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.BusBuddies;
using Hoppa.BusBuddies.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusBuddiesAnalyzerTests
    {
        private static void SetField(Object obj, string field, Object value)
            => obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);

        private static BusBuddiesAnalyzer MakeAnalyzer(int runs = 60)
        {
            var cfg = ScriptableObject.CreateInstance<BusBuddiesAnalyzerConfig>();
            cfg.Runs = runs;
            var ana = ScriptableObject.CreateInstance<BusBuddiesAnalyzer>();
            SetField(ana, "_config", cfg);
            return ana;
        }

        private static LevelDocument Doc(GridData<ICellData> grid, BusQueueData queue, int slots) => new LevelDocument
        {
            SchemaVersion = "busbuddies",
            LevelId = "test",
            Grid = grid,
            TopSection = queue != null ? JObject.FromObject(queue) : null,
            GameData = new JObject { ["conveyorCount"] = slots },
        };

        private static (GridData<ICellData> grid, BusQueueData q) TwoCellAB()
        {
            var grid = new GridData<ICellData>(2, 1);
            grid.Set(0, 0, new BBPixelCell { ColorId = "A" });
            grid.Set(1, 0, new BBPixelCell { ColorId = "B" });
            var q = new BusQueueData();
            var c0 = new BusColumn(); c0.Buses.Add(new BusEntry { ColorId = "A", Capacity = 1 });
            var c1 = new BusColumn(); c1.Buses.Add(new BusEntry { ColorId = "B", Capacity = 1 });
            q.Columns.Add(c0); q.Columns.Add(c1);
            return (grid, q);
        }

        [Test]
        public void BalancedSmallGrid_IsSolvable_WithApsAndBand()
        {
            var (grid, q) = TwoCellAB();
            var r = MakeAnalyzer().Analyze(Doc(grid, q, 2), null, new AnalysisRequest { Seed = 99 });

            Assert.AreEqual(AnalysisStatus.Solvable, r.Status);
            Assert.IsTrue(r.Solvable);
            Assert.IsNotNull(r.WinPath);              // small grid -> exact solver path
            Assert.Greater(r.ApsEstimate, 0f);
            Assert.GreaterOrEqual(r.Band, 1);
            Assert.Greater(r.RolloutsRun, 0);
        }

        [Test]
        public void UnbalancedGrid_IsProvenUnsolvable()
        {
            // 3 A blocks, bus A capacity 2.
            var grid = new GridData<ICellData>(3, 1);
            grid.Set(0, 0, new BBPixelCell { ColorId = "A" });
            grid.Set(1, 0, new BBPixelCell { ColorId = "A" });
            grid.Set(2, 0, new BBPixelCell { ColorId = "A" });
            var q = new BusQueueData();
            var c0 = new BusColumn(); c0.Buses.Add(new BusEntry { ColorId = "A", Capacity = 2 });
            q.Columns.Add(c0);

            var r = MakeAnalyzer().Analyze(Doc(grid, q, 1), null, new AnalysisRequest());
            Assert.AreEqual(AnalysisStatus.Unsolvable, r.Status);
            Assert.IsFalse(r.Solvable);
            Assert.IsNotNull(r.FailureReason);
        }

        [Test]
        public void NoBuses_IsUnknown()
        {
            var grid = new GridData<ICellData>(1, 1);
            grid.Set(0, 0, new BBPixelCell { ColorId = "A" });
            var r = MakeAnalyzer().Analyze(Doc(grid, new BusQueueData(), 1), null, new AnalysisRequest());
            Assert.AreEqual(AnalysisStatus.Unknown, r.Status);
            Assert.IsFalse(r.Solvable);
        }

        [Test]
        public void Aps_IsDeterministic_ForFixedSeed()
        {
            var (g1, q1) = TwoCellAB();
            var (g2, q2) = TwoCellAB();
            var a = MakeAnalyzer().Analyze(Doc(g1, q1, 2), null, new AnalysisRequest { Seed = 7 });
            var b = MakeAnalyzer().Analyze(Doc(g2, q2, 2), null, new AnalysisRequest { Seed = 7 });
            Assert.AreEqual(a.ApsEstimate, b.ApsEstimate);
            Assert.AreEqual(a.WinRate, b.WinRate);
        }
    }
}
