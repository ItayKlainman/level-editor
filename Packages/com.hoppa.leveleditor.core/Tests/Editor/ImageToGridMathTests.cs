using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Hoppa.LevelEditor.Core.Editor;

namespace Hoppa.LevelEditor.Core.EditorTests
{
    public class ImageToGridMathTests
    {
        static List<PaletteColor> Palette() => new List<PaletteColor>
        {
            new PaletteColor { Id = "Red",    C = Color.red },
            new PaletteColor { Id = "Green",  C = Color.green },
            new PaletteColor { Id = "Blue",   C = Color.blue },
            new PaletteColor { Id = "Yellow", C = new Color(1f, 1f, 0f) },
        };

        [Test]
        public void RedmeanDist_IdenticalColors_IsZero()
        {
            Assert.AreEqual(0.0, ImageToGridMath.RedmeanDist(Color.red, Color.red), 1e-6);
        }

        [Test]
        public void RedmeanDist_IsSymmetric()
        {
            var a = new Color(0.2f, 0.6f, 0.1f);
            var b = new Color(0.9f, 0.8f, 0.0f);
            Assert.AreEqual(ImageToGridMath.RedmeanDist(a, b), ImageToGridMath.RedmeanDist(b, a), 1e-6);
        }

        [Test]
        public void NearestId_PicksClosestPaletteColor()
        {
            // Pure green pixel → "Green".
            Assert.AreEqual("Green", ImageToGridMath.NearestId(new Color(0.05f, 0.9f, 0.05f), Palette()));
        }

        [Test]
        public void Downscale_AreaAverage_AveragesRegion()
        {
            // 2x2 source: two white, two black → single cell = mid-grey.
            var src = new[] { Color.white, Color.black, Color.black, Color.white };
            var outp = ImageToGridMath.Downscale(src, 2, 2, 1, 1, SampleMode.AreaAverage, Palette());
            Assert.AreEqual(1, outp.Length);
            Assert.AreEqual(0.5f, outp[0].r, 1e-4);
            Assert.AreEqual(0.5f, outp[0].g, 1e-4);
        }

        [Test]
        public void ComputeOutlineMask_MarksBgAdjacentToSubject()
        {
            // 3x1: bg, subject, bg  → both bg cells border the subject.
            var isBg = new[] { true, false, true };
            var mask = ImageToGridMath.ComputeOutlineMask(isBg, 3, 1);
            Assert.IsTrue(mask[0]);
            Assert.IsFalse(mask[1]); // subject cells are never outline
            Assert.IsTrue(mask[2]);
        }

        [Test]
        public void Downscale_Dominant_PicksMajorityColorIgnoringMinority()
        {
            // One cell over a 5x1 region: 3 green pixels, 2 red pixels → green wins,
            // and the returned color is the mean of the GREEN pixels (not muddied by red).
            var g = new Color(0f, 0.8f, 0f);
            var r = new Color(0.9f, 0f, 0f);
            var src = new[] { g, g, g, r, r };
            var outp = ImageToGridMath.Downscale(src, 5, 1, 1, 1, SampleMode.Dominant, Palette());
            Assert.AreEqual("Green", ImageToGridMath.NearestId(outp[0], Palette()));
            Assert.AreEqual(0f, outp[0].r, 1e-4, "red minority must not bleed in");
            Assert.AreEqual(0.8f, outp[0].g, 1e-4);
        }

        [Test]
        public void Downscale_Dominant_TieBreaksToLowestPaletteIndex()
        {
            // 1 red + 1 green, palette order Red(0) before Green(1) → tie → Red.
            var src = new[] { new Color(0.9f, 0f, 0f), new Color(0f, 0.8f, 0f) };
            var outp = ImageToGridMath.Downscale(src, 2, 1, 1, 1, SampleMode.Dominant, Palette());
            Assert.AreEqual("Red", ImageToGridMath.NearestId(outp[0], Palette()));
        }

        [Test]
        public void NormalizedDistance_IsZeroForSame_AndOneForBlackWhite()
        {
            Assert.AreEqual(0f, ImageToGridMath.NormalizedDistance(Color.green, Color.green), 1e-4);
            Assert.AreEqual(1f, ImageToGridMath.NormalizedDistance(Color.black, Color.white), 1e-3);
        }

        [Test]
        public void ResolveRemap_InReach_ReturnsTarget()
        {
            var remaps = new List<ColorRemap> {
                new ColorRemap { Source = new Color(0.7f, 0.85f, 0.15f), TargetColorId = "Green", Reach = 0.3f },
            };
            // A lime-ish cell close to the source → forced to Green.
            var id = ImageToGridMath.ResolveRemap(new Color(0.72f, 0.83f, 0.18f), remaps, Palette());
            Assert.AreEqual("Green", id);
        }

        [Test]
        public void ResolveRemap_OutOfReach_ReturnsNull()
        {
            var remaps = new List<ColorRemap> {
                new ColorRemap { Source = new Color(0.7f, 0.85f, 0.15f), TargetColorId = "Green", Reach = 0.02f },
            };
            Assert.IsNull(ImageToGridMath.ResolveRemap(Color.blue, remaps, Palette()));
        }

        [Test]
        public void ResolveRemap_ClosestSourceWins()
        {
            var remaps = new List<ColorRemap> {
                new ColorRemap { Source = Color.red,   TargetColorId = "Red",   Reach = 1f },
                new ColorRemap { Source = Color.green, TargetColorId = "Green", Reach = 1f },
            };
            Assert.AreEqual("Green", ImageToGridMath.ResolveRemap(new Color(0.1f, 0.8f, 0.1f), remaps, Palette()));
        }

        [Test]
        public void ResolveRemap_TargetNotInPalette_IsIgnored()
        {
            var remaps = new List<ColorRemap> {
                new ColorRemap { Source = Color.green, TargetColorId = "Magenta", Reach = 1f },
            };
            Assert.IsNull(ImageToGridMath.ResolveRemap(Color.green, remaps, Palette()));
        }

        [Test]
        public void ResolveRemap_NullOrEmpty_ReturnsNull()
        {
            Assert.IsNull(ImageToGridMath.ResolveRemap(Color.green, null, Palette()));
            Assert.IsNull(ImageToGridMath.ResolveRemap(Color.green, new List<ColorRemap>(), Palette()));
        }

        [Test]
        public void ResolveRemap_EquidistantSources_EarliestEntryWins()
        {
            // Two remaps with identical Sources (Color.red) equidistant from a red cell.
            // Distances tie at 0; the tie must resolve to the EARLIEST entry ("Red").
            var remaps = new List<ColorRemap> {
                new ColorRemap { Source = Color.red, TargetColorId = "Red",   Reach = 1f },
                new ColorRemap { Source = Color.red, TargetColorId = "Green", Reach = 1f },
            };
            Assert.AreEqual("Red", ImageToGridMath.ResolveRemap(Color.red, remaps, Palette()));
        }

        // ── Characterization: MergeToCap ───────────────────────────────────────
        [Test]
        public void MergeToCap_CollapsesToCap_ButKeepsProtectedRareColor()
        {
            // 12 cells over 4 distinct colors; a lone "Yellow" is the rare protected color.
            // Two trailing cells are masked empty (excluded from the count, never merged).
            var ids = new[]
            {
                "Red", "Red", "Red", "Red",   // 4
                "Green", "Green", "Green",     // 3
                "Blue", "Blue",                // 2
                "Yellow",                      // 1  (protected, rare)
                "Empty", "Empty",              // masked out
            };
            var isEmpty = new[]
            {
                false, false, false, false,
                false, false, false,
                false, false,
                false,
                true, true,
            };

            ImageToGridMath.MergeToCap(ids, Palette(), cap: 2, isEmpty: isEmpty, protectedId: "Yellow");

            var distinctNonEmpty = new HashSet<string>();
            for (int i = 0; i < ids.Length; i++)
                if (!isEmpty[i]) distinctNonEmpty.Add(ids[i]);

            Assert.LessOrEqual(distinctNonEmpty.Count, 2, "distinct non-empty colors must collapse to the cap");
            Assert.IsTrue(distinctNonEmpty.Contains("Yellow"), "the protected rare color must survive the merge");
            Assert.AreEqual("Empty", ids[10], "masked-empty cells must be left untouched");
            Assert.AreEqual("Empty", ids[11], "masked-empty cells must be left untouched");
        }

        // ── Characterization: BorderRing ───────────────────────────────────────
        [Test]
        public void BorderRing_FlagsEdgeConnectedBackground_NotDisconnectedInteriorIsland()
        {
            // 5x5: red frame (dominant border color) with a green interior, plus a lone
            // red pixel at the center. The center red is NOT connected to the border
            // through red cells, so it must NOT be flagged background.
            const int W = 5, H = 5;
            var avg = new Color[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                bool onBorder = x == 0 || y == 0 || x == W - 1 || y == H - 1;
                avg[y * W + x] = onBorder ? Color.red : Color.green;
            }
            int center = 2 * W + 2;
            avg[center] = Color.red; // isolated red island, walled off by green

            var bg = ImageToGridMath.BorderRing(avg, W, H, Palette());

            Assert.IsTrue(bg[0], "top-left border cell is edge-connected background");
            Assert.IsTrue(bg[W - 1], "top-right border cell is edge-connected background");
            Assert.IsFalse(bg[center], "disconnected interior red island must NOT be background");
            Assert.IsFalse(bg[1 * W + 2], "green interior cell must NOT be background");
        }

        // ── Characterization: BySaturation ─────────────────────────────────────
        [Test]
        public void BySaturation_ClassifiesLowSaturationAsBackground_NotHighSaturation()
        {
            // Two greys (saturation 0) and two vivid colors (saturation 1).
            // Median lands on the vivid end, so greys fall below it → background;
            // vivid colors are at/above the median → not background.
            var grey = new Color(0.5f, 0.5f, 0.5f);
            var avg = new[] { grey, grey, Color.red, Color.green };

            var bg = ImageToGridMath.BySaturation(avg, 4, 1);

            Assert.IsTrue(bg[0], "low-saturation grey must be classified background");
            Assert.IsTrue(bg[1], "low-saturation grey must be classified background");
            Assert.IsFalse(bg[2], "high-saturation red must NOT be background");
            Assert.IsFalse(bg[3], "high-saturation green must NOT be background");
        }
    }
}
