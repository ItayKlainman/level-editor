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
    // Task 7: the reworked autofiller drives the fill from the designer knobs
    // (read from GameData), keeps per-color sums exact, is always Solvable when
    // possible, is deterministic, and reports APS as a read-out (not a gate).
    public sealed class BusBuddiesDifficultyModelTests
    {
        private static void SetField(Object obj, string field, Object value)
            => obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);

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

        private static BusBuddiesAutofiller MakeAutofiller()
        {
            var cfg = ScriptableObject.CreateInstance<BusBuddiesAutofillConfig>(); // ChunksBase 10, ChunksStep 5
            cfg.SearchRolloutCount = 40;
            var af = ScriptableObject.CreateInstance<BusBuddiesAutofiller>();
            SetField(af, "_config", cfg);
            return af;
        }

        private static LevelDocument SingleColorDoc(int w, int h, string color, int conveyor, BusBuddiesDifficultySettings s)
        {
            var grid = new GridData<ICellData>(w, h);
            for (int i = 0; i < grid.Cells.Length; i++) grid.Cells[i] = new BBPixelCell { ColorId = color };
            var doc = new LevelDocument
            {
                SchemaVersion = "busbuddies", LevelId = "t", Grid = grid,
                GameData = new JObject { ["conveyorCount"] = conveyor },
            };
            s?.WriteTo(doc);
            return doc;
        }

        private static int BusCount(JObject top)
        {
            var q = top.ToObject<BusQueueData>();
            int n = 0; foreach (var c in q.Columns) n += c.Buses.Count; return n;
        }

        private static int CapSum(JObject top, string color)
        {
            var q = top.ToObject<BusQueueData>();
            int s = 0;
            foreach (var c in q.Columns) foreach (var b in c.Buses) if (b.ColorId == color) s += b.Capacity;
            return s;
        }

        [Test]
        public void FewerChunks_ProduceMoreBuses()
        {
            var profile = MakeProfile();
            var af = MakeAutofiller();

            var lo = af.Complete(SingleColorDoc(6, 6, "A", 3,
                new BusBuddiesDifficultySettings { BusesChunks = 1, DeviationPercent = 0.5f, Columns = 2, Difficulty = 1 }),
                profile, new CompletionRequest { Seed = 7 });
            var hi = af.Complete(SingleColorDoc(6, 6, "A", 3,
                new BusBuddiesDifficultySettings { BusesChunks = 5, DeviationPercent = 0.5f, Columns = 2, Difficulty = 1 }),
                profile, new CompletionRequest { Seed = 7 });

            Assert.Greater(BusCount(lo.TopSection), BusCount(hi.TopSection),
                "Buses Chunks 1 (avg 10) yields more buses than Chunks 5 (avg 30)");
        }

        [Test]
        public void Fill_IsBalanced_AndSolvable_WithApsReadout()
        {
            var profile = MakeProfile();
            var af = MakeAutofiller();
            var doc = SingleColorDoc(6, 6, "A", 3,
                new BusBuddiesDifficultySettings { BusesChunks = 2, DeviationPercent = 0.5f, Columns = 2, Difficulty = 3 });

            var res = af.Complete(doc, profile, new CompletionRequest { Seed = 11 });

            Assert.IsNotNull(res.TopSection);
            Assert.AreEqual(36, CapSum(res.TopSection, "A"), "per-color bus capacity must equal block count");
            Assert.IsNotNull(res.Analysis, "analysis attached as a read-out");
            Assert.IsTrue(res.Analysis.Solvable, "single-color fill must be solvable: " + res.FailureReason);
            Assert.IsTrue(res.Succeeded);
            Assert.GreaterOrEqual(res.Analysis.ApsEstimate, 0f, "measured APS present");
        }

        [Test]
        public void SameSettingsAndSeed_AreDeterministic()
        {
            var af = MakeAutofiller();
            var s = new BusBuddiesDifficultySettings { BusesChunks = 3, DeviationPercent = 0.4f, Columns = 3, Difficulty = 4 };
            var a = af.Complete(SingleColorDoc(6, 6, "A", 3, s), MakeProfile(), new CompletionRequest { Seed = 99 });
            var b = af.Complete(SingleColorDoc(6, 6, "A", 3, s), MakeProfile(), new CompletionRequest { Seed = 99 });
            Assert.AreEqual(
                a.TopSection.ToString(Newtonsoft.Json.Formatting.None),
                b.TopSection.ToString(Newtonsoft.Json.Formatting.None),
                "same settings + seed → identical queue");
        }

        // ── LOW-6: determinism regression lock. Locks the exact queue for one
        // fixed settings+seed combo — captured once via the real
        // BusBuddiesAutofiller.Complete (script-execute, one-off diagnostic) and
        // hardcoded here — so the analyzer-seed / rng dependency can't silently
        // drift without a test noticing.
        //
        // Re-captured after the re-tweak fix to BusBuddiesCapacityMath (bucket
        // count `k` is now computed upfront from the whole total instead of
        // discovered by greedy peeling): total=36, avg=15 (Chunks 2), window
        // [10,20] (Deviation 0.3) now correctly picks k=2 buses (36/15≈2.4→2,
        // both in-window) instead of the old greedy peel's 3 buses [14,11,11] —
        // fewer, cleaner buses, still summing exactly to 36. ──
        [Test]
        public void FixedSeed_ProducesLockedByteIdenticalQueue()
        {
            var af = MakeAutofiller();
            var s = new BusBuddiesDifficultySettings { BusesChunks = 2, DeviationPercent = 0.3f, Columns = 2, Difficulty = 2 };
            var res = af.Complete(SingleColorDoc(6, 6, "A", 2, s), MakeProfile(), new CompletionRequest { Seed = 123456 });

            const string expected =
                "{\"columns\":[{\"buses\":[{\"colorId\":\"A\",\"capacity\":17,\"hidden\":false,\"connectedId\":-1}]}," +
                "{\"buses\":[{\"colorId\":\"A\",\"capacity\":19,\"hidden\":false,\"connectedId\":-1}]}]}";

            Assert.AreEqual(expected, res.TopSection.ToString(Newtonsoft.Json.Formatting.None),
                "same settings + seed must byte-match the locked queue — a diff means the analyzer/rng-seed dependency drifted");
        }

        // NOTE: the former `WidenedSearch_FindsArrangement_SolvableOnlyUnderNonDefaultColumnSplit`
        // test + its `ColumnPhaseOnlyAnalyzer` fixture were removed on 2026-07-14. They
        // exercised the old random column-phase search gated on the analyzer's Status —
        // a mechanism replaced by BusBuddiesConstructiveArranger (solvable-by-construction,
        // verified by exact replay, not the analyzer). Solvability of ring/enclosed grids
        // is now covered by BusBuddiesConstructiveArrangerTests + Autofill_LargeRingGrid_IsSolvable.
    }
}
