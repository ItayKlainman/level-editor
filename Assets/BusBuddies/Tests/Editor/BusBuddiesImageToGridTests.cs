using System;
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
    // Bus Buddies image→grid (sub-phase 1c, Task 1). Mirrors YakImageToGridTests but
    // asserts the Bus-Buddies-specific defaults: Background = Empty and
    // OutlineSubject = true, emitting BBPixelCell for subject/outline and BBEmptyCell
    // for non-outline background. Hermetic — synthetic palette + in-memory profile,
    // schemaId = "busbuddies".
    public sealed class BusBuddiesImageToGridTests
    {
        // ── Grid shape / profile sizing ───────────────────────────────────────

        [Test]
        public void Convert_ProducesProfileSizedGrid()
        {
            var profile = MakeProfile(8, 6, Pal());
            var conv = MakeConverter(cap: 6);
            var src = MakeTexture(8, 6, (x, y) => Color.red);

            var doc = conv.Convert(src, profile);

            Assert.AreEqual(8, doc.Grid.Width);
            Assert.AreEqual(6, doc.Grid.Height);
            UnityEngine.Object.DestroyImmediate(src);
        }

        [Test]
        public void Convert_StampsConveyorCount_FromDefaultActiveSlots()
        {
            var profile = MakeProfile(8, 6, Pal());
            var conv = MakeConverter(cap: 6);
            conv.DefaultActiveSlots = 7;
            var src = MakeTexture(8, 6, (x, y) => Color.red);

            var doc = conv.Convert(src, profile);

            Assert.IsNotNull(doc.GameData);
            Assert.AreEqual(7, (int)doc.GameData["conveyorCount"]);
            UnityEngine.Object.DestroyImmediate(src);
        }

        // ── Default Background = Empty ─────────────────────────────────────────

        [Test]
        public void Convert_DefaultCornerIsEmpty_CenterIsPixel()
        {
            // BB defaults: Background = Empty, OutlineSubject = true.
            // Corner (0,0) is far background → BBEmptyCell.
            // Center (6,6) is subject → BBPixelCell with a real color.
            var profile = MakeProfile(12, 12, Pal());
            var conv = MakeConverter(cap: 6);
            var src = SubjectOnBackground(12, 12);

            var doc = conv.Convert(src, profile);

            Assert.IsInstanceOf<BBEmptyCell>(doc.Grid.Get(0, 0),
                "far background corner must be BBEmptyCell by default");

            var centerCell = doc.Grid.Get(6, 6);
            Assert.IsInstanceOf<BBPixelCell>(centerCell,
                "subject center must be BBPixelCell");
            Assert.IsFalse(string.IsNullOrEmpty(((BBPixelCell)centerCell).ColorId));
            UnityEngine.Object.DestroyImmediate(src);
        }

        // ── Outline ring ──────────────────────────────────────────────────────

        [Test]
        public void Convert_OutlineRingIsBlackPixel_NonOutlineBgIsEmpty()
        {
            // Outline mask is computed BEFORE bg mutation:
            //   • outline cells → Black BBPixelCell
            //   • non-outline bg cells → BBEmptyCell
            //   • subject interior → non-Black BBPixelCell
            var profile = MakeProfile(12, 12, Pal());
            var conv = MakeConverter(cap: 6, outlineColorId: "Black");
            var src = SubjectOnBackground(12, 12);

            var doc = conv.Convert(src, profile);

            // Outline cell (3,6): bg adjacent to subject at (4,6) → Black pixel.
            var outlineCell = doc.Grid.Get(3, 6) as BBPixelCell;
            Assert.IsNotNull(outlineCell, "outline cell must be BBPixelCell");
            Assert.AreEqual("Black", outlineCell.ColorId,
                "outline ring cell must have the outline ColorId (Black)");

            // Far corner (0,0): non-outline bg → empty.
            Assert.IsInstanceOf<BBEmptyCell>(doc.Grid.Get(0, 0),
                "non-outline bg corner must be BBEmptyCell");

            // Subject center (6,6): pixel, not Black.
            var subjectCell = doc.Grid.Get(6, 6) as BBPixelCell;
            Assert.IsNotNull(subjectCell, "subject interior must be BBPixelCell");
            Assert.AreNotEqual("Black", subjectCell.ColorId,
                "subject interior must not be overwritten by outline color");
            UnityEngine.Object.DestroyImmediate(src);
        }

        // ── Color cap (BBPixelCell colors only) ───────────────────────────────

        [Test]
        public void Convert_RespectsColorCap_CountingPixelColorsOnly()
        {
            var profile = MakeProfile(12, 12, Pal());
            var conv = MakeConverter(cap: 4, outlineColorId: "Black");

            // Subject x=[2..9]: 4 color bands; x=[0..1],[10..11] grey bg.
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
                if (cell is BBPixelCell p) distinct.Add(p.ColorId);

            Assert.LessOrEqual(distinct.Count, 4,
                "distinct pixel colors must not exceed the cap");
            Assert.IsTrue(distinct.Contains("Black"),
                "Black outline must survive even at a tight cap (protected from merge)");
            UnityEngine.Object.DestroyImmediate(src);
        }

        // ── Determinism ───────────────────────────────────────────────────────

        [Test]
        public void Convert_IsDeterministic_ForSameImageAndConfig()
        {
            var profile = MakeProfile(12, 12, Pal());
            var conv = MakeConverter(cap: 4);
            var src = SubjectOnBackground(12, 12);

            var a = conv.Convert(src, profile);
            var b = conv.Convert(src, profile);

            Assert.AreEqual(a.Grid.Cells.Length, b.Grid.Cells.Length);
            for (int i = 0; i < a.Grid.Cells.Length; i++)
            {
                bool aEmpty = a.Grid.Cells[i] is BBEmptyCell;
                bool bEmpty = b.Grid.Cells[i] is BBEmptyCell;
                Assert.AreEqual(aEmpty, bEmpty, $"cell {i} empty-ness differs between runs");
                if (!aEmpty)
                    Assert.AreEqual(((BBPixelCell)a.Grid.Cells[i]).ColorId,
                                    ((BBPixelCell)b.Grid.Cells[i]).ColorId,
                                    $"cell {i} ColorId differs between runs");
            }
            UnityEngine.Object.DestroyImmediate(src);
        }

        // ── Subject differs from background ───────────────────────────────────

        [Test]
        public void Convert_SubjectDiffersFromBackground()
        {
            var profile = MakeProfile(12, 12, Pal());
            var conv = MakeConverter(cap: 6);
            var src = SubjectOnBackground(12, 12);

            var doc = conv.Convert(src, profile);

            var center = doc.Grid.Get(6, 6) as BBPixelCell;
            Assert.IsNotNull(center, "subject center must be a pixel");
            // Corner is empty by default; subject pixel must carry a real color id.
            Assert.IsFalse(string.IsNullOrEmpty(center.ColorId));
            UnityEngine.Object.DestroyImmediate(src);
        }

        // ── Outline fallback when palette lacks Black ─────────────────────────

        [Test]
        public void Convert_OutlinePaletteMissingBlack_FallsBackToDarkest_NoThrow()
        {
            // Palette {Red,Green,Blue,Grey,White}: darkest by luminance is Blue.
            var palNoBlack = PalWithout("Black");
            var profile = MakeProfile(12, 12, palNoBlack);
            var conv = MakeConverter(cap: 6, outlineColorId: "Black");
            var src = SubjectOnBackground(12, 12);

            LevelDocument doc = null;
            Assert.DoesNotThrow(() => doc = conv.Convert(src, profile),
                "Convert must not throw when outline color is missing from palette");

            var outlineCell = doc.Grid.Get(3, 6) as BBPixelCell;
            Assert.IsNotNull(outlineCell,
                "outline cell must still be BBPixelCell with missing-Black fallback");
            Assert.AreEqual("Blue", outlineCell.ColorId,
                "darkest fallback in {Red,Green,Blue,Grey,White} is Blue (Lum ≈ 0.0722)");
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
            so.FindProperty("_schemaId").stringValue = "busbuddies";
            so.ApplyModifiedPropertiesWithoutUndo();
            return p;
        }

        private static BusBuddiesImageToGrid MakeConverter(
            int cap,
            string outlineColorId = "Black")
        {
            var c = ScriptableObject.CreateInstance<BusBuddiesImageToGrid>();
            c.ColorCap = cap;
            c.BackgroundNeutrals = new[] { "Grey", "White", "Black" };
            c.Segmentation = BusBuddiesImageToGrid.SegmentationMode.BorderRing;
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

        // Grey field with a red square in the middle. Subject: x,y in [w/3 .. w-w/3).
        private static Texture2D SubjectOnBackground(int w, int h)
            => MakeTexture(w, h, (x, y) =>
            {
                bool inner = x >= w / 3 && x < w - w / 3 && y >= h / 3 && y < h - h / 3;
                return inner ? Color.red : new Color(0.5f, 0.5f, 0.5f);
            });
    }
}
