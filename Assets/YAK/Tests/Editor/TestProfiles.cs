using System.Collections.Generic;
using System.Reflection;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YAK.Editor;
using UnityEngine;

namespace Hoppa.YAK.Editor.Tests
{
    // Builds a real, in-memory YAK pipeline profile for harness tests: palette +
    // YAKLevelGenerator + YAKSpoolAutofiller(+config) + YAKLevelAnalyzer(+config) +
    // YAKImageToGrid + YAKGeneratorConfig (UseImageSource=false so the procedural
    // grid path runs without needing image files on disk). Mirrors the wiring in
    // YakBatchTests/YakAnalyzerTests; grids/tolerances kept tiny + wide for speed.
    public static class TestProfiles
    {
        private static void SetField(Object obj, string field, object value)
            => obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);

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

        public static GameProfile MakeYakProfile()
        {
            // Analyzer.
            var anaCfg = ScriptableObject.CreateInstance<YAKAnalyzerConfig>();
            anaCfg.Runs = 20;
            var analyzer = ScriptableObject.CreateInstance<YAKLevelAnalyzer>();
            SetField(analyzer, "_config", anaCfg);

            // Auto-filler (completer).
            var afCfg = ScriptableObject.CreateInstance<YAKSpoolAutofillConfig>();
            afCfg.MinCapacity = 2; afCfg.MaxCapacity = 6; afCfg.AvgCapacity = 4;
            afCfg.ColumnRange = new Vector2Int(2, 3);
            afCfg.ApsTolerance = 50f; afCfg.MaxAttempts = 12; afCfg.SearchRolloutCount = 20;
            var af = ScriptableObject.CreateInstance<YAKSpoolAutofiller>();
            SetField(af, "_config", afCfg);

            // Generator + its config (procedural path).
            var genCfg = ScriptableObject.CreateInstance<YAKGeneratorConfig>();
            genCfg.UseImageSource = false;
            genCfg.FallbackColors = 2;
            genCfg.TargetAPS = 2f;
            genCfg.ApsTolerance = 50f;    // wide → Succeeded if solvable
            genCfg.AnalyzerRollouts = 20;
            genCfg.ConveyorCount = 2;
            var gen = ScriptableObject.CreateInstance<YAKLevelGenerator>();

            // Image→grid (present but unused with UseImageSource=false).
            var imageToGrid = ScriptableObject.CreateInstance<YAKImageToGrid>();

            // Cell type defs (so BuildRegistry / serialization round-trips work).
            var emptyDef = ScriptableObject.CreateInstance<YAKEmptyCellDefinition>();
            var woolDef  = ScriptableObject.CreateInstance<YAKWoolCellDefinition>();

            var profile = ScriptableObject.CreateInstance<GameProfile>();
            var so = new UnityEditor.SerializedObject(profile);
            so.FindProperty("_colorPalette").objectReferenceValue = Pal();
            so.FindProperty("_gridWidth").intValue = 6;
            so.FindProperty("_gridHeight").intValue = 6;
            so.FindProperty("_schemaId").stringValue = "yak";
            so.FindProperty("_levelGenerator").objectReferenceValue = gen;
            so.FindProperty("_generatorConfig").objectReferenceValue = genCfg;
            so.FindProperty("_levelAnalyzer").objectReferenceValue = analyzer;
            so.FindProperty("_levelCompleter").objectReferenceValue = af;
            so.FindProperty("_imageToGrid").objectReferenceValue = imageToGrid;

            var cellTypes = so.FindProperty("_cellTypes");
            cellTypes.arraySize = 2;
            cellTypes.GetArrayElementAtIndex(0).objectReferenceValue = emptyDef; // first = empty
            cellTypes.GetArrayElementAtIndex(1).objectReferenceValue = woolDef;
            so.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }
    }
}
