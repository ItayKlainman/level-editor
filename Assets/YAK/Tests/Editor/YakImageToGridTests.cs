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
    // Phase C verification: YAKImageToGrid produces a profile-sized, all-wool,
    // palette-quantized, color-capped grid deterministically. Hermetic — uses a
    // synthetic palette + in-memory profile (no dependency on project wiring).
    public sealed class YakImageToGridTests
    {
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

        private static YAKImageToGrid MakeConverter(int cap)
        {
            var c = ScriptableObject.CreateInstance<YAKImageToGrid>();
            c.ColorCap = cap;
            c.BackgroundNeutrals = new[] { "Grey", "White", "Black" };
            c.Segmentation = YAKImageToGrid.SegmentationMode.BorderRing;
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
        private static Texture2D SubjectOnBackground(int w, int h)
            => MakeTexture(w, h, (x, y) =>
            {
                bool inner = x >= w / 3 && x < w - w / 3 && y >= h / 3 && y < h - h / 3;
                return inner ? Color.red : new Color(0.5f, 0.5f, 0.5f);
            });
    }
}
