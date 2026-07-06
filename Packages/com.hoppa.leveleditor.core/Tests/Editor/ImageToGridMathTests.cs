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
    }
}
