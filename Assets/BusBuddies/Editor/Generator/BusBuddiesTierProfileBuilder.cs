using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Object = UnityEngine.Object;

namespace Hoppa.BusBuddies.Editor
{
    /// A transient, tier-configured copy of a GameProfile. NEVER touches on-disk
    /// assets: every varied object is an Object.Instantiate clone. Call Cleanup()
    /// when done. Copy-mirror of YAKTierProfileBuilder, bus-scaled (no complexity axis).
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

    public static class BusBuddiesTierProfileBuilder
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

            // Image→grid clone: color cap + active slots.
            if (baseProfile.ImageToGrid is BusBuddiesImageToGrid baseIg)
            {
                var ig = Object.Instantiate(baseIg);
                ig.ColorCap = Mathf.Max(2, tier.MaxColors);
                ig.DefaultActiveSlots = Mathf.Max(1, tier.ConveyorSlots);
                owned.Add(ig);
                Set(profile, "_imageToGrid", ig);
            }

            // Autofiller clone + its config clone. The tier's six difficulty knobs
            // become the config's fresh-level DEFAULTS, so the autofiller builds the
            // bus queue with this tier's difficulty even on the procedural path
            // (where the generated doc's GameData carries no bb.* keys).
            var d = tier.Difficulty ?? new BusBuddiesDifficultySettings();
            if (baseProfile.LevelCompleter is BusBuddiesAutofiller baseAf)
            {
                var af = Object.Instantiate(baseAf);
                var baseCfg = af.GetType().GetField("_config", FF)?.GetValue(af) as BusBuddiesAutofillConfig;
                var cfg = baseCfg != null ? Object.Instantiate(baseCfg) : ScriptableObject.CreateInstance<BusBuddiesAutofillConfig>();
                cfg.DefaultChunks            = Mathf.Clamp(d.BusesChunks, 1, 5);
                cfg.DefaultDeviation         = Mathf.Clamp01(d.DeviationPercent);
                cfg.DefaultColumns           = Mathf.Clamp(d.Columns, 1, 5);
                cfg.DefaultDifficulty        = Mathf.Clamp(d.Difficulty, 1, 5);
                cfg.DefaultNoSingleBusColor  = d.NoSingleBusColor;
                cfg.DefaultRoundToFive       = d.RoundToFive;
                cfg.DefaultActiveSlots       = Mathf.Max(1, tier.ConveyorSlots);
                Set(af, "_config", cfg);
                owned.Add(cfg);
                owned.Add(af);
                Set(profile, "_levelCompleter", af);
            }

            // Generator config clone: active slots + fallback color count. APS is no
            // longer a per-tier target (it is a measured read-out), so the base
            // generator config's APS values are left as-is.
            // FallbackColors is the color count for the PROCEDURAL grid path
            // (UseImageSource=false), the primary path used by the harness and tests.
            if (baseProfile.GeneratorConfig is BusBuddiesGeneratorConfig baseGc)
            {
                var gc = Object.Instantiate(baseGc);
                gc.ConveyorCount = Mathf.Max(1, tier.ConveyorSlots);
                gc.FallbackColors = Mathf.Max(1, tier.MaxColors);
                owned.Add(gc);
                Set(profile, "_generatorConfig", gc);
            }

            return new TierProfile(profile, owned);
        }

        // Stamp a generated level's GameData with a tier's difficulty knobs so the
        // saved level carries them (and re-opens in the editor with the right
        // sliders). Config defaults already drove the fill; this persists intent.
        public static void StampDifficulty(LevelDocument doc, TierPreset tier)
        {
            if (doc == null || tier == null) return;
            (tier.Difficulty ?? new BusBuddiesDifficultySettings()).WriteTo(doc);
        }
    }
}
