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
        // drift without a test noticing. ──
        [Test]
        public void FixedSeed_ProducesLockedByteIdenticalQueue()
        {
            var af = MakeAutofiller();
            var s = new BusBuddiesDifficultySettings { BusesChunks = 2, DeviationPercent = 0.3f, Columns = 2, Difficulty = 2 };
            var res = af.Complete(SingleColorDoc(6, 6, "A", 2, s), MakeProfile(), new CompletionRequest { Seed = 123456 });

            const string expected =
                "{\"columns\":[{\"buses\":[{\"colorId\":\"A\",\"capacity\":14,\"hidden\":false,\"connectedId\":-1}," +
                "{\"colorId\":\"A\",\"capacity\":11,\"hidden\":false,\"connectedId\":-1}]}," +
                "{\"buses\":[{\"colorId\":\"A\",\"capacity\":11,\"hidden\":false,\"connectedId\":-1}]}]}";

            Assert.AreEqual(expected, res.TopSection.ToString(Newtonsoft.Json.Formatting.None),
                "same settings + seed must byte-match the locked queue — a diff means the analyzer/rng-seed dependency drifted");
        }

        // ── MED-2: widened search. Reports Solvable only when the queue's column-0
        // head bus is color "B" — with 2 equal colors + 2 columns, the DEFAULT
        // (phase-0) arrangement always puts "A" at the head of column 0, so this
        // is reachable ONLY by trying a non-default column-assignment phase.
        // Regression guard: the old thin relaxation (difficulty ladder only, no
        // attempt search) would report this unsolvable.
        private sealed class ColumnPhaseOnlyAnalyzer : LevelAnalyzerAsset
        {
            public override LevelAnalysisResult Analyze(LevelDocument doc, GameProfile profile, AnalysisRequest req)
            {
                var top = doc.TopSection?.ToObject<BusQueueData>();
                bool solvable = top != null && top.Columns.Count > 0 && top.Columns[0].Buses.Count > 0
                                && top.Columns[0].Buses[0].ColorId == "B";
                return new LevelAnalysisResult
                {
                    Status = solvable ? AnalysisStatus.Solvable : AnalysisStatus.Unsolvable,
                    Solvable = solvable,
                    ApsEstimate = solvable ? 2f : 0f,
                };
            }
        }

        [Test]
        public void WidenedSearch_FindsArrangement_SolvableOnlyUnderNonDefaultColumnSplit()
        {
            // Wide capacity window (avg 100) keeps each 5-pixel color a single bus
            // regardless of rng draw, isolating column-phase as the only thing that
            // can vary across search attempts.
            var cfg = ScriptableObject.CreateInstance<BusBuddiesAutofillConfig>();
            cfg.ChunksBase = 100; cfg.ChunksStep = 0;
            cfg.MaxAttempts = 8;
            var af = ScriptableObject.CreateInstance<BusBuddiesAutofiller>();
            SetField(af, "_config", cfg);

            var profile = ScriptableObject.CreateInstance<GameProfile>();
            SetField(profile, "_levelAnalyzer", ScriptableObject.CreateInstance<ColumnPhaseOnlyAnalyzer>());

            var grid = new GridData<ICellData>(2, 5); // 5 cells "A", 5 cells "B"
            for (int y = 0; y < 5; y++)
            {
                grid.Set(0, y, new BBPixelCell { ColorId = "A" });
                grid.Set(1, y, new BBPixelCell { ColorId = "B" });
            }
            var doc = new LevelDocument
            {
                SchemaVersion = "busbuddies", LevelId = "t", Grid = grid,
                GameData = new JObject { ["conveyorCount"] = 2 },
            };
            new BusBuddiesDifficultySettings { BusesChunks = 1, DeviationPercent = 0f, Columns = 2, Difficulty = 1 }.WriteTo(doc);

            var res = af.Complete(doc, profile, new CompletionRequest { Seed = 1 });

            Assert.IsTrue(res.Succeeded,
                "widened search must find the arrangement reachable only via a non-default column phase: " + res.FailureReason);
            var top = res.TopSection.ToObject<BusQueueData>();
            Assert.AreEqual("B", top.Columns[0].Buses[0].ColorId, "accepted arrangement is a non-default-phase split");
        }
    }
}
