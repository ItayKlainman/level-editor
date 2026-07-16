using System;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // The six designer-facing difficulty knobs for a Bus Buddies level (the
    // boss's Excel model). A small [Serializable] value block that persists into
    // LevelDocument.GameData under stable "bb.*" keys, so Open → edit → Save
    // round-trips. Missing keys fall back to the autofill config's fresh-level
    // defaults. All knobs are clamped to their documented ranges on read.
    [Serializable]
    public sealed class BusBuddiesDifficultySettings
    {
        // GameData keys (stable — used by the panel, the autofiller and the tier builder).
        public const string KeyBusesChunks = "bb.busesChunks";
        public const string KeyDeviation   = "bb.deviation";
        public const string KeyColumns     = "bb.columns";
        public const string KeyDifficulty  = "bb.difficulty";
        public const string KeyNoSingleBus = "bb.noSingleBus";
        public const string KeyRoundToFive = "bb.roundToFive";

        [Range(1, 10)]  public int   BusesChunks      = 3;   // avg pixels per bus (via ChunksBase/Step); 1–10 (6–10 = coarse buses)
        [Range(0f, 1f)] public float DeviationPercent = 0.5f; // capacity spread around avg (0.5 = ±50%)
        [Range(1, 5)]   public int   Columns          = 3;   // queue columns
        [Range(1, 5)]   public int   Difficulty       = 3;   // "dig" depth of main colors
        public bool NoSingleBusColor = true;                 // no color may occupy a single bus (boss's rule; on by default)
        public bool RoundToFive      = true;                 // prefer capacities that are multiples of 5 (on by default)

        public BusBuddiesDifficultySettings Clone() => new BusBuddiesDifficultySettings
        {
            BusesChunks = BusesChunks,
            DeviationPercent = DeviationPercent,
            Columns = Columns,
            Difficulty = Difficulty,
            NoSingleBusColor = NoSingleBusColor,
            RoundToFive = RoundToFive,
        };

        // Clamp every knob to its documented range. Idempotent.
        public void Clamp()
        {
            BusesChunks      = Mathf.Clamp(BusesChunks, 1, 10);
            DeviationPercent = Mathf.Clamp01(DeviationPercent);
            Columns          = Mathf.Clamp(Columns, 1, 5);
            Difficulty       = Mathf.Clamp(Difficulty, 1, 5);
        }

        // Fresh-level defaults, sourced from the config when present (else the
        // built-in field defaults above).
        public static BusBuddiesDifficultySettings Defaults(BusBuddiesAutofillConfig cfg)
        {
            var s = new BusBuddiesDifficultySettings();
            if (cfg != null)
            {
                s.BusesChunks      = cfg.DefaultChunks;
                s.DeviationPercent = cfg.DefaultDeviation;
                s.Columns          = cfg.DefaultColumns;
                s.Difficulty       = cfg.DefaultDifficulty;
                s.NoSingleBusColor = cfg.DefaultNoSingleBusColor;
                s.RoundToFive      = cfg.DefaultRoundToFive;
            }
            s.Clamp();
            return s;
        }

        // Read the six knobs from a level's GameData, defaulting each missing key
        // to the config's fresh-level default. Result is clamped.
        public static BusBuddiesDifficultySettings ReadFrom(LevelDocument doc, BusBuddiesAutofillConfig cfg)
        {
            var def = Defaults(cfg);
            var gd = doc?.GameData;
            var s = new BusBuddiesDifficultySettings
            {
                BusesChunks      = ReadInt(gd,  KeyBusesChunks, def.BusesChunks),
                DeviationPercent = ReadFloat(gd, KeyDeviation,   def.DeviationPercent),
                Columns          = ReadInt(gd,  KeyColumns,     def.Columns),
                Difficulty       = ReadInt(gd,  KeyDifficulty,  def.Difficulty),
                NoSingleBusColor = ReadBool(gd, KeyNoSingleBus, def.NoSingleBusColor),
                RoundToFive      = ReadBool(gd, KeyRoundToFive, def.RoundToFive),
            };
            s.Clamp();
            return s;
        }

        // Write the six knobs into a level's GameData (creating the object if
        // absent). Clamps first so persisted values are always in range.
        public void WriteTo(LevelDocument doc)
        {
            if (doc == null) return;
            Clamp();
            if (doc.GameData == null) doc.GameData = new JObject();
            doc.GameData[KeyBusesChunks] = BusesChunks;
            doc.GameData[KeyDeviation]   = DeviationPercent;
            doc.GameData[KeyColumns]     = Columns;
            doc.GameData[KeyDifficulty]  = Difficulty;
            doc.GameData[KeyNoSingleBus] = NoSingleBusColor;
            doc.GameData[KeyRoundToFive] = RoundToFive;
        }

        private static int ReadInt(JObject gd, string key, int fallback)
        {
            var t = gd?[key];
            return (t != null && t.Type != JTokenType.Null) ? (int)t : fallback;
        }

        private static float ReadFloat(JObject gd, string key, float fallback)
        {
            var t = gd?[key];
            return (t != null && t.Type != JTokenType.Null) ? (float)t : fallback;
        }

        private static bool ReadBool(JObject gd, string key, bool fallback)
        {
            var t = gd?[key];
            return (t != null && t.Type != JTokenType.Null) ? (bool)t : fallback;
        }
    }
}
