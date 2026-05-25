using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;

namespace Hoppa.YarnTwist.Editor.Tests
{
    // Loads the live YarnTwistProfile asset so tests run against the same
    // generator / config the operator uses in-Editor. Avoids reflecting into
    // GameProfile to set private fields.
    public class YarnTwistLevelGeneratorTests
    {
        private const string ProfilePath = "Assets/YarnTwist/Data/Config/YarnTwistProfile.asset";

        private GameProfile _profile;

        [SetUp]
        public void SetUp()
        {
            _profile = AssetDatabase.LoadAssetAtPath<GameProfile>(ProfilePath);
            Assert.IsNotNull(_profile, $"Failed to load {ProfilePath}");
            Assert.IsNotNull(_profile.LevelGenerator, "Profile is missing LevelGenerator");
            Assert.IsNotNull(_profile.GeneratorConfig, "Profile is missing GeneratorConfig");
        }

        // ── Determinism ───────────────────────────────────────────────────

        [Test]
        public void Generate_SameSeed_ProducesIdenticalStructure()
        {
            var req = new LevelGeneratorRequest
            {
                Difficulty     = 5,
                Seed           = 12345,
                AdvancedConfig = _profile.GeneratorConfig,
            };
            var r1 = _profile.LevelGenerator.Generate(req, _profile);
            var r2 = _profile.LevelGenerator.Generate(req, _profile);

            Assert.IsNotNull(r1.Document);
            Assert.IsNotNull(r2.Document);
            // Modified timestamp differs across runs — compare only structural parts.
            Assert.AreEqual(StructureSignature(r1.Document), StructureSignature(r2.Document));
        }

        // ── Validity sweep ────────────────────────────────────────────────

        [TestCase(1, 101)]
        [TestCase(3, 202)]
        [TestCase(5, 303)]
        [TestCase(8, 404)]
        [TestCase(10, 505)]
        public void Generate_DifficultySweep_PassesProfileRules(int difficulty, int seed)
        {
            var req = new LevelGeneratorRequest
            {
                Difficulty     = difficulty,
                Seed           = seed,
                AdvancedConfig = _profile.GeneratorConfig,
            };

            var result = _profile.LevelGenerator.Generate(req, _profile);
            Assert.IsNotNull(result.Document, "Generator produced null document");
            Assert.IsTrue(result.Succeeded,
                $"Generator gave up after {result.CandidatesTried} candidates. " +
                $"Rejections: {DescribeRejects(result)}");

            // Independent re-verification: re-run validation outside the
            // generator's loop and assert no errors.
            var eval = LevelGeneratorRunner.Evaluate(result.Document, _profile);
            Assert.IsFalse(eval.HasErrors,
                $"Returned document has rule errors: {string.Join(", ", eval.ErrorsByRule.Keys)}");
        }

        // ── Override propagation ──────────────────────────────────────────

        [Test]
        public void Generate_GridWidthOverride_RespectedInDocument()
        {
            // Temporarily mutate the shared config; restore after.
            var cfg = (YarnTwistGeneratorConfig)_profile.GeneratorConfig;
            int saved = cfg.GridWidthOverride;
            try
            {
                cfg.GridWidthOverride = 8;
                var req = new LevelGeneratorRequest
                {
                    Difficulty     = 5,
                    Seed           = 4242,
                    AdvancedConfig = cfg,
                };
                var result = _profile.LevelGenerator.Generate(req, _profile);
                Assert.IsNotNull(result.Document);
                Assert.AreEqual(8, result.Document.Grid.Width);
            }
            finally
            {
                cfg.GridWidthOverride = saved;
            }
        }

        // ── Diagnostics ───────────────────────────────────────────────────

        [Test]
        public void Generate_Result_PopulatesDiagnosticFields()
        {
            var req = new LevelGeneratorRequest
            {
                Difficulty     = 5,
                Seed           = 0,                         // 0 = randomize
                AdvancedConfig = _profile.GeneratorConfig,
            };
            var result = _profile.LevelGenerator.Generate(req, _profile);
            Assert.AreNotEqual(0, result.SeedUsed, "SeedUsed should be resolved from 0 → random");
            Assert.GreaterOrEqual(result.CandidatesTried, 1);
            Assert.GreaterOrEqual(result.ElapsedMs, 0);
            Assert.IsNotNull(result.RuleRejectCounts);
        }

        // ── APS metadata recording ────────────────────────────────────────

        [Test]
        public void Generate_WithTargetAPS_RecordsValueOnMetadata()
        {
            var req = new LevelGeneratorRequest
            {
                Difficulty     = 4,
                Seed           = 9999,
                TargetAPS      = 2.5f,
                AdvancedConfig = _profile.GeneratorConfig,
            };
            var result = _profile.LevelGenerator.Generate(req, _profile);
            Assert.IsNotNull(result.Document);
            Assert.AreEqual(2.5f, result.Document.Metadata.Aps);
        }

        // ── Helpers ───────────────────────────────────────────────────────

        // Builds a comparable signature from the structural parts of the doc
        // (grid + top section) so determinism tests aren't broken by per-run
        // timestamps in metadata or random ids.
        private string StructureSignature(LevelDocument doc)
        {
            var registry = _profile.BuildRegistry();
            var json     = new JsonLevelSerializer().Save(doc, registry);
            var root     = JObject.Parse(json);
            root.Remove("metadata");
            root.Remove("levelId");
            root.Remove("displayName");
            // gameData has timestamp-free contents in v1 (coinReward); keep it.
            return root.ToString();
        }

        private static string DescribeRejects(LevelGeneratorResult r)
        {
            if (r.RuleRejectCounts == null || r.RuleRejectCounts.Count == 0) return "(none)";
            var parts = new System.Collections.Generic.List<string>(r.RuleRejectCounts.Count);
            foreach (var kv in r.RuleRejectCounts) parts.Add($"{kv.Key}×{kv.Value}");
            return string.Join(", ", parts);
        }
    }
}
