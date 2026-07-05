using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.BusBuddies;
using Hoppa.BusBuddies.Editor;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor.Tests
{
    // Sub-phase 1c, Task 2: BusBuddiesLevelGenerator drives the real analyzer +
    // autofiller loop on a small synthetic profile. Mirrors YakBatchTests'
    // generator coverage: procedural → solvable autofilled level; no-profile/palette
    // → graceful fail; deterministic for the same seed.
    public sealed class BusBuddiesGeneratorTests
    {
        [Test]
        public void Generator_Procedural_ProducesSolvableAutofilledLevel()
        {
            var profile = MakeProfile(6, 6, out var analyzer);
            var gen = ScriptableObject.CreateInstance<BusBuddiesLevelGenerator>();
            var genCfg = MakeGenConfig();

            var res = gen.Generate(new LevelGeneratorRequest { Seed = 12345, AdvancedConfig = genCfg }, profile);

            Assert.IsNotNull(res.Document, "generator must produce a document");
            Assert.IsNotNull(res.Document.TopSection, "autofiller should have populated the bus queue");
            foreach (var cell in res.Document.Grid.Cells)
                Assert.IsInstanceOf<BBPixelCell>(cell, "procedural grid is all pixel cells (no carved empties)");

            // Independent confirmation it is solvable.
            var an = analyzer.Analyze(res.Document, profile, new AnalysisRequest { RolloutCount = 40, Seed = 12345 });
            Assert.AreEqual(AnalysisStatus.Solvable, an.Status, an.FailureReason);
        }

        [Test]
        public void Generator_NoProfile_FailsGracefully()
        {
            var gen = ScriptableObject.CreateInstance<BusBuddiesLevelGenerator>();
            var res = gen.Generate(new LevelGeneratorRequest { Seed = 1, AdvancedConfig = MakeGenConfig() }, null);
            Assert.IsFalse(res.Succeeded, "no profile → must not succeed");
            Assert.IsTrue(res.RuleRejectCounts.ContainsKey("__no_profile_or_palette__"));
        }

        [Test]
        public void Generator_NoPalette_FailsGracefully()
        {
            var gen = ScriptableObject.CreateInstance<BusBuddiesLevelGenerator>();
            var profile = ScriptableObject.CreateInstance<GameProfile>(); // no palette wired
            var res = gen.Generate(new LevelGeneratorRequest { Seed = 1, AdvancedConfig = MakeGenConfig() }, profile);
            Assert.IsFalse(res.Succeeded, "no palette → must not succeed");
            Assert.IsTrue(res.RuleRejectCounts.ContainsKey("__no_profile_or_palette__"));
        }

        [Test]
        public void Generator_Deterministic_ForSameSeed()
        {
            var profile = MakeProfile(6, 6, out _);
            var gen = ScriptableObject.CreateInstance<BusBuddiesLevelGenerator>();
            var genCfg = MakeGenConfig();

            var a = gen.Generate(new LevelGeneratorRequest { Seed = 777, AdvancedConfig = genCfg }, profile);
            var b = gen.Generate(new LevelGeneratorRequest { Seed = 777, AdvancedConfig = genCfg }, profile);

            Assert.IsNotNull(a.Document); Assert.IsNotNull(b.Document);

            // Grid identical.
            Assert.AreEqual(a.Document.Grid.Cells.Length, b.Document.Grid.Cells.Length);
            for (int i = 0; i < a.Document.Grid.Cells.Length; i++)
                Assert.AreEqual(((BBPixelCell)a.Document.Grid.Cells[i]).ColorId,
                                ((BBPixelCell)b.Document.Grid.Cells[i]).ColorId,
                                $"cell {i} differs between identical seeds");

            // Bus queue identical.
            Assert.AreEqual(a.Document.TopSection?.ToString(Newtonsoft.Json.Formatting.None),
                            b.Document.TopSection?.ToString(Newtonsoft.Json.Formatting.None),
                            "same seed → identical bus queue");
        }

        // ── Builders ──────────────────────────────────────────────────────────

        private static BusBuddiesGeneratorConfig MakeGenConfig()
        {
            var genCfg = ScriptableObject.CreateInstance<BusBuddiesGeneratorConfig>();
            genCfg.UseImageSource = false;   // force procedural path
            genCfg.FallbackColors = 2;
            genCfg.TargetAPS = 3f;
            genCfg.ApsTolerance = 50f;       // wide → Succeeded if solvable
            genCfg.AnalyzerRollouts = 40;
            genCfg.ConveyorCount = 2;
            return genCfg;
        }

        private static ColorPaletteAsset Pal()
        {
            var entries = new List<ColorEntry>
            {
                new ColorEntry { Id = "Red",   Color = Color.red },
                new ColorEntry { Id = "Blue",  Color = Color.blue },
                new ColorEntry { Id = "Green", Color = Color.green },
                new ColorEntry { Id = "Grey",  Color = new Color(0.5f, 0.5f, 0.5f) },
            };
            var pal = ScriptableObject.CreateInstance<ColorPaletteAsset>();
            typeof(ColorPaletteAsset).GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(pal, entries);
            return pal;
        }

        private static GameProfile MakeProfile(int w, int h, out BusBuddiesAnalyzer analyzer)
        {
            var anaCfg = ScriptableObject.CreateInstance<BusBuddiesAnalyzerConfig>();
            anaCfg.Runs = 40;
            analyzer = ScriptableObject.CreateInstance<BusBuddiesAnalyzer>();
            SetField(analyzer, "_config", anaCfg);

            var afCfg = ScriptableObject.CreateInstance<BusBuddiesAutofillConfig>();
            afCfg.MinCapacity = 2; afCfg.MaxCapacity = 6; afCfg.AvgCapacity = 4;
            afCfg.ColumnRange = new Vector2Int(2, 3);
            afCfg.ApsTolerance = 50f; afCfg.MaxAttempts = 15; afCfg.SearchRolloutCount = 40;
            var af = ScriptableObject.CreateInstance<BusBuddiesAutofiller>();
            SetField(af, "_config", afCfg);

            var profile = ScriptableObject.CreateInstance<GameProfile>();
            var so = new UnityEditor.SerializedObject(profile);
            so.FindProperty("_colorPalette").objectReferenceValue = Pal();
            so.FindProperty("_gridWidth").intValue = w;
            so.FindProperty("_gridHeight").intValue = h;
            so.FindProperty("_schemaId").stringValue = "busbuddies";
            so.FindProperty("_levelAnalyzer").objectReferenceValue = analyzer;
            so.FindProperty("_levelCompleter").objectReferenceValue = af;
            so.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }

        private static void SetField(Object obj, string field, Object value)
            => obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);
    }
}
