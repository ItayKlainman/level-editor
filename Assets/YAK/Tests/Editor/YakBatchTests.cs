using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YAK;
using Hoppa.YAK.Editor;
using UnityEngine;

namespace Hoppa.YAK.Editor.Tests
{
    // Phase E verification: thumbnail render, batch staging scan/import, and the
    // generator end-to-end (procedural source → autofill → analyzer-solvable).
    // Hermetic: small synthetic profile so the analyzer/autofiller loop is fast.
    public sealed class YakBatchTests
    {
        [Test]
        public void Thumbnail_RendersExpectedSizeAndColor()
        {
            var pal = Pal();
            var grid = new GridData<ICellData>(2, 1);
            grid.Set(0, 0, new YAKWoolCell { ColorId = "Red" });
            grid.Set(1, 0, new YAKWoolCell { ColorId = "Blue" });
            var doc = new LevelDocument { Grid = grid };

            var tex = LevelThumbnail.Render(doc, pal, cellPixels: 3);
            Assert.AreEqual(6, tex.width);
            Assert.AreEqual(3, tex.height);
            // (0,0) is bottom-left = grid (0,0) = Red.
            Assert.AreEqual(Color.red, tex.GetPixel(0, 0));
            Assert.AreEqual(Color.blue, tex.GetPixel(3, 0));
            Object.DestroyImmediate(tex);
        }

        [Test]
        public void BatchStaging_ScanAndImport_RoundTrips()
        {
            string staging = Path.Combine(Application.temporaryCachePath, "yak_batch_test_" + System.Guid.NewGuid().ToString("N"));
            string target  = Path.Combine(Application.temporaryCachePath, "yak_import_test_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                File.WriteAllText(Path.Combine(staging, "a.json"), "{}");
                BatchStaging.WriteStats(Path.Combine(staging, "a" + BatchStaging.StatsSuffix),
                    new LevelStats { id = "a", status = "Solvable", solvable = true, aps = 3.2f, band = 2, distinctColors = 4 });
                File.WriteAllText(Path.Combine(staging, "b.json"), "{}");

                var cands = BatchStaging.Scan(staging);
                Assert.AreEqual(2, cands.Count, "two candidates (stats.json is not a candidate)");
                var a = cands.Find(c => c.Id == "a");
                Assert.IsNotNull(a.Stats);
                Assert.AreEqual(3.2f, a.Stats.aps, 1e-4f);
                Assert.IsNull(cands.Find(c => c.Id == "b").Stats);

                string dest = BatchStaging.Import(a.JsonPath, target);
                Assert.IsTrue(File.Exists(dest));
                Assert.AreEqual("a.json", Path.GetFileName(dest));
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                if (Directory.Exists(target)) Directory.Delete(target, true);
            }
        }

        [Test]
        public void Generator_Procedural_ProducesSolvableAutofilledLevel()
        {
            var profile = MakeProfile(6, 6, out var analyzer);
            var gen = ScriptableObject.CreateInstance<YAKLevelGenerator>();
            var genCfg = ScriptableObject.CreateInstance<YAKGeneratorConfig>();
            genCfg.UseImageSource = false;     // force procedural path
            genCfg.FallbackColors = 2;
            genCfg.TargetAPS = 3f;
            genCfg.ApsTolerance = 50f;         // wide → Succeeded if solvable
            genCfg.AnalyzerRollouts = 20;
            genCfg.ConveyorCount = 2;

            var res = gen.Generate(new LevelGeneratorRequest { Seed = 12345, AdvancedConfig = genCfg }, profile);

            Assert.IsNotNull(res.Document, "generator must produce a document");
            Assert.IsNotNull(res.Document.TopSection, "autofiller should have populated the spool section");
            foreach (var cell in res.Document.Grid.Cells)
                Assert.IsInstanceOf<YAKWoolCell>(cell, "procedural grid is all wool");

            // Independent confirmation it's solvable.
            var an = analyzer.Analyze(res.Document, profile, new AnalysisRequest { RolloutCount = 20, Seed = 12345 });
            Assert.AreEqual(AnalysisStatus.Solvable, an.Status, an.FailureReason);
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
                new ColorEntry { Id = "White", Color = Color.white },
                new ColorEntry { Id = "Black", Color = Color.black },
            };
            var pal = ScriptableObject.CreateInstance<ColorPaletteAsset>();
            typeof(ColorPaletteAsset).GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(pal, entries);
            return pal;
        }

        private static GameProfile MakeProfile(int w, int h, out YAKLevelAnalyzer analyzer)
        {
            var anaCfg = ScriptableObject.CreateInstance<YAKAnalyzerConfig>();
            anaCfg.Runs = 20;
            analyzer = ScriptableObject.CreateInstance<YAKLevelAnalyzer>();
            SetPrivate(analyzer, "_config", anaCfg);

            var afCfg = ScriptableObject.CreateInstance<YAKSpoolAutofillConfig>();
            afCfg.MinCapacity = 2; afCfg.MaxCapacity = 6; afCfg.AvgCapacity = 4;
            afCfg.ColumnRange = new Vector2Int(2, 3);
            afCfg.ApsTolerance = 50f; afCfg.MaxAttempts = 15; afCfg.SearchRolloutCount = 20;
            var af = ScriptableObject.CreateInstance<YAKSpoolAutofiller>();
            SetPrivate(af, "_config", afCfg);

            var profile = ScriptableObject.CreateInstance<GameProfile>();
            var so = new UnityEditor.SerializedObject(profile);
            so.FindProperty("_colorPalette").objectReferenceValue = Pal();
            so.FindProperty("_gridWidth").intValue = w;
            so.FindProperty("_gridHeight").intValue = h;
            so.FindProperty("_schemaId").stringValue = "yak";
            so.FindProperty("_levelAnalyzer").objectReferenceValue = analyzer;
            so.FindProperty("_levelCompleter").objectReferenceValue = af;
            so.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }

        private static void SetPrivate(Object obj, string field, Object value)
            => obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);
    }
}
