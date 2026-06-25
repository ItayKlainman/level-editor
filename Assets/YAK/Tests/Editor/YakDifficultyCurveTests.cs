using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Hoppa.YAK.Editor;

namespace Hoppa.YAK.Editor.Tests
{
    public sealed class YakDifficultyCurveTests
    {
        private static YAKDifficultyCurveConfig MakeConfig()
        {
            var c = ScriptableObject.CreateInstance<YAKDifficultyCurveConfig>();
            c.Presets = new List<TierPreset>
            {
                new TierPreset { Name = "Tutorial", GridWidth = 8,  GridHeight = 8,  MaxColors = 2, AvgCapacity = 30, ColumnRange = new Vector2Int(2,3), TargetAps = 1.5f, ApsTolerance = 0.6f },
                new TierPreset { Name = "Easy",     GridWidth = 12, GridHeight = 12, MaxColors = 3, AvgCapacity = 25, ColumnRange = new Vector2Int(2,4), TargetAps = 2.5f, ApsTolerance = 0.6f },
                new TierPreset { Name = "Medium",   GridWidth = 20, GridHeight = 20, MaxColors = 4, AvgCapacity = 20, ColumnRange = new Vector2Int(3,5), TargetAps = 4f,   ApsTolerance = 0.6f },
            };
            c.Curve = new List<CurveSegment>
            {
                new CurveSegment { TierName = "Tutorial", LevelCount = 5 },
                new CurveSegment { TierName = "Easy",     LevelCount = 10 },
                new CurveSegment { TierName = "Medium",   LevelCount = 15 },
            };
            return c;
        }

        [Test]
        public void TotalLevels_SumsSegments()
        {
            Assert.AreEqual(30, MakeConfig().TotalLevels());
        }

        [Test]
        public void TierForLevel_MapsRangesAndBoundaries()
        {
            var c = MakeConfig();
            Assert.AreEqual("Tutorial", c.TierForLevel(1).Name);
            Assert.AreEqual("Tutorial", c.TierForLevel(5).Name);   // last tutorial
            Assert.AreEqual("Easy",     c.TierForLevel(6).Name);   // first easy
            Assert.AreEqual("Easy",     c.TierForLevel(15).Name);
            Assert.AreEqual("Medium",   c.TierForLevel(16).Name);
            Assert.AreEqual("Medium",   c.TierForLevel(30).Name);
        }

        [Test]
        public void TierForLevel_OutOfRange_ClampsToLastTier()
        {
            var c = MakeConfig();
            Assert.AreEqual("Medium", c.TierForLevel(99).Name); // beyond total → last
            Assert.AreEqual("Tutorial", c.TierForLevel(0).Name); // below 1 → first
        }

        [Test]
        public void Duplicate_ProducesIndependentCopy()
        {
            var c = MakeConfig();
            var dup = c.Duplicate(0);
            Assert.AreEqual(4, c.Presets.Count);
            Assert.AreNotSame(c.Presets[0], dup);
            dup.GridWidth = 999;
            Assert.AreEqual(8, c.Presets[0].GridWidth, "editing the copy must not touch the original");
        }

        [Test]
        public void Validate_FlagsDanglingTierAndEmpties()
        {
            var c = MakeConfig();
            Assert.IsEmpty(c.Validate());
            c.Curve.Add(new CurveSegment { TierName = "DoesNotExist", LevelCount = 3 });
            CollectionAssert.IsNotEmpty(c.Validate());

            var empty = ScriptableObject.CreateInstance<YAKDifficultyCurveConfig>();
            CollectionAssert.IsNotEmpty(empty.Validate());
        }

        [Test]
        public void Summary_DescribesRamp()
        {
            StringAssert.Contains("30 levels", MakeConfig().Summary());
            StringAssert.Contains("Tutorial", MakeConfig().Summary());
        }
    }
}
