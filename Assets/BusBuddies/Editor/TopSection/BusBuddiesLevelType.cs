using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;

namespace Hoppa.BusBuddies.Editor
{
    // Mirrors the game's BUBLevelType enum (None=0, Hard=1, SuperHard=2) — a pure
    // player-facing presentation tag, no gameplay/sim implications.
    public enum BusLevelType
    {
        None = 0,
        Hard = 1,
        SuperHard = 2,
    }

    // UI-agnostic storage + queries for the per-level difficulty tag. Kept SPARSELY in
    // LevelDocument.GameData["levelType"] as the int ordinal — None has no entry
    // (default = nothing set). Mirrors the BusBuddiesSlotConfigs/BusBuddiesPlateConfigs
    // GameData-JObject style.
    public static class BusBuddiesLevelType
    {
        private const string Key = "levelType";

        public static BusLevelType Get(LevelDocument doc)
        {
            var token = doc?.GameData?[Key];
            if (token == null) return BusLevelType.None;
            int ordinal = token.Value<int>();
            return System.Enum.IsDefined(typeof(BusLevelType), ordinal)
                ? (BusLevelType)ordinal
                : BusLevelType.None;
        }

        // Setting None removes the key (sparse storage — existing None-levels stay
        // byte-identical on export).
        public static void Set(LevelDocument doc, BusLevelType value)
        {
            doc.GameData ??= new JObject();
            if (value == BusLevelType.None)
                doc.GameData.Remove(Key);
            else
                doc.GameData[Key] = (int)value;
        }
    }
}
