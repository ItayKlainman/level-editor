using System.Reflection;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YAK;
using Hoppa.YAK.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.YAK.Tests
{
    // Shared fixtures for the autofiller complexity tests. Mirrors the profile /
    // autofiller construction in YakAutofillTests (analyzer + completer wired on a
    // GameProfile) so the complexity tests reuse one pipeline rather than inventing
    // a new profile path.
    internal static class YakAutofillTestFixtures
    {
        private static void SetField(Object obj, string field, Object value)
            => obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);

        // Multi-color wool grid sized so a dozen-ish spools result (room for variety).
        // 9x6 = 54 wool across 3 colors (18 each), striped by column for clean balance.
        public static LevelDocument MultiColorGrid(int seed)
        {
            string[] colors = { "A", "B", "C" };
            var grid = new GridData<ICellData>(9, 6);
            for (int x = 0; x < 9; x++)
                for (int y = 0; y < 6; y++)
                    grid.Set(x, y, new YAKWoolCell { ColorId = colors[x / 3] });
            return new LevelDocument
            {
                SchemaVersion = "yak.v1",
                LevelId = "cplx_test_" + seed,
                Grid = grid,
                GameData = new JObject { ["conveyorCount"] = 6 },
            };
        }

        private static (GameProfile profile, YAKSpoolAutofiller af) Pipeline()
        {
            var anaCfg = ScriptableObject.CreateInstance<YAKAnalyzerConfig>();
            anaCfg.Runs = 60;
            var ana = ScriptableObject.CreateInstance<YAKLevelAnalyzer>();
            SetField(ana, "_config", anaCfg);

            var afCfg = ScriptableObject.CreateInstance<YAKSpoolAutofillConfig>();
            afCfg.MinCapacity = 2; afCfg.MaxCapacity = 5; afCfg.AvgCapacity = 3;
            afCfg.ColumnRange = new Vector2Int(3, 4);
            afCfg.ApsTolerance = 50f;          // wide → accept first solvable
            afCfg.ComplexityTolerance = 50f;   // wide → don't gate on complexity here
            afCfg.MaxAttempts = 20;
            afCfg.SearchRolloutCount = 40;
            var af = ScriptableObject.CreateInstance<YAKSpoolAutofiller>();
            SetField(af, "_config", afCfg);

            var profile = ScriptableObject.CreateInstance<GameProfile>();
            SetField(profile, "_levelAnalyzer", ana);
            SetField(profile, "_levelCompleter", af);
            return (profile, af);
        }

        public static LevelCompletionResult Run(LevelDocument grid, int targetComplexity, int seed)
        {
            var (profile, af) = Pipeline();
            return af.Complete(grid, profile,
                new CompletionRequest { TargetComplexity = targetComplexity, Seed = seed });
        }

        // Runs the autofiller then re-analyzes the produced spool section with
        // MeasureComplexity = true, returning the mean measured complexity. Averaged
        // over a few seeds to smooth Monte-Carlo tie-break noise.
        public static float RunAndMeasureComplexity(LevelDocument grid, int targetComplexity, int seed)
        {
            float sum = 0f; int n = 0;
            for (int s = 0; s < 3; s++)
            {
                var (profile, af) = Pipeline();
                var res = af.Complete(grid, profile,
                    new CompletionRequest { TargetComplexity = targetComplexity, Seed = seed + s });
                if (res.TopSection == null) continue;

                var candDoc = new LevelDocument
                {
                    SchemaVersion = grid.SchemaVersion,
                    LevelId = grid.LevelId,
                    Grid = grid.Grid,
                    TopSection = res.TopSection,
                    GameData = grid.GameData,
                };
                var an = profile.LevelAnalyzer.Analyze(candDoc, profile,
                    new AnalysisRequest { RolloutCount = 120, Seed = seed + s, MeasureComplexity = true });
                if (an.ComplexityEstimate > 0f) { sum += an.ComplexityEstimate; n++; }
            }
            return n > 0 ? sum / n : 0f;
        }
    }
}
