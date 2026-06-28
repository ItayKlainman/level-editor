using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Hoppa.LevelEditor.Core.Editor;
using Object = UnityEngine.Object;

namespace Hoppa.YAK.Editor
{
    /// A transient, tier-configured copy of a GameProfile. NEVER touches on-disk assets:
    /// every varied object is an Object.Instantiate clone. Call Cleanup() when done.
    public struct TierProfile
    {
        public GameProfile Profile;
        private List<Object> _owned;
        public TierProfile(GameProfile profile, List<Object> owned) { Profile = profile; _owned = owned; }
        public void Cleanup()
        {
            if (_owned == null) return;
            foreach (var o in _owned) if (o != null) Object.DestroyImmediate(o);
            _owned = null;
        }
    }

    public static class YAKTierProfileBuilder
    {
        private const BindingFlags FF = BindingFlags.NonPublic | BindingFlags.Instance;

        private static void Set(Object obj, string field, object value)
            => obj.GetType().GetField(field, FF)?.SetValue(obj, value);

        public static TierProfile Build(GameProfile baseProfile, TierPreset tier)
        {
            var owned = new List<Object>();

            var profile = Object.Instantiate(baseProfile);
            owned.Add(profile);

            // Grid size.
            Set(profile, "_gridWidth", Mathf.Max(1, tier.GridWidth));
            Set(profile, "_gridHeight", Mathf.Max(1, tier.GridHeight));

            // Image→grid clone: color cap + conveyor.
            if (baseProfile.ImageToGrid is YAKImageToGrid baseIg)
            {
                var ig = Object.Instantiate(baseIg);
                ig.ColorCap = Mathf.Max(2, tier.MaxColors);
                ig.DefaultConveyorCount = Mathf.Max(1, tier.ConveyorSlots);
                owned.Add(ig);
                Set(profile, "_imageToGrid", ig);
            }

            // Autofiller clone + its config clone: capacity, columns, conveyor, APS, hidden.
            if (baseProfile.LevelCompleter is YAKSpoolAutofiller baseAf)
            {
                var af = Object.Instantiate(baseAf);
                var baseCfg = af.GetType().GetField("_config", FF)?.GetValue(af) as YAKSpoolAutofillConfig;
                var cfg = baseCfg != null ? Object.Instantiate(baseCfg) : ScriptableObject.CreateInstance<YAKSpoolAutofillConfig>();
                int avg = Mathf.Max(1, tier.AvgCapacity);
                // Slack widens the [min,max] window around avg; 0 slack = tight band.
                int half = Mathf.Max(0, Mathf.RoundToInt(avg * Mathf.Clamp01(tier.CapacitySlack)));
                cfg.AvgCapacity = avg;
                cfg.MinCapacity = Mathf.Max(1, avg - half);
                cfg.MaxCapacity = Mathf.Max(avg, avg + half); // max >= avg always
                cfg.ColumnRange = tier.ColumnRange;
                cfg.DefaultConveyorSlots = Mathf.Max(1, tier.ConveyorSlots);
                cfg.DefaultApsTarget = tier.TargetAps;
                cfg.ApsTolerance = tier.ApsTolerance;
                cfg.HiddenRatio = Mathf.Clamp01(tier.HiddenRatio);
                cfg.DefaultComplexity = Mathf.Clamp(tier.Complexity, 1, 10);
                Set(af, "_config", cfg);
                owned.Add(cfg);
                owned.Add(af);
                Set(profile, "_levelCompleter", af);
            }

            // Generator config clone: APS target/tolerance + conveyor + fallback color count.
            // FallbackColors is the color count for the PROCEDURAL grid path (UseImageSource=false),
            // which is the primary path used by the harness and tests. Without this, tier.MaxColors
            // only affects the image→grid path and has NO effect on procedurally generated levels.
            if (baseProfile.GeneratorConfig is YAKGeneratorConfig baseGc)
            {
                var gc = Object.Instantiate(baseGc);
                gc.TargetAPS = tier.TargetAps;
                gc.ApsTolerance = tier.ApsTolerance;
                gc.ConveyorCount = Mathf.Max(1, tier.ConveyorSlots);
                gc.FallbackColors = Mathf.Max(1, tier.MaxColors);
                owned.Add(gc);
                Set(profile, "_generatorConfig", gc);
            }

            return new TierProfile(profile, owned);
        }
    }
}
