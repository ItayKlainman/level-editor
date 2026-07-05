using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.BusBuddies;
using Hoppa.BusBuddies.Editor;

namespace Hoppa.BusBuddies.Editor.Tests
{
    // Sub-phase 1c, Task 3: BusBuddiesTierProfileBuilder applies a tier's knobs to a
    // TRANSIENT profile clone without mutating on-disk originals (mirrors
    // YakTierProfileBuilderTests), plus a light BusBuddiesCurveBatchHarness staging
    // smoke test.
    public sealed class BusBuddiesTierProfileBuilderTests
    {
        private static void SetField(Object obj, string field, object value)
            => obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);
        private static T GetField<T>(Object obj, string field)
            => (T)obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj);

        private static GameProfile MakeBaseProfile(
            out BusBuddiesImageToGrid ig, out BusBuddiesAutofiller af,
            out BusBuddiesAutofillConfig afc, out BusBuddiesGeneratorConfig gc)
        {
            var profile = ScriptableObject.CreateInstance<GameProfile>();
            ig  = ScriptableObject.CreateInstance<BusBuddiesImageToGrid>();
            af  = ScriptableObject.CreateInstance<BusBuddiesAutofiller>();
            afc = ScriptableObject.CreateInstance<BusBuddiesAutofillConfig>();
            gc  = ScriptableObject.CreateInstance<BusBuddiesGeneratorConfig>();
            SetField(af, "_config", afc);
            SetField(profile, "_gridWidth", 30);
            SetField(profile, "_gridHeight", 30);
            SetField(profile, "_imageToGrid", ig);
            SetField(profile, "_levelCompleter", af);
            SetField(profile, "_generatorConfig", gc);
            return profile;
        }

        [Test]
        public void Build_AppliesTierKnobs_ToTransientProfile()
        {
            var baseProfile = MakeBaseProfile(out _, out _, out _, out _);
            var tier = new TierPreset { Name="Easy", GridWidth=10, GridHeight=10, MaxColors=2,
                AvgCapacity=9, ConveyorSlots=7, ColumnRange=new Vector2Int(2,3),
                HiddenRatio=0.25f, TargetAps=1.5f, ApsTolerance=0.4f };

            var built = BusBuddiesTierProfileBuilder.Build(baseProfile, tier);
            try
            {
                Assert.AreEqual(10, built.Profile.GridWidth);
                Assert.AreEqual(10, built.Profile.GridHeight);
                var bIg = (BusBuddiesImageToGrid)built.Profile.ImageToGrid;
                Assert.AreEqual(2, bIg.ColorCap);
                Assert.AreEqual(7, bIg.DefaultActiveSlots);
                var bAfc = GetField<BusBuddiesAutofillConfig>(built.Profile.LevelCompleter, "_config");
                Assert.AreEqual(9, bAfc.AvgCapacity);
                Assert.AreEqual(7, bAfc.DefaultActiveSlots);
                Assert.AreEqual(0.25f, bAfc.HiddenRatio);
                Assert.AreEqual(new Vector2Int(2,3), bAfc.ColumnRange);
                Assert.AreEqual(1.5f, bAfc.DefaultApsTarget);
                var bGc = (BusBuddiesGeneratorConfig)built.Profile.GeneratorConfig;
                Assert.AreEqual(1.5f, bGc.TargetAPS);
                Assert.AreEqual(7, bGc.ConveyorCount);
                Assert.AreEqual(2, bGc.FallbackColors, "FallbackColors must equal tier.MaxColors for procedural path");
            }
            finally { built.Cleanup(); }
        }

        [Test]
        public void Build_DoesNotMutateOriginals()
        {
            var baseProfile = MakeBaseProfile(out var ig, out var af, out var afc, out var gc);
            ig.ColorCap = 6; afc.AvgCapacity = 8; gc.TargetAPS = 3f;
            var tier = new TierPreset { Name="X", GridWidth=8, GridHeight=8, MaxColors=2,
                AvgCapacity=11, ConveyorSlots=5, ColumnRange=new Vector2Int(2,3), TargetAps=1f, ApsTolerance=0.5f };

            var built = BusBuddiesTierProfileBuilder.Build(baseProfile, tier);
            built.Cleanup();

            Assert.AreEqual(6,  ig.ColorCap,      "original ImageToGrid must be untouched");
            Assert.AreEqual(8,  afc.AvgCapacity,  "original autofill config must be untouched");
            Assert.AreEqual(3f, gc.TargetAPS,     "original generator config must be untouched");
            Assert.AreEqual(30, baseProfile.GridWidth, "original profile grid must be untouched");
        }

        // ── Light curve-batch staging smoke ───────────────────────────────────

        [Test]
        public void RunCurve_WritesNumberedLevels_WithTierStats()
        {
            var profile = MakeFullProfile();
            var curve = ScriptableObject.CreateInstance<BusBuddiesDifficultyCurveConfig>();
            curve.Presets = new List<TierPreset> {
                new TierPreset { Name="Tiny", GridWidth=6, GridHeight=6, MaxColors=2, AvgCapacity=6,
                                 ConveyorSlots=6, ColumnRange=new Vector2Int(2,3), TargetAps=2f, ApsTolerance=50f },
            };
            curve.Curve = new List<CurveSegment> { new CurveSegment { TierName="Tiny", LevelCount=2 } };

            string root = Path.Combine(Path.GetTempPath(), "busbuddies_curve_test_" + System.Guid.NewGuid().ToString("N"));
            string dir = BusBuddiesCurveBatchHarness.RunCurve(curve, profile, attemptsPerLevel: 8, stagingRoot: root);

            Assert.IsNotNull(dir);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "level_1.json")), "level_1.json written");
            Assert.IsTrue(File.Exists(Path.Combine(dir, "level_2.json")), "level_2.json written");
            string statsPath = Path.Combine(dir, "level_1" + BatchStaging.StatsSuffix);
            Assert.IsTrue(File.Exists(statsPath), "stats written");
            StringAssert.Contains("Tiny", File.ReadAllText(statsPath), "stats carry tier name");

            Directory.Delete(dir, true);
        }

        [Test]
        public void RunCurve_EmptyCurve_ReturnsNull()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var profile = MakeFullProfile();
            var curve = ScriptableObject.CreateInstance<BusBuddiesDifficultyCurveConfig>();
            Assert.IsNull(BusBuddiesCurveBatchHarness.RunCurve(curve, profile, 4, Path.GetTempPath()));
        }

        // ── Builders ──────────────────────────────────────────────────────────

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

        // Full in-memory BB pipeline profile (procedural fallback path). Loads the
        // real on-disk cell defs so BuildRegistry() has populated type ids.
        private static GameProfile MakeFullProfile()
        {
            var anaCfg = ScriptableObject.CreateInstance<BusBuddiesAnalyzerConfig>();
            anaCfg.Runs = 40;
            var analyzer = ScriptableObject.CreateInstance<BusBuddiesAnalyzer>();
            SetField(analyzer, "_config", anaCfg);

            var afCfg = ScriptableObject.CreateInstance<BusBuddiesAutofillConfig>();
            afCfg.MinCapacity = 2; afCfg.MaxCapacity = 6; afCfg.AvgCapacity = 4;
            afCfg.ColumnRange = new Vector2Int(2, 3);
            afCfg.ApsTolerance = 50f; afCfg.MaxAttempts = 12; afCfg.SearchRolloutCount = 40;
            var af = ScriptableObject.CreateInstance<BusBuddiesAutofiller>();
            SetField(af, "_config", afCfg);

            var genCfg = ScriptableObject.CreateInstance<BusBuddiesGeneratorConfig>();
            genCfg.UseImageSource = false;
            genCfg.FallbackColors = 2;
            genCfg.TargetAPS = 2f;
            genCfg.ApsTolerance = 50f;
            genCfg.AnalyzerRollouts = 40;
            genCfg.ConveyorCount = 2;
            var gen = ScriptableObject.CreateInstance<BusBuddiesLevelGenerator>();

            var imageToGrid = ScriptableObject.CreateInstance<BusBuddiesImageToGrid>();

            var emptyDef = UnityEditor.AssetDatabase.LoadAssetAtPath<BBEmptyCellDefinition>(
                "Assets/BusBuddies/Data/Config/CellDefs/BBEmptyCellDef.asset");
            var pixelDef = UnityEditor.AssetDatabase.LoadAssetAtPath<BBPixelCellDefinition>(
                "Assets/BusBuddies/Data/Config/CellDefs/BBPixelCellDef.asset");

            var profile = ScriptableObject.CreateInstance<GameProfile>();
            var so = new UnityEditor.SerializedObject(profile);
            so.FindProperty("_colorPalette").objectReferenceValue = Pal();
            so.FindProperty("_gridWidth").intValue = 6;
            so.FindProperty("_gridHeight").intValue = 6;
            so.FindProperty("_schemaId").stringValue = "busbuddies";
            so.FindProperty("_levelGenerator").objectReferenceValue = gen;
            so.FindProperty("_generatorConfig").objectReferenceValue = genCfg;
            so.FindProperty("_levelAnalyzer").objectReferenceValue = analyzer;
            so.FindProperty("_levelCompleter").objectReferenceValue = af;
            so.FindProperty("_imageToGrid").objectReferenceValue = imageToGrid;

            var cellTypes = so.FindProperty("_cellTypes");
            cellTypes.arraySize = 2;
            cellTypes.GetArrayElementAtIndex(0).objectReferenceValue = emptyDef;
            cellTypes.GetArrayElementAtIndex(1).objectReferenceValue = pixelDef;
            so.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }
    }
}
