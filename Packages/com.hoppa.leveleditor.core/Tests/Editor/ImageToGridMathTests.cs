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
    }
}
