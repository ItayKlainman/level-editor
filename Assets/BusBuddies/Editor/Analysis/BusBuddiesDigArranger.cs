using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // The "dig" axis. Arranges a flat pool of buses across N queue columns so that
    // MAIN-color buses are buried (placed at higher / later indices — Buses[0] is
    // the HEAD, tapped first) according to the Difficulty knob:
    //   • Difficulty 1 — main colors interleaved evenly (round-robin baseline).
    //   • Difficulty 5 — most main-color buses stacked at the buried end, but one
    //     of each main color kept near the head so the level stays playable.
    // Target depth f = (difficulty-1)/4 ∈ [0,1]. Solvability is the hard ceiling:
    // Arrange() relaxes f (steps Difficulty down toward the baseline) until the
    // supplied validator confirms Solvable, never burying below the Diff-1 baseline.
    public static class BusBuddiesDigArranger
    {
        // Relaxing arrange: try the requested difficulty, then progressively lower
        // burial until `isSolvable` accepts an arrangement. Returns the first
        // solvable arrangement, else the Difficulty-1 baseline (honest — the
        // caller decides how to report an unsolvable baseline). isSolvable may be
        // null (accept the requested difficulty as-is).
        public static BusQueueData Arrange(
            IReadOnlyList<BusEntry> buses, ISet<string> mainColors, int columns, int difficulty,
            System.Random rng, Func<BusQueueData, bool> isSolvable)
        {
            int d = Mathf.Clamp(difficulty, 1, 5);
            BusQueueData last = null;
            for (; d >= 1; d--)
            {
                last = BuildArrangement(buses, mainColors, columns, d, rng);
                if (isSolvable == null || isSolvable(last)) return last;
            }
            return last; // Diff-1 baseline (may be unsolvable — caller reports honestly)
        }

        // Single deterministic arrangement at a fixed difficulty (no relaxation).
        public static BusQueueData BuildArrangement(
            IReadOnlyList<BusEntry> buses, ISet<string> mainColors, int columns, int difficulty,
            System.Random rng)
        {
            int cols = Mathf.Max(1, columns);
            var top = new BusQueueData();
            for (int i = 0; i < cols; i++) top.Columns.Add(new BusColumn());
            if (buses == null || buses.Count == 0) return top;

            var flat = FlatOrder(buses, mainColors, difficulty);

            // Distribute round-robin across columns; within-column order preserves
            // the flat (head→buried) order, so column index 0 is the head.
            for (int i = 0; i < flat.Count; i++)
                top.Columns[i % cols].Buses.Add(flat[i]);
            return top;
        }

        // Head→buried flat ordering for a difficulty. index 0 = head (most accessible).
        public static List<BusEntry> FlatOrder(
            IReadOnlyList<BusEntry> buses, ISet<string> mainColors, int difficulty)
        {
            var baseOrder = RoundRobinOrder(buses);
            int n = baseOrder.Count;
            if (n <= 1) return baseOrder;

            float f = (Mathf.Clamp(difficulty, 1, 5) - 1) / 4f;

            // One anchor per main color: its earliest occurrence in baseOrder stays
            // near the head (exempt from the dig push) so each main color is always
            // reachable from move one.
            var anchors = new HashSet<int>();
            if (mainColors != null && mainColors.Count > 0)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < n; i++)
                {
                    var id = baseOrder[i].ColorId;
                    if (IsMain(mainColors, id) && seen.Add(id)) anchors.Add(i);
                }
            }

            // Sort key per bus in [0,1]. Background & anchors keep their baseline
            // position; non-anchor mains are pushed toward the buried end by f.
            var keyed = new (float key, int idx, BusEntry bus)[n];
            for (int i = 0; i < n; i++)
            {
                float baseKey = i / (float)(n - 1);
                bool push = mainColors != null && IsMain(mainColors, baseOrder[i].ColorId) && !anchors.Contains(i);
                float key = push ? baseKey * (1f - f) + 1f * f : baseKey;
                keyed[i] = (key, i, baseOrder[i]);
            }

            // Deterministic stable sort: by key, tie-broken by original index.
            Array.Sort(keyed, (a, b) =>
            {
                int c = a.key.CompareTo(b.key);
                return c != 0 ? c : a.idx.CompareTo(b.idx);
            });

            var flat = new List<BusEntry>(n);
            foreach (var k in keyed) flat.Add(k.bus);
            return flat;
        }

        // Even round-robin interleave across colors (colors ordered by id for
        // determinism; per-color bus order preserved).
        public static List<BusEntry> RoundRobinOrder(IReadOnlyList<BusEntry> buses)
        {
            var order = new List<BusEntry>();
            if (buses == null || buses.Count == 0) return order;

            var groups = new Dictionary<string, List<BusEntry>>(StringComparer.Ordinal);
            var colorOrder = new List<string>();
            foreach (var b in buses)
            {
                string id = b.ColorId ?? "";
                if (!groups.TryGetValue(id, out var list))
                {
                    list = new List<BusEntry>();
                    groups[id] = list;
                    colorOrder.Add(id);
                }
                list.Add(b);
            }
            colorOrder.Sort(StringComparer.Ordinal);

            int maxLen = 0;
            foreach (var g in groups.Values) maxLen = Mathf.Max(maxLen, g.Count);
            for (int i = 0; i < maxLen; i++)
                foreach (var id in colorOrder)
                {
                    var g = groups[id];
                    if (i < g.Count) order.Add(g[i]);
                }
            return order;
        }

        private static bool IsMain(ISet<string> mainColors, string id)
            => id != null && mainColors.Contains(id);
    }
}
