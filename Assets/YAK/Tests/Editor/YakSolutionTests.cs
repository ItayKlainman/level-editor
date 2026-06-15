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
    // Phase D verification: the exported solution.json round-trips and, replayed
    // through the simulator, deterministically WINS the level (the editor half of
    // the acceptance criterion; the in-game half lives in the game project).
    public sealed class YakSolutionTests
    {
        [Test]
        public void Solution_RoundTripsAndReplaysToWin()
        {
            // Order-sensitive balanced 2x2 (solvable with 2 belt slots).
            var grid = new GridData<ICellData>(2, 2);
            grid.Set(0, 0, new YAKWoolCell { ColorId = "A" });
            grid.Set(0, 1, new YAKWoolCell { ColorId = "B" });
            grid.Set(1, 0, new YAKWoolCell { ColorId = "B" });
            grid.Set(1, 1, new YAKWoolCell { ColorId = "A" });
            var top = new YAKTopSectionData();
            top.Columns.Add(Col("A", 2));
            top.Columns.Add(Col("B", 2));
            var doc = new LevelDocument
            {
                SchemaVersion = "yak.v1", LevelId = "sol_test",
                Grid = grid,
                TopSection = JObject.FromObject(top),
                GameData = new JObject { ["conveyorCount"] = 2 },
            };

            // Analyze → expect a solvable result carrying a structured WinPath.
            var ana = MakeAnalyzer();
            var res = ana.Analyze(doc, null, new AnalysisRequest { RecordSolution = true });
            Assert.AreEqual(AnalysisStatus.Solvable, res.Status, res.FailureReason);
            Assert.IsNotNull(res.WinPath);
            Assert.Greater(res.WinPath.Count, 0);

            // Export → JSON → re-import (the on-disk contract).
            string json = SolutionJson.Serialize(new LevelSolution { levelId = doc.LevelId, steps = ToArray(res.WinPath) });
            var reloaded = SolutionJson.Deserialize(json);
            Assert.AreEqual("sol_test", reloaded.levelId);
            CollectionAssert.AreEqual(ToArray(res.WinPath), reloaded.steps);

            // Replay the reloaded steps through a fresh simulator → must WIN.
            var model = YakLevelModel.Build(grid, top, conveyorSlots: 2);
            var state = new YakSimState(model);
            foreach (int col in reloaded.steps)
            {
                Assert.IsTrue(state.CanSend(col), $"replay step {col} not sendable");
                Assert.GreaterOrEqual(state.FreeSlot(), 0, $"replay step {col} has no free belt slot");
                state.ApplyMove(col);
            }
            Assert.IsTrue(state.IsWin(), "replaying the exported solution must win the level");
        }

        [Test]
        public void SolutionJson_NoWinPath_WritesNothing()
        {
            string path = System.IO.Path.Combine(Application.temporaryCachePath, "none.solution.json");
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            bool wrote = SolutionJson.Write(path, "none", null);
            Assert.IsFalse(wrote);
            Assert.IsFalse(System.IO.File.Exists(path));
        }

        private static YAKSpoolColumn Col(string color, int cap)
        {
            var c = new YAKSpoolColumn();
            c.Spools.Add(new YAKSpoolEntry { ColorId = color, Capacity = cap });
            return c;
        }

        private static int[] ToArray(System.Collections.Generic.IReadOnlyList<int> list)
        {
            var arr = new int[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = list[i];
            return arr;
        }

        private static YAKLevelAnalyzer MakeAnalyzer()
        {
            var cfg = ScriptableObject.CreateInstance<YAKAnalyzerConfig>();
            cfg.Runs = 40;
            var ana = ScriptableObject.CreateInstance<YAKLevelAnalyzer>();
            typeof(YAKLevelAnalyzer).GetField("_config", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(ana, cfg);
            return ana;
        }
    }
}
