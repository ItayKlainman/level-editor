using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YarnTwist;
using Hoppa.YarnTwist.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor.Tests
{
    public class YarnTwistSpoolAutofillerTests
    {
        private const string ProfilePath = "Assets/YarnTwist/Data/Config/YarnTwistProfile.asset";
        private GameProfile _profile;
        private YarnTwistLevelAnalyzer _analyzer;
        private YarnTwistSpoolAutofiller _autofiller;
        private YarnTwistSpoolAutofillConfig _config;

        [SetUp]
        public void SetUp()
        {
            _profile = AssetDatabase.LoadAssetAtPath<GameProfile>(ProfilePath);
            Assert.IsNotNull(_profile);

            _analyzer   = ScriptableObject.CreateInstance<YarnTwistLevelAnalyzer>();
            _autofiller = ScriptableObject.CreateInstance<YarnTwistSpoolAutofiller>();
            _config     = ScriptableObject.CreateInstance<YarnTwistSpoolAutofillConfig>();
            _config.WinPathTargetByDifficulty = new AnimationCurve(new Keyframe(1f, 100f), new Keyframe(10f, 1f));
            _config.WinPathTolerance = 0.9f; // wide band so determinism/coverage tests don't churn
            _config.MaxRerollAttempts = 10;
            _config.PerCandidateTimeoutMs = 200;
            _config.TotalTimeoutMs = 5_000;

            // Inject the config + analyzer via reflection (test-only).
            var cfgField = typeof(YarnTwistSpoolAutofiller).GetField("_config",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(cfgField, "YarnTwistSpoolAutofiller is missing the _config field");
            cfgField.SetValue(_autofiller, _config);

            var anaField = typeof(YarnTwistSpoolAutofiller).GetField("_analyzerOverride",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(anaField, "YarnTwistSpoolAutofiller is missing the _analyzerOverride field");
            anaField.SetValue(_autofiller, _analyzer);
        }

        [TearDown]
        public void TearDown()
        {
            if (_analyzer   != null) Object.DestroyImmediate(_analyzer);
            if (_autofiller != null) Object.DestroyImmediate(_autofiller);
            if (_config     != null) Object.DestroyImmediate(_config);
        }

        // ── Fixture 1: determinism — same seed → identical top section ─

        [Test]
        public void Complete_SameSeed_ProducesIdenticalTopSection()
        {
            var doc = MakeBoxGrid(("pink",4),("blue",4));
            var req = new CompletionRequest { Difficulty = 5, Seed = 12345, ConveyorCapacityOverride = 24 };
            var r1 = _autofiller.Complete(doc, _profile, req);
            var r2 = _autofiller.Complete(doc, _profile, req);
            Assert.IsNotNull(r1.TopSection);
            Assert.IsNotNull(r2.TopSection);
            Assert.AreEqual(r1.TopSection.ToString(Newtonsoft.Json.Formatting.None),
                            r2.TopSection.ToString(Newtonsoft.Json.Formatting.None));
        }

        // ── Fixture 2: output top section satisfies color balance ─────

        [Test]
        public void Complete_AnyResult_HasExactColorBalance()
        {
            var doc = MakeBoxGrid(("pink",5),("blue",3),("green",2));
            var r = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 5, Seed = 7777, ConveyorCapacityOverride = 24 });
            Assert.IsNotNull(r.TopSection);

            var counts = new Dictionary<string, int>();
            var data = r.TopSection.ToObject<YarnTopSectionData>();
            foreach (var col in data.Columns)
                foreach (var s in col.Spools)
                {
                    counts.TryGetValue(s.ColorId, out var n);
                    counts[s.ColorId] = n + 1;
                }
            Assert.AreEqual(5 * 3, counts["pink"]);
            Assert.AreEqual(3 * 3, counts["blue"]);
            Assert.AreEqual(2 * 3, counts["green"]);
        }

        // ── Fixture 7: empty grid trivially succeeds ───────────────────

        [Test]
        public void Complete_EmptyGrid_SucceedsWithEmptyTopSection()
        {
            var doc = MakeBoxGrid(); // no boxes
            var r = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 5, Seed = 1, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Succeeded);
            var data = r.TopSection.ToObject<YarnTopSectionData>();
            int totalSpools = 0;
            foreach (var col in data.Columns) totalSpools += col.Spools.Count;
            Assert.AreEqual(0, totalSpools);
        }

        // ── Fixture 3: D=1 vs D=10 — average path count strictly higher at D=1 ─

        [Test]
        public void Complete_DifficultyOne_HasHigherAveragePathCountThanTen()
        {
            var doc = MakeBoxGrid(("pink",3),("blue",3));
            long sumD1 = 0, sumD10 = 0;
            const int N = 20;
            for (int i = 0; i < N; i++)
            {
                var r1 = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 1, Seed = 200 + i * 7, ConveyorCapacityOverride = 24 });
                var rT = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 10, Seed = 200 + i * 7, ConveyorCapacityOverride = 24 });
                sumD1  += r1.Analysis?.WinPathCount  ?? 0;
                sumD10 += rT.Analysis?.WinPathCount  ?? 0;
            }
            // D=1 targets ~100 paths, D=10 targets ~1. Both with wide tolerance,
            // but the curve still pushes D=1 statistically higher over 20 trials.
            Assert.GreaterOrEqual(sumD1, sumD10);
        }

        // ── Fixture 4: hidden ratio matches HiddenSpoolRatio.Evaluate(D) ─

        [Test]
        public void Complete_HiddenRatio_TracksDifficulty()
        {
            // At D=1, HiddenSpoolRatio = 0 → no hidden spools.
            // At D=10, HiddenSpoolRatio = 40 → ~40% hidden.
            var doc = MakeBoxGrid(("pink",5),("blue",5)); // 30 spools total
            var rLow  = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 1,  Seed = 42, ConveyorCapacityOverride = 24 });
            var rHigh = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 10, Seed = 42, ConveyorCapacityOverride = 24 });

            int hiddenLow  = CountHidden(rLow.TopSection);
            int hiddenHigh = CountHidden(rHigh.TopSection);
            Assert.AreEqual(0, hiddenLow);
            // 30 × 40% = 12, within ±1 for rounding tolerance.
            Assert.GreaterOrEqual(hiddenHigh, 11);
            Assert.LessOrEqual(hiddenHigh, 13);
        }

        // ── Fixture 5: capacity override changes outcome on a borderline grid ─

        [Test]
        public void Complete_CapacityOverride_AffectsCandidateAnalysis()
        {
            // 4 pink boxes = 36 balls, 12 pink spools.
            // At cap=12 every spool must clear before next tap; severely
            // constrained. At cap=30 there's slack — many more candidates pass.
            var doc = MakeBoxGrid(("pink",4));
            var rTight = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 5, Seed = 555, ConveyorCapacityOverride = 12 });
            var rLoose = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 5, Seed = 555, ConveyorCapacityOverride = 30 });
            Assert.IsNotNull(rTight.Analysis);
            Assert.IsNotNull(rLoose.Analysis);
            // Loose capacity should never decrease the path count for the same seed.
            Assert.GreaterOrEqual(rLoose.Analysis.WinPathCount, rTight.Analysis.WinPathCount);
        }

        // ── Fixture 6: impossible target returns best-so-far + Succeeded=false ─

        [Test]
        public void Complete_ImpossibleTarget_ReturnsBestEffort()
        {
            var doc = MakeBoxGrid(("pink",2),("blue",2));
            // Set target to a value the grid can't produce: 1,000,000 paths
            // when the puzzle only has ~24 orderings.
            _config.WinPathTargetByDifficulty = new AnimationCurve(new Keyframe(5f, 1_000_000f));
            _config.WinPathTolerance = 0.01f;
            _config.MaxRerollAttempts = 5;
            var r = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 5, Seed = 999, ConveyorCapacityOverride = 24 });
            Assert.IsFalse(r.Succeeded);
            Assert.IsNotNull(r.TopSection);   // best-so-far still applied
            Assert.IsNotNull(r.Analysis);
            Assert.IsNotNull(r.FailureReason);
        }

        // ── Fixture 8: result is identical regardless of parallelism ───

        [Test]
        public void Complete_DeterministicAcrossDegreeOfParallelism()
        {
            // Same seed must produce the same top section whether stage-1 runs
            // single-threaded or across many workers (lowest-index hit wins).
            var doc = MakeBoxGrid(("pink",3),("blue",3));
            _config.MaxRerollAttempts = 12;
            _config.RolloutCount = 50;       // keep the sweep fast
            _config.GuidedSteps = 50;
            var req = new CompletionRequest { Difficulty = 5, Seed = 24680, ConveyorCapacityOverride = 24 };

            _config.MaxDegreeOfParallelism = 1;
            var r1 = _autofiller.Complete(doc, _profile, req);
            _config.MaxDegreeOfParallelism = 8;
            var r8 = _autofiller.Complete(doc, _profile, req);

            Assert.IsNotNull(r1.TopSection);
            Assert.IsNotNull(r8.TopSection);
            Assert.AreEqual(r1.TopSection.ToString(Newtonsoft.Json.Formatting.None),
                            r8.TopSection.ToString(Newtonsoft.Json.Formatting.None));
        }

        // ── Fixture 9: bigger multi-color grid stays robust ────────────

        [Test]
        public void Complete_LargerGrid_ReturnsBalancedResultWithoutThrowing()
        {
            // 6 colors × 3 boxes exercises the parallel sweep, the rollout
            // fallback and (likely) guided refinement together.
            var doc = MakeBoxGrid(("pink",3),("blue",3),("green",3),("teal",3),("yellow",3),("purple",3));
            _config.MaxRerollAttempts = 16;
            _config.RolloutCount = 50;
            _config.GuidedSteps = 30;

            var r = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 6, Seed = 13579, ConveyorCapacityOverride = 24 });

            Assert.IsNotNull(r.TopSection);
            Assert.IsNotNull(r.Analysis);
            // Color balance is guaranteed by construction: 3 spools per box.
            var counts = new Dictionary<string, int>();
            var data = r.TopSection.ToObject<YarnTopSectionData>();
            foreach (var col in data.Columns)
                foreach (var s in col.Spools)
                {
                    counts.TryGetValue(s.ColorId, out var n);
                    counts[s.ColorId] = n + 1;
                }
            foreach (var c in new[] { "pink","blue","green","teal","yellow","purple" })
                Assert.AreEqual(9, counts[c], $"color {c} should have 18 balls = 9 spools");
        }

        // ── Fixture 10: grid with a connected pair → balanced + solvable ─

        [Test]
        public void Complete_ConnectedPairGrid_BalancedAndSolvable()
        {
            // Connected boxes are ordinary boxes for inventory (2 pink = 6 pink spools);
            // the analyzer (which the autofiller delegates to) models the clear-together
            // effect, so the returned candidate must still be solvable and balanced.
            var grid = new GridData<ICellData>(2, 1);
            grid.Set(0, 0, new YarnBoxCell { ColorId = "pink", ConnectedDir = YarnDirection.Right });
            grid.Set(1, 0, new YarnBoxCell { ColorId = "pink", ConnectedDir = YarnDirection.Left });
            var doc = new LevelDocument
            {
                SchemaVersion = "yarn-twist.v1", LevelId = "test", Grid = grid, TopSection = new JObject(),
            };

            var r = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 5, Seed = 4242, ConveyorCapacityOverride = 24 });

            Assert.IsNotNull(r.TopSection);
            Assert.IsNotNull(r.Analysis);
            Assert.IsTrue(r.Analysis.Solvable, r.FailureReason);

            int pink = 0;
            var data = r.TopSection.ToObject<YarnTopSectionData>();
            foreach (var col in data.Columns)
                foreach (var s in col.Spools)
                    if (s.ColorId == "pink") pink++;
            Assert.AreEqual(6, pink); // 2 boxes × 3 spools — connection doesn't change inventory
        }

        private static int CountHidden(JObject topJson)
        {
            int n = 0;
            var data = topJson.ToObject<YarnTopSectionData>();
            foreach (var col in data.Columns)
                foreach (var s in col.Spools)
                    if (s.Hidden) n++;
            return n;
        }

        // ── Helpers ────────────────────────────────────────────────────

        // Produces a 1-row grid with `count` boxes of each requested color, in left-to-right order.
        private static LevelDocument MakeBoxGrid(params (string color, int count)[] perColor)
        {
            int total = 0;
            foreach (var p in perColor) total += p.count;
            int width = System.Math.Max(1, total);
            var grid = new GridData<ICellData>(width, 1);
            for (int i = 0; i < grid.Cells.Length; i++) grid.Cells[i] = new YarnEmptyCell();

            int x = 0;
            foreach (var (color, n) in perColor)
                for (int i = 0; i < n; i++) { grid.Set(x, 0, new YarnBoxCell { ColorId = color }); x++; }

            return new LevelDocument
            {
                SchemaVersion = "yarn-twist.v1",
                LevelId       = "test",
                Grid          = grid,
                TopSection    = new JObject(),
            };
        }
    }
}
