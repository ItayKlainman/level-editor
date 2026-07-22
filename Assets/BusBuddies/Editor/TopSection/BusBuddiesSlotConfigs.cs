using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;

namespace Hoppa.BusBuddies.Editor
{
    // One "road block" on an Active-Bus-Row parking slot: the slot is blocked until
    // `Amount` buses have been clicked. SlotIndex is stored 0-based (internal). The UI
    // shows human slot numbers (internal + 1); the exporter offsets by IndexBase.
    public struct BusSlotConfig
    {
        public int SlotIndex;   // 0-based internal slot index
        public int Amount;      // buses that must be clicked before release (>= 1)
    }

    // UI-agnostic storage + queries for per-slot Road Blocks. Road blocks are non-cell
    // document data kept SPARSELY in LevelDocument.GameData["slotConfigs"] — only BLOCKED
    // slots have an entry; unblocked slots are omitted (default = nothing blocked). Each
    // entry is { "slotIndex": n, "amount": a } with n 0-based. Mirrors the YarnPalettes
    // GameData-JArray helper style (All/Write/Set/Remove/clamp).
    public static class BusBuddiesSlotConfigs
    {
        // APPROVED: 0-based storage + export. UI shows slots 1..N; export writes
        // SlotIndex 0..N-1. This single const is the one-line flip point if the game
        // ever wants 1-based SlotIndex on the wire (exporter/importer offset by it).
        public const int IndexBase = 0;

        // Prefill for a freshly-ticked slot; amounts always clamp to >= 1.
        public const int DefaultAmount = 10;

        private const string Key = "slotConfigs";

        public static List<BusSlotConfig> All(LevelDocument doc)
        {
            var list = new List<BusSlotConfig>();
            if (doc?.GameData?[Key] is not JArray arr) return list;
            foreach (var t in arr)
            {
                if (t is not JObject o) continue;
                list.Add(new BusSlotConfig
                {
                    SlotIndex = o["slotIndex"]?.Value<int>() ?? 0,
                    Amount    = o["amount"]?.Value<int>() ?? DefaultAmount,
                });
            }
            return list;
        }

        public static void Write(LevelDocument doc, List<BusSlotConfig> list)
        {
            doc.GameData ??= new JObject();
            var arr = new JArray();
            foreach (var s in list)
                arr.Add(new JObject
                {
                    ["slotIndex"] = s.SlotIndex,
                    ["amount"]    = s.Amount,
                });
            doc.GameData[Key] = arr;
        }

        public static bool IsBlocked(LevelDocument doc, int slotIndex)
        {
            foreach (var s in All(doc))
                if (s.SlotIndex == slotIndex) return true;
            return false;
        }

        public static bool TryGet(LevelDocument doc, int slotIndex, out BusSlotConfig config)
        {
            foreach (var s in All(doc))
                if (s.SlotIndex == slotIndex) { config = s; return true; }
            config = default;
            return false;
        }

        // Blocks a slot with the given amount (clamped to >= 1). If the slot is already
        // blocked, its amount is updated instead of adding a duplicate.
        public static void SetBlocked(LevelDocument doc, int slotIndex, int amount)
        {
            var list = All(doc);
            int clamped = System.Math.Max(1, amount);
            bool found = false;
            for (int i = 0; i < list.Count; i++)
                if (list[i].SlotIndex == slotIndex)
                {
                    var s = list[i];
                    s.Amount = clamped;
                    list[i] = s;
                    found = true;
                }
            if (!found)
                list.Add(new BusSlotConfig { SlotIndex = slotIndex, Amount = clamped });
            Write(doc, list);
        }

        // Unblocks a slot (removes its entry). No-op if it wasn't blocked.
        public static void Clear(LevelDocument doc, int slotIndex)
        {
            var list = All(doc);
            list.RemoveAll(s => s.SlotIndex == slotIndex);
            Write(doc, list);
        }

        // Sets the release amount of an already-blocked slot (clamp >= 1). No-op if the
        // slot isn't blocked (use SetBlocked to block it first).
        public static void SetAmount(LevelDocument doc, int slotIndex, int amount)
        {
            var list = All(doc);
            for (int i = 0; i < list.Count; i++)
                if (list[i].SlotIndex == slotIndex)
                {
                    var s = list[i];
                    s.Amount = System.Math.Max(1, amount);
                    list[i] = s;
                }
            Write(doc, list);
        }
    }
}
