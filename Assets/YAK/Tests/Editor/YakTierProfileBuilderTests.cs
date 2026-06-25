using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YAK.Editor;

namespace Hoppa.YAK.Editor.Tests
{
    public sealed class YakTierProfileBuilderTests
    {
        private static void SetField(Object obj, string field, object value)
            => obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);
        private static T GetField<T>(Object obj, string field)
            => (T)obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj);

        private static GameProfile MakeBaseProfile(out YAKImageToGrid ig, out YAKSpoolAutofiller af, out YAKSpoolAutofillConfig afc, out YAKGeneratorConfig gc)
        {
            var profile = ScriptableObject.CreateInstance<GameProfile>();
            ig  = ScriptableObject.CreateInstance<YAKImageToGrid>();
            af  = ScriptableObject.CreateInstance<YAKSpoolAutofiller>();
            afc = ScriptableObject.CreateInstance<YAKSpoolAutofillConfig>();
            gc  = ScriptableObject.CreateInstance<YAKGeneratorConfig>();
            SetField(af, "_config", afc);
            SetField(profile, "_gridWidth", 30);
            SetField(profile, "_gridHeight", 30);
            SetField(profile, "_imageToGrid", ig);
            SetField(profile, "_levelCompleter", af);
            SetField(profile, "_generatorConfig", gc);
            return profile;
        }

        [Test]
        public void Build_AppliesTierKnobs_ToTransientProfile()
        {
            var baseProfile = MakeBaseProfile(out var ig, out var af, out var afc, out var gc);
            var tier = new TierPreset { Name="Easy", GridWidth=10, GridHeight=10, MaxColors=2,
                AvgCapacity=28, ConveyorSlots=7, ColumnRange=new Vector2Int(2,3),
                HiddenRatio=0.25f, TargetAps=1.5f, ApsTolerance=0.4f };

            var built = YAKTierProfileBuilder.Build(baseProfile, tier);
            try
            {
                Assert.AreEqual(10, built.Profile.GridWidth);
                Assert.AreEqual(10, built.Profile.GridHeight);
                var bIg = (YAKImageToGrid)built.Profile.ImageToGrid;
                Assert.AreEqual(2, bIg.ColorCap);
                Assert.AreEqual(7, bIg.DefaultConveyorCount);
                var bAfc = GetField<YAKSpoolAutofillConfig>(built.Profile.LevelCompleter, "_config");
                Assert.AreEqual(28, bAfc.AvgCapacity);
                Assert.AreEqual(7, bAfc.DefaultConveyorSlots);
                Assert.AreEqual(0.25f, bAfc.HiddenRatio);
                Assert.AreEqual(new Vector2Int(2,3), bAfc.ColumnRange);
                var bGc = (YAKGeneratorConfig)built.Profile.GeneratorConfig;
                Assert.AreEqual(1.5f, bGc.TargetAPS);
                Assert.AreEqual(7, bGc.ConveyorCount);
            }
            finally { built.Cleanup(); }
        }

        [Test]
        public void Build_DoesNotMutateOriginals()
        {
            var baseProfile = MakeBaseProfile(out var ig, out var af, out var afc, out var gc);
            ig.ColorCap = 6; afc.AvgCapacity = 20; gc.TargetAPS = 3f;
            var tier = new TierPreset { Name="X", GridWidth=8, GridHeight=8, MaxColors=2, AvgCapacity=30, ConveyorSlots=5, ColumnRange=new Vector2Int(2,3), TargetAps=1f, ApsTolerance=0.5f };

            var built = YAKTierProfileBuilder.Build(baseProfile, tier);
            built.Cleanup();

            Assert.AreEqual(6,  ig.ColorCap,      "original ImageToGrid must be untouched");
            Assert.AreEqual(20, afc.AvgCapacity,  "original autofill config must be untouched");
            Assert.AreEqual(3f, gc.TargetAPS,     "original generator config must be untouched");
            Assert.AreEqual(30, baseProfile.GridWidth, "original profile grid must be untouched");
        }
    }
}
