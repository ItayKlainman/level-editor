using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YAK;
using Hoppa.YAK.Editor;
using UnityEngine;

namespace Hoppa.YAK.Editor.Tests
{
    // Phase C verification + Task-4 Bus Buddies options:
    // YAKImageToGrid produces a profile-sized, palette-quantized, color-capped grid
    // deterministically, with correct BackgroundFill and OutlineSubject behavior.
    // Hermetic — uses a synthetic palette + in-memory profile (no project wiring).
    public sealed class YakImageToGridTests
    {
        // ── Original tests (regression guard) ────────────────────────────────

        [Test]
        public void Convert_ProducesProfileSizedAllWoolGrid_WithNoEmpties()
        {
            var profile = MakeProfile(8, 6, Pal());
            var conv = MakeConverter(cap: 6);
            var src = MakeTexture(8, 6, (x, y) => Color.red);

            var doc = conv.Convert(src, profile);

            Assert.AreEqual(8, doc.Grid.Width);
            Assert.AreEqual(6, doc.Grid.Height);
            foreach (var cell in doc.Grid.Cells)
            {
                Assert.IsInstanceOf<YAKWoolCell>(cell, "every cell must be wool (no empties)");
                Assert.IsFalse(string.IsNullOrEmpty(((YAKWoolCell)cell).ColorId));
            }
            UnityEngine.Object.DestroyImmediate(src);
        }

        [Test]
        public void Convert_IsDeterministic_ForSameImageAndConfig()
        {
            var profile = MakeProfile(12, 12, Pal());
            var conv = MakeConverter(cap: 4);
            var src = SubjectOnBackground(12, 12);

            var a = conv.Convert(src, profile);
            var b = conv.Convert(src, profile);

            for (int i = 0; i < a.Grid.Cells.Length; i++)
                Assert.AreEqual(((YAKWoolCell)a.Grid.Cells[i]).ColorId,
                                ((YAKWoolCell)b.Grid.Cells[i]).ColorId,
                                $"cell {i} differs between identical conversions");
            UnityEngine.Object.DestroyImmediate(src);
        }

        [Test]
        public void Convert_RespectsColorCap()
        {
            var profile = MakeProfile(12, 12, Pal());
            var conv = MakeConverter(cap: 3);
            // Source painted from 6 distinct palette colors in vertical bands.
            var bands = new[] { Color.red, Color.green, Color.blue, Color.white, Color.black, new Color(0.5f, 0.5f, 0.5f) };
            var src = MakeTexture(12, 12, (x, y) => bands[(x * bands.Length) / 12]);

            var doc = conv.Convert(src, profile);

            var distinct = new HashSet<string>();
            foreach (var cell in doc.Grid.Cells) distinct.Add(((YAKWoolCell)cell).ColorId);
            Assert.LessOrEqual(distinct.Count, 3, "distinct colors must not exceed the cap");
            UnityEngine.Object.DestroyImmediate(src);
        }

        [Test]
        public void Convert_SubjectDiffersFromBackground()
        {
            var profile = MakeProfile(12, 12, Pal());
            var conv = MakeConverter(cap: 6);
            var src = SubjectOnBackground(12, 12); // grey field, red center block

            var doc = conv.Convert(src, profile);

            string center = ((YAKWoolCell)doc.Grid.Get(6, 6)).ColorId;
            string corner = ((YAKWoolCell)doc.Grid.Get(0, 0)).ColorId;
            Assert.AreNotEqual(corner, center, "subject (center) and background (corner) must differ");
            UnityEngine.Object.DestroyImmediate(src);
        }

        // ── Task 4a: BackgroundFill.Empty ─────────────────────────────────────

        [Test]
        public void Convert_BackgroundEmpty_CornerIsEmptyCell_CenterIsWool()
        {
            // Task 4a: BackgroundFill.Empty emits YAKEmptyCell for background,
            // YAKWoolCell for subject.
            var profile = MakeProfile(12, 12, Pal());
            var conv = MakeConverter(cap: 6, background: YAKImageToGrid.BackgroundFill.Empty);
            var src = SubjectOnBackground(12, 12);

            var doc = conv.Convert(src, profile);

            // Corner (0,0) is background → must be empty.
            Assert.IsInstanceOf<YAKEmptyCell>(doc.Grid.Get(0, 0),
                "background corner must be YAKEmptyCell in Empty mode");

            // Center (6,6) is subject → must be wool with a real color.
            var centerCell = doc.Grid.Get(6, 6);
            Assert.IsInstanceOf<YAKWoolCell>(centerCell,
                "subject center must be YAKWoolCell in Empty mode");
            Assert.IsFalse(string.IsNullOrEmpty(((YAKWoolCell)centerCell).ColorId));
            UnityEngine.Object.DestroyImmediate(src);
        }

        [Test]
        public void Convert_BackgroundFillNeutral_NoEmpties_RegressionGuard()
        {
            // Task 4a regression: default FillNeutral behavior must produce zero empty cells.
            var profile = MakeProfile(12, 12, Pal());
            var conv = MakeConverter(cap: 6, background: YAKImageToGrid.BackgroundFill.FillNeutral);
            var src = SubjectOnBackground(12, 12);

            var doc = conv.Convert(src, profile);

            foreach (var cell in doc.Grid.Cells)
                Assert.IsInstanceOf<YAKWoolCell>(cell,
                    "FillNeutral mode must produce zero empty cells");
            UnityEngine.Object.DestroyImmediate(src);
        }

        // ── Task 4b: OutlineSubject ────────────────────────────────────────────

        [Test]
        public void Convert_OutlineSubject_FillNeutral_OutlineRingIsBlack_CornerIsNeutralWool()
        {
            // Task 4b (FillNeutral mode): bg cells adjacent to subject become Black wool;
            // far corner stays a neutral wool (not empty); subject interior unchanged (not Black).
            var profile = MakeProfile(12, 12, Pal());
            var conv = MakeConverter(cap: 6,
                background: YAKImageToGrid.BackgroundFill.FillNeutral,
                outlineSubject: true,
                outlineColorId: "Black");
            var src = SubjectOnBackground(12, 12);

            var doc = conv.Convert(src, profile);

            // Subject inner cell (6,6): should be wool, NOT Black.
            var innerCell = doc.Grid.Get(6, 6) as YAKWoolCell;
            Assert.IsNotNull(innerCell, "subject interior must be YAKWoolCell");
            Assert.AreNotEqual("Black", innerCell.ColorId,
                "subject interior must not be overwritten by outline color");

            // Outline cell (3,6): bg adjacent to subject at (4,6) → must be Black wool.
            // SubjectOnBackground: subject is x=[4..7], y=[4..7] in a 12x12 grid.
            var outlineCell = doc.Grid.Get(3, 6) as YAKWoolCell;
            Assert.IsNotNull(outlineCell, "outline cell must be YAKWoolCell");
            Assert.AreEqual("Black", outlineCell.ColorId,
                "outline ring cell must have the outline ColorId (Black)");

            // Far corner (0,0): bg, not adjacent to subject → neutral wool (not Black, not empty).
            var cornerCell = doc.Grid.Get(0, 0) as YAKWoolCell;
            Assert.IsNotNull(cornerCell, "FillNeutral corner must be YAKWoolCell (not empty)");
            Assert.AreNotEqual("Black", cornerCell.ColorId,
                "non-outline corner must not be painted Black in FillNeutral mode");
            UnityEngine.Object.DestroyImmediate(src);
        }

        [Test]
        public void Convert_OutlineSubject_Empty_OutlineSurvivesAsBlack_NonOutlineBgIsEmpty()
        {
            // Task 4b + 4a combined (step-ordering proof):
            // outline mask is computed BEFORE bg mutation, so:
            //   • outline cells → Black YAKWoolCell
            //   • non-outline bg cells → YAKEmptyCell
            var profile = MakeProfile(12, 12, Pal());
            var conv = MakeConverter(cap: 6,
                background: YAKImageToGrid.BackgroundFill.Empty,
                outlineSubject: true,
                outlineColorId: "Black");
            var src = SubjectOnBackground(12, 12);

            var doc = conv.Convert(src, profile);

            // Outline cell (3,6): bg adjacent to subject → must be Black wool.
            var outlineCell = doc.Grid.Get(3, 6) as YAKWoolCell;
            Assert.IsNotNull(outlineCell, "outline cell must be YAKWoolCell");
            Assert.AreEqual("Black", outlineCell.ColorId,
                "outline cell must be Black in Empty+Outline mode");

            // Far corner (0,0): non-outline bg → must be empty.
            Assert.IsInstanceOf<YAKEmptyCell>(doc.Grid.Get(0, 0),
                "non-outline bg corner must be YAKEmptyCell in Empty+Outline mode");

            // Subject center (6,6): must be wool (not black, not empty).
            var subjectCell = doc.Grid.Get(6, 6) as YAKWoolCell;
            Assert.IsNotNull(subjectCell, "subject interior must be YAKWoolCell");
            Assert.AreNotEqual("Black", subjectCell.ColorId);
            UnityEngine.Object.DestroyImmediate(src);
        }

        [Test]
        public void Convert_OutlineSubject_PaletteMissingBlack_FallsBackToDarkestColor_NoThrow()
        {
            // Task 4b fallback: when OutlineColorId ("Black") is absent from the palette,
            // the darkest available color is used and a warning is logged (no exception thrown).
            // With palette {Red, Green, Blue, Grey, White} the darkest by luminance is Blue
            // (Lum ≈ 0.0722).
            var palNoBlack = PalWithout("Black");
            var profile = MakeProfile(12, 12, palNoBlack);
            var conv = MakeConverter(cap: 6,
                background: YAKImageToGrid.BackgroundFill.Empty,
                outlineSubject: true,
                outlineColorId: "Black");
            var src = SubjectOnBackground(12, 12);

            LevelDocument doc = null;
            Assert.DoesNotThrow(() => doc = conv.Convert(src, profile),
                "Convert must not throw when outline color is missing from palette");

            // Outline cell (3,6) should be the darkest fallback (Blue), not null/empty.
            var outlineCell = doc.Grid.Get(3, 6) as YAKWoolCell;
            Assert.IsNotNull(outlineCell,
                "outline cell must still be YAKWoolCell even with missing-Black fallback");
            Assert.AreEqual("Blue", outlineCell.ColorId,
                "darkest fallback in {Red,Green,Blue,Grey,White} is Blue (Lum ≈ 0.0722)");
            UnityEngine.Object.DestroyImmediate(src);
        }

        [Test]
        public void Convert_ColorCap_RespectedWithOutlineOn_BlackSurvivesAtTightCap()
        {
            // Task 4b: Black (outline) counts toward ColorCap but is PROTECTED from merge.
            // Use a 5-color subject (colored vertical bands inside a grey border) + Black outline.
            // cap=4 forces merging of subject colors; Black must survive.
            var profile = MakeProfile(12, 12, Pal());
            var conv = MakeConverter(cap: 4,
                background: YAKImageToGrid.BackgroundFill.Empty,
                outlineSubject: true,
                outlineColorId: "Black");

            // Subject: x=[2..9] has 4 color bands; x=[0..1] and x=[10..11] are grey bg.
            var subjectColors = new[] { Color.red, Color.green, Color.blue, Color.white };
            var src = MakeTexture(12, 12, (x, y) =>
            {
                bool isSubject = x >= 2 && x < 10;
                if (!isSubject) return new Color(0.5f, 0.5f, 0.5f);
                return subjectColors[((x - 2) * subjectColors.Length) / 8];
            });

            var doc = conv.Convert(src, profile);

            var distinct = new HashSet<string>();
            foreach (var cell in doc.Grid.Cells)
                if (cell is YAKWoolCell w) distinct.Add(w.ColorId);

            Assert.LessOrEqual(distinct.Count, 4,
                "distinct non-empty colors must not exceed the cap");
            Assert.IsTrue(distinct.Contains("Black"),
                "Black outline must survive even at a tight cap (protected from merge)");
            UnityEngine.Object.DestroyImmediate(src);
        }

        [Test]
        public void Convert_IsDeterministic_WithOutlineAndEmptyMode()
        {
            // Task 4b + determinism: two identical runs with OutlineSubject+Empty must produce
            // identical output (no randomness from outline mask or empty-cell emission).
            var profile = MakeProfile(12, 12, Pal());
            var conv = MakeConverter(cap: 4,
                background: YAKImageToGrid.BackgroundFill.Empty,
                outlineSubject: true,
                outlineColorId: "Black");
            var src = SubjectOnBackground(12, 12);

            var a = conv.Convert(src, profile);
            var b = conv.Convert(src, profile);

            Assert.AreEqual(a.Grid.Cells.Length, b.Grid.Cells.Length);
            for (int i = 0; i < a.Grid.Cells.Length; i++)
            {
                bool aEmpty = a.Grid.Cells[i] is YAKEmptyCell;
                bool bEmpty = b.Grid.Cells[i] is YAKEmptyCell;
                Assert.AreEqual(aEmpty, bEmpty, $"cell {i} empty-ness differs between runs");
                if (!aEmpty)
                    Assert.AreEqual(((YAKWoolCell)a.Grid.Cells[i]).ColorId,
                                    ((YAKWoolCell)b.Grid.Cells[i]).ColorId,
                                    $"cell {i} ColorId differs between runs");
            }
            UnityEngine.Object.DestroyImmediate(src);
        }

        // ── Builders ──────────────────────────────────────────────────────────

        private static ColorPaletteAsset Pal()
        {
            var entries = new List<ColorEntry>
            {
                new ColorEntry { Id = "Red",   DisplayName = "Red",   Color = Color.red },
                new ColorEntry { Id = "Green", DisplayName = "Green", Color = Color.green },
                new ColorEntry { Id = "Blue",  DisplayName = "Blue",  Color = Color.blue },
                new ColorEntry { Id = "Grey",  DisplayName = "Grey",  Color = new Color(0.5f, 0.5f, 0.5f) },
                new ColorEntry { Id = "White", DisplayName = "White", Color = Color.white },
                new ColorEntry { Id = "Black", DisplayName = "Black", Color = Color.black },
            };
            var pal = ScriptableObject.CreateInstance<ColorPaletteAsset>();
            typeof(ColorPaletteAsset).GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(pal, entries);
            return pal;
        }

        // Full palette minus one named entry (for fallback tests).
        private static ColorPaletteAsset PalWithout(string excludeId)
        {
            var entries = new List<ColorEntry>
            {
                new ColorEntry { Id = "Red",   DisplayName = "Red",   Color = Color.red },
                new ColorEntry { Id = "Green", DisplayName = "Green", Color = Color.green },
                new ColorEntry { Id = "Blue",  DisplayName = "Blue",  Color = Color.blue },
                new ColorEntry { Id = "Grey",  DisplayName = "Grey",  Color = new Color(0.5f, 0.5f, 0.5f) },
                new ColorEntry { Id = "White", DisplayName = "White", Color = Color.white },
                new ColorEntry { Id = "Black", DisplayName = "Black", Color = Color.black },
            };
            entries.RemoveAll(e => string.Equals(e.Id, excludeId, StringComparison.Ordinal));
            var pal = ScriptableObject.CreateInstance<ColorPaletteAsset>();
            typeof(ColorPaletteAsset).GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(pal, entries);
            return pal;
        }

        private static GameProfile MakeProfile(int w, int h, ColorPaletteAsset pal)
        {
            var p = ScriptableObject.CreateInstance<GameProfile>();
            var so = new UnityEditor.SerializedObject(p);
            so.FindProperty("_colorPalette").objectReferenceValue = pal;
            so.FindProperty("_gridWidth").intValue = w;
            so.FindProperty("_gridHeight").intValue = h;
            so.FindProperty("_schemaId").stringValue = "yak";
            so.ApplyModifiedPropertiesWithoutUndo();
            return p;
        }

        private static YAKImageToGrid MakeConverter(
            int cap,
            YAKImageToGrid.BackgroundFill background = YAKImageToGrid.BackgroundFill.FillNeutral,
            bool outlineSubject = false,
            string outlineColorId = "Black")
        {
            var c = ScriptableObject.CreateInstance<YAKImageToGrid>();
            c.ColorCap = cap;
            c.BackgroundNeutrals = new[] { "Grey", "White", "Black" };
            c.Segmentation = YAKImageToGrid.SegmentationMode.BorderRing;
            c.Background = background;
            c.OutlineSubject = outlineSubject;
            c.OutlineColorId = outlineColorId;
            return c;
        }

        private static Texture2D MakeTexture(int w, int h, Func<int, int, Color> at)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = at(x, y);
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        // Grey field with a saturated red square in the middle (clear subject/bg).
        // Subject: x in [w/3 .. w-w/3), y in [h/3 .. h-h/3). For 12x12: x=[4..7], y=[4..7].
        private static Texture2D SubjectOnBackground(int w, int h)
            => MakeTexture(w, h, (x, y) =>
            {
                bool inner = x >= w / 3 && x < w - w / 3 && y >= h / 3 && y < h - h / 3;
                return inner ? Color.red : new Color(0.5f, 0.5f, 0.5f);
            });
    }
}
