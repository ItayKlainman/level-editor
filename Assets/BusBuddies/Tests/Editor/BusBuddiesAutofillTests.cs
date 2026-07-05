using System.Collections.Generic;
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
    // Sub-phase 1b-ii verification: BusBuddiesAutofiller produces balanced,
    // analyzer-solvable bus queues, and reports honest best-effort / clean
    // failures. Mirrors YakAutofillTests (reflection-inject style).
    public sealed class BusBuddiesAutofillTests
    {
        // ── Partition (balance-by-construction primitive) ─────────────────────

        [Test]
        public void Partition_SumsExactly_AndStaysInRange()
        {
            var rng = new System.Random(1);
            foreach (int total in new[] { 12, 18, 7, 30, 25 })
            {
                var caps = BusBuddiesAutofiller.Partition(total, 3, 12, 6, rng);
                int sum = 0;
                foreach (var c in caps) { sum += c; Assert.That(c, Is.InRange(3, 12), $"cap {c} out of range for total {total}"); }
                Assert.AreEqual(total, sum, $"partition of {total} must sum exactly");
            }
        }

        [Test]
        public void Partition_BelowMin_IsSingleUndersizedBus()
        {
            var caps = BusBuddiesAutofiller.Partition(2, 3, 12, 6, new System.Random(1));
            Assert.AreEqual(1, caps.Count);
            Assert.AreEqual(2, caps[0]); // documented exception: total < min → one undersized bus
        }

        // ── Auto-fill end-to-end ──────────────────────────────────────────────

        [Test]
        public void Autofill_SingleColor_IsBalancedAndSolvable()
        {
            var profile = MakeProfile();
            var af = MakeAutofiller(min: 3, max: 12, avg: 6, colMin: 1, colMax: 2, tol: 50f);
            var doc = DocAllOneColor(3, 2, "A", conveyor: 1); // 6 blocks of A

            var res = af.Complete(doc, profile, new CompletionRequest { Seed = 7 });

            Assert.IsNotNull(res.TopSection);
            Assert.IsNotNull(res.Analysis);
            Assert.IsTrue(res.Analysis.Solvable, "single-color fill must be solvable: " + res.FailureReason);
            AssertBalanced(res.TopSection, new Dictionary<string, int> { { "A", 6 } });
            AssertCapsInRange(res.TopSection, 3, 12);
        }

        [Test]
        public void Autofill_TwoColors_IsBalanced()
        {
            var profile = MakeProfile();
            var af = MakeAutofiller(min: 3, max: 12, avg: 6, colMin: 1, colMax: 3, tol: 50f);
            // 4x2 grid: left 2 columns A (4), right 2 columns B (4).
            var grid = new GridData<ICellData>(4, 2);
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 4; x++)
                    grid.Set(x, y, new BBPixelCell { ColorId = x < 2 ? "A" : "B" });
            var doc = Doc(grid, conveyor: 2);

            var res = af.Complete(doc, profile, new CompletionRequest { Seed = 11 });

            Assert.IsNotNull(res.TopSection);
            AssertBalanced(res.TopSection, new Dictionary<string, int> { { "A", 4 }, { "B", 4 } });
        }

        [Test]
        public void Autofill_EmptyGrid_ReturnsEmptyTopSuccessfully()
        {
            var profile = MakeProfile();
            var af = MakeAutofiller(min: 3, max: 12, avg: 6, colMin: 1, colMax: 3, tol: 1f);
            var grid = new GridData<ICellData>(2, 2);
            for (int i = 0; i < grid.Cells.Length; i++) grid.Cells[i] = new BBEmptyCell();

            var res = af.Complete(Doc(grid, conveyor: 1), profile, new CompletionRequest { Seed = 3 });

            Assert.IsTrue(res.Succeeded);
            var top = res.TopSection.ToObject<BusQueueData>();
            Assert.GreaterOrEqual(top.Columns.Count, 1);
            int totalBuses = 0;
            foreach (var c in top.Columns) totalBuses += c.Buses.Count;
            Assert.AreEqual(0, totalBuses, "empty grid → zero buses");
        }

        [Test]
        public void Autofill_NoAnalyzer_FailsCleanly()
        {
            var af = MakeAutofiller(min: 3, max: 12, avg: 6, colMin: 1, colMax: 2, tol: 50f);
            var profile = ScriptableObject.CreateInstance<GameProfile>(); // no analyzer wired
            var res = af.Complete(DocAllOneColor(2, 1, "A", 1), profile, new CompletionRequest());
            Assert.IsFalse(res.Succeeded);
            StringAssert.Contains("analyzer", res.FailureReason);
        }

        [Test]
        public void Autofill_Deterministic_ForFixedSeed()
        {
            var af = MakeAutofiller(min: 3, max: 12, avg: 6, colMin: 1, colMax: 3, tol: 50f);
            var a = af.Complete(DocAllOneColor(3, 2, "A", 1), MakeProfile(), new CompletionRequest { Seed = 42 });
            var b = af.Complete(DocAllOneColor(3, 2, "A", 1), MakeProfile(), new CompletionRequest { Seed = 42 });
            Assert.IsNotNull(a.TopSection);
            Assert.IsNotNull(b.TopSection);
            Assert.AreEqual(a.TopSection.ToString(Newtonsoft.Json.Formatting.None),
                            b.TopSection.ToString(Newtonsoft.Json.Formatting.None),
                            "same seed → identical top section");
        }

        // ── Builders ──────────────────────────────────────────────────────────

        private static GameProfile MakeProfile()
        {
            var anaCfg = ScriptableObject.CreateInstance<BusBuddiesAnalyzerConfig>();
            anaCfg.Runs = 40;
            var ana = ScriptableObject.CreateInstance<BusBuddiesAnalyzer>();
            SetField(ana, "_config", anaCfg);

            var profile = ScriptableObject.CreateInstance<GameProfile>();
            SetField(profile, "_levelAnalyzer", ana);
            return profile;
        }

        private static BusBuddiesAutofiller MakeAutofiller(int min, int max, int avg, int colMin, int colMax, float tol)
        {
            var cfg = ScriptableObject.CreateInstance<BusBuddiesAutofillConfig>();
            cfg.MinCapacity = min; cfg.MaxCapacity = max; cfg.AvgCapacity = avg;
            cfg.ColumnRange = new Vector2Int(colMin, colMax);
            cfg.ApsTolerance = tol;
            cfg.MaxAttempts = 25;
            cfg.SearchRolloutCount = 40;
            var af = ScriptableObject.CreateInstance<BusBuddiesAutofiller>();
            SetField(af, "_config", cfg);
            return af;
        }

        private static GridData<ICellData> GridAllOneColor(int w, int h, string color)
        {
            var grid = new GridData<ICellData>(w, h);
            for (int i = 0; i < grid.Cells.Length; i++) grid.Cells[i] = new BBPixelCell { ColorId = color };
            return grid;
        }

        private static LevelDocument DocAllOneColor(int w, int h, string color, int conveyor)
            => Doc(GridAllOneColor(w, h, color), conveyor);

        private static LevelDocument Doc(GridData<ICellData> grid, int conveyor) => new LevelDocument
        {
            SchemaVersion = "busbuddies",
            LevelId = "test",
            Grid = grid,
            GameData = new JObject { ["conveyorCount"] = conveyor },
        };

        private static void AssertBalanced(JObject topJson, Dictionary<string, int> expectedBlocks)
        {
            var top = topJson.ToObject<BusQueueData>();
            var capByColor = new Dictionary<string, int>();
            foreach (var col in top.Columns)
                foreach (var b in col.Buses)
                {
                    capByColor.TryGetValue(b.ColorId, out var n);
                    capByColor[b.ColorId] = n + b.Capacity;
                }
            foreach (var kv in expectedBlocks)
            {
                capByColor.TryGetValue(kv.Key, out var cap);
                Assert.AreEqual(kv.Value, cap, $"color {kv.Key}: bus capacity must equal block count");
            }
        }

        private static void AssertCapsInRange(JObject topJson, int min, int max)
        {
            var top = topJson.ToObject<BusQueueData>();
            foreach (var col in top.Columns)
                foreach (var b in col.Buses)
                    Assert.That(b.Capacity, Is.InRange(min, max), $"bus cap {b.Capacity} out of [{min},{max}]");
        }

        private static void SetField(Object obj, string field, Object value)
            => obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);
    }
}
