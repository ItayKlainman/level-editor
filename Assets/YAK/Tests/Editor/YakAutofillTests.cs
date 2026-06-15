using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YAK;
using Hoppa.YAK.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.YAK.Editor.Tests
{
    // Phase B verification: YAKSpoolAutofiller produces balanced, analyzer-solvable
    // spool layouts, and reports honest best-effort when a target is unreachable.
    public sealed class YakAutofillTests
    {
        // ── Partition (balance-by-construction primitive) ─────────────────────

        [Test]
        public void Partition_SumsExactly_AndStaysInRange()
        {
            var rng = new System.Random(1);
            foreach (int total in new[] { 40, 55, 23, 100, 31 })
            {
                var caps = YAKSpoolAutofiller.Partition(total, 10, 30, 20, rng);
                int sum = 0;
                foreach (var c in caps) { sum += c; Assert.That(c, Is.InRange(10, 30), $"cap {c} out of range for total {total}"); }
                Assert.AreEqual(total, sum, $"partition of {total} must sum exactly");
            }
        }

        [Test]
        public void Partition_BelowMin_IsSingleUndersizedSpool()
        {
            var caps = YAKSpoolAutofiller.Partition(4, 10, 30, 20, new System.Random(1));
            Assert.AreEqual(1, caps.Count);
            Assert.AreEqual(4, caps[0]); // documented exception: total < min → one undersized spool
        }

        // ── Auto-fill end-to-end ──────────────────────────────────────────────

        [Test]
        public void Autofill_SingleColor_IsBalancedAndSolvable()
        {
            var profile = MakeProfile();
            var af = MakeAutofiller(min: 10, max: 30, avg: 20, colMin: 2, colMax: 2, tol: 50f);
            var doc = DocAllOneColor(8, 5, "A", conveyor: 1); // 40 wool A

            var res = af.Complete(doc, profile, new CompletionRequest { Seed = 7 });

            Assert.IsNotNull(res.TopSection);
            Assert.IsNotNull(res.Analysis);
            Assert.IsTrue(res.Analysis.Solvable, "single-color fill must be solvable: " + res.FailureReason);
            AssertBalanced(res.TopSection, new Dictionary<string, int> { { "A", 40 } });
            AssertCapsInRange(res.TopSection, 10, 30);
        }

        [Test]
        public void Autofill_TwoColors_IsBalanced()
        {
            var profile = MakeProfile();
            var af = MakeAutofiller(min: 2, max: 5, avg: 3, colMin: 2, colMax: 4, tol: 50f);
            // 4x5 grid: left 2 columns A (10), right 2 columns B (10).
            var grid = new GridData<ICellData>(4, 5);
            for (int y = 0; y < 5; y++)
                for (int x = 0; x < 4; x++)
                    grid.Set(x, y, new YAKWoolCell { ColorId = x < 2 ? "A" : "B" });
            var doc = Doc(grid, conveyor: 3);

            var res = af.Complete(doc, profile, new CompletionRequest { Seed = 11 });

            Assert.IsNotNull(res.TopSection);
            AssertBalanced(res.TopSection, new Dictionary<string, int> { { "A", 10 }, { "B", 10 } });
        }

        [Test]
        public void Autofill_EmptyGrid_ReturnsEmptyTopSuccessfully()
        {
            var profile = MakeProfile();
            var af = MakeAutofiller(min: 10, max: 30, avg: 20, colMin: 2, colMax: 3, tol: 1f);
            var grid = new GridData<ICellData>(2, 2);
            for (int i = 0; i < grid.Cells.Length; i++) grid.Cells[i] = new YAKEmptyCell();

            var res = af.Complete(Doc(grid, conveyor: 1), profile, new CompletionRequest { Seed = 3 });

            Assert.IsTrue(res.Succeeded);
            var top = res.TopSection.ToObject<YAKTopSectionData>();
            Assert.GreaterOrEqual(top.Columns.Count, 2);
            foreach (var c in top.Columns) Assert.AreEqual(0, c.Spools.Count);
        }

        [Test]
        public void Autofill_NoAnalyzer_FailsCleanly()
        {
            var af = MakeAutofiller(min: 10, max: 30, avg: 20, colMin: 2, colMax: 2, tol: 50f);
            var profile = ScriptableObject.CreateInstance<GameProfile>(); // no analyzer wired
            var res = af.Complete(DocAllOneColor(2, 2, "A", 1), profile, new CompletionRequest());
            Assert.IsFalse(res.Succeeded);
            StringAssert.Contains("analyzer", res.FailureReason);
        }

        // ── Builders ──────────────────────────────────────────────────────────

        private static GameProfile MakeProfile()
        {
            var anaCfg = ScriptableObject.CreateInstance<YAKAnalyzerConfig>();
            anaCfg.Runs = 40;
            var ana = ScriptableObject.CreateInstance<YAKLevelAnalyzer>();
            SetField(ana, "_config", anaCfg);

            var profile = ScriptableObject.CreateInstance<GameProfile>();
            SetField(profile, "_levelAnalyzer", ana);
            return profile;
        }

        private static YAKSpoolAutofiller MakeAutofiller(int min, int max, int avg, int colMin, int colMax, float tol)
        {
            var cfg = ScriptableObject.CreateInstance<YAKSpoolAutofillConfig>();
            cfg.MinCapacity = min; cfg.MaxCapacity = max; cfg.AvgCapacity = avg;
            cfg.ColumnRange = new Vector2Int(colMin, colMax);
            cfg.ApsTolerance = tol;
            cfg.MaxAttempts = 25;
            cfg.SearchRolloutCount = 40;
            var af = ScriptableObject.CreateInstance<YAKSpoolAutofiller>();
            SetField(af, "_config", cfg);
            return af;
        }

        private static GridData<ICellData> GridAllOneColor(int w, int h, string color)
        {
            var grid = new GridData<ICellData>(w, h);
            for (int i = 0; i < grid.Cells.Length; i++) grid.Cells[i] = new YAKWoolCell { ColorId = color };
            return grid;
        }

        private static LevelDocument DocAllOneColor(int w, int h, string color, int conveyor)
            => Doc(GridAllOneColor(w, h, color), conveyor);

        private static LevelDocument Doc(GridData<ICellData> grid, int conveyor) => new LevelDocument
        {
            SchemaVersion = "yak.v1",
            LevelId = "test",
            Grid = grid,
            GameData = new JObject { ["conveyorCount"] = conveyor },
        };

        private static void AssertBalanced(JObject topJson, Dictionary<string, int> expectedWool)
        {
            var top = topJson.ToObject<YAKTopSectionData>();
            var capByColor = new Dictionary<string, int>();
            foreach (var col in top.Columns)
                foreach (var s in col.Spools)
                {
                    capByColor.TryGetValue(s.ColorId, out var n);
                    capByColor[s.ColorId] = n + s.Capacity;
                }
            foreach (var kv in expectedWool)
            {
                capByColor.TryGetValue(kv.Key, out var cap);
                Assert.AreEqual(kv.Value, cap, $"color {kv.Key}: spool capacity must equal wool count");
            }
        }

        private static void AssertCapsInRange(JObject topJson, int min, int max)
        {
            var top = topJson.ToObject<YAKTopSectionData>();
            foreach (var col in top.Columns)
                foreach (var s in col.Spools)
                    Assert.That(s.Capacity, Is.InRange(min, max), $"spool cap {s.Capacity} out of [{min},{max}]");
        }

        private static void SetField(Object obj, string field, Object value)
            => obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);
    }
}
