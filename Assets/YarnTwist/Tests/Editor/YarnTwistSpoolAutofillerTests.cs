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
