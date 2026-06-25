using System.IO;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YAK.Editor;

namespace Hoppa.YAK.Editor.Tests
{
    public sealed class YakCurveBatchTests
    {
        [Test]
        public void RunCurve_WritesNumberedLevels_WithTierStats()
        {
            // Minimal real profile: palette + generator + completer(autofiller) +
            // analyzer + imageToGrid (procedural fallback path, UseImageSource=false).
            var profile = TestProfiles.MakeYakProfile();
            var curve = ScriptableObject.CreateInstance<YAKDifficultyCurveConfig>();
            curve.Presets = new List<TierPreset> {
                new TierPreset { Name="Tiny", GridWidth=6, GridHeight=6, MaxColors=2, AvgCapacity=12,
                                 ConveyorSlots=6, ColumnRange=new Vector2Int(2,3), TargetAps=2f, ApsTolerance=5f }, // wide tol → accepts fast
            };
            curve.Curve = new List<CurveSegment> { new CurveSegment { TierName="Tiny", LevelCount=2 } };

            string root = Path.Combine(Path.GetTempPath(), "yak_curve_test_" + System.Guid.NewGuid().ToString("N"));
            string dir = YAKCurveBatchHarness.RunCurve(curve, profile, attemptsPerLevel: 8, stagingRoot: root);

            Assert.IsNotNull(dir);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "level_1.json")), "level_1.json written");
            Assert.IsTrue(File.Exists(Path.Combine(dir, "level_2.json")), "level_2.json written");
            string statsPath = Path.Combine(dir, "level_1" + Hoppa.LevelEditor.Core.Editor.BatchStaging.StatsSuffix);
            Assert.IsTrue(File.Exists(statsPath), "stats written");
            StringAssert.Contains("Tiny", File.ReadAllText(statsPath), "stats carry tier name");

            Directory.Delete(dir, true);
        }

        [Test]
        public void RunCurve_EmptyCurve_ReturnsNull()
        {
            // The harness logs an error explaining the invalid curve; that is expected
            // here (it is the contract for returning null), so don't fail the test on it.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var profile = TestProfiles.MakeYakProfile();
            var curve = ScriptableObject.CreateInstance<YAKDifficultyCurveConfig>();
            Assert.IsNull(YAKCurveBatchHarness.RunCurve(curve, profile, 4, Path.GetTempPath()));
        }
    }
}
