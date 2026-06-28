using System.Reflection;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YAK;
using Hoppa.YAK.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.YAK.Tests
{
    // Shared fixtures for the analyzer/complexity tests. Mirrors the inline setup
    // in YakAnalyzerTests (Trivial grid + Doc + MakeAnalyzer) so new tests reuse a
    // single solvable level + analyzer rather than duplicating the builders.
    internal static class YakAnalyzerTestFixtures
    {
        // Single grid column [A,A,A]; one spool {A, cap3}. Trivially solvable.
        // profile is null (the analyzer falls back to its own config).
        public static (LevelDocument doc, GameProfile profile) SolvableSmallLevel()
        {
            var grid = new GridData<ICellData>(1, 3);
            for (int y = 0; y < 3; y++) grid.Set(0, y, new YAKWoolCell { ColorId = "A" });

            var col = new YAKSpoolColumn();
            col.Spools.Add(new YAKSpoolEntry { ColorId = "A", Capacity = 3, Hidden = false });
            var top = new YAKTopSectionData();
            top.Columns.Add(col);

            var doc = new LevelDocument
            {
                SchemaVersion = "yak.v1",
                LevelId = "test",
                Grid = grid,
                TopSection = JObject.FromObject(top),
                GameData = new JObject { ["conveyorCount"] = 1 },
            };
            return (doc, null);
        }

        public static YAKLevelAnalyzer Analyzer(int runs = 120)
        {
            var cfg = ScriptableObject.CreateInstance<YAKAnalyzerConfig>();
            cfg.Runs = runs;
            var ana = ScriptableObject.CreateInstance<YAKLevelAnalyzer>();
            typeof(YAKLevelAnalyzer)
                .GetField("_config", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(ana, cfg);
            return ana;
        }
    }
}
