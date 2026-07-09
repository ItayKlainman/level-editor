using System.Collections.Generic;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Pure capacity math for the designer difficulty model.
    //   • Buses Chunks → average pixels per bus (ChunksBase + step ramp).
    //   • Deviation %  → the [min,max] capacity window around the average.
    //   • Per-color partition into buses summing EXACTLY to the color total,
    //     with the optional "No 1-bus color" and "Round to 5" rules.
    //
    // Rule-conflict priority (lead decision): exact-sum is inviolable; No-1-bus
    // wins next; Round-to-5 yields first (best-effort).
    public static class BusBuddiesCapacityMath
    {
        // avg pixels per bus for a Buses Chunks value (1..5).
        public static int Avg(int busesChunks, int chunksBase, int chunksStep)
        {
            int c = Mathf.Clamp(busesChunks, 1, 5);
            return Mathf.Max(1, chunksBase + (c - 1) * Mathf.Max(0, chunksStep));
        }

        // Capacity window around avg for a deviation fraction (0..1).
        // half = round(avg*dev); min = max(1, avg-half); max = avg+half.
        public static void Window(int avg, float deviation, out int min, out int max)
        {
            avg = Mathf.Max(1, avg);
            int half = Mathf.RoundToInt(avg * Mathf.Clamp01(deviation));
            min = Mathf.Max(1, avg - half);
            max = avg + half;
        }

        // Approximate bus count for a pixel total at a given avg (>= 1 when total > 0).
        public static int EstimateBusCount(int totalPixels, int avg)
        {
            if (totalPixels <= 0) return 0;
            if (avg <= 0) return 1;
            return Mathf.Max(1, Mathf.RoundToInt(totalPixels / (float)avg));
        }

        // Base partition: split `total` into capacities in [min,max] (jittered
        // around avg) summing EXACTLY to total. If total < min, returns one
        // undersized bus (= total), the documented exception. The guarantee is
        // EXACT SUM, not a min-floor on every bus: e.g. avg=10, dev=0 (min=max=10),
        // total=25 → [10,10,5] — the final chunk can legitimately land below min.
        // (Extracted from the original autofiller so the rule-aware PartitionColor
        // can build on it.)
        public static List<int> Partition(int total, int min, int max, int avg, System.Random rng)
        {
            var caps = new List<int>();
            if (total <= 0) return caps;
            if (max < min) max = min;
            avg = Mathf.Clamp(avg, min, max);

            if (total <= max)
            {
                caps.Add(total); // single bus (may be < min only when total < min — accepted)
                return caps;
            }

            int remaining = total;
            while (remaining > max)
            {
                int jitter = rng != null ? rng.Next(-1, 2) : 0;
                int take = Mathf.Clamp(avg + jitter, min, Mathf.Min(max, remaining - min));
                if (take < min) take = min;
                caps.Add(take);
                remaining -= take;
            }
            caps.Add(remaining);
            return caps;
        }

        // Rule-aware per-color partition. Always sums EXACTLY to total.
        //   roundToFive : prefer capacities that are multiples of 5 (best-effort,
        //                 minimizes the number of non-multiples), WITHIN the
        //                 [min,max] Deviation window — Deviation and Round-to-5
        //                 are independent knobs, so this never collapses the
        //                 window down to a fixed bus count derived from avg alone.
        //   noSingleBus : a color that would occupy a single bus is re-split into
        //                 two (respecting round-to-5 when also enabled).
        public static List<int> PartitionColor(
            int total, int min, int max, int avg, bool noSingleBus, bool roundToFive, System.Random rng)
        {
            List<int> caps;
            if (total <= 0) return new List<int>();

            caps = roundToFive
                ? RoundFiveWindowPartition(total, min, max, avg, rng)
                : Partition(total, min, max, avg, rng);

            // No-1-bus wins over round-to-5: split a single bus into two.
            if (noSingleBus && caps.Count == 1 && total >= 2)
                caps = SplitInTwo(total, roundToFive);

            return caps;
        }

        // Split `total` (>= 2) into two positive buses summing exactly to total,
        // preferring two multiples of 5 when roundToFive is on.
        private static List<int> SplitInTwo(int total, bool roundToFive)
        {
            int a = total / 2;
            if (roundToFive)
            {
                // Nearest multiple of 5 to total/2 that leaves both parts >= 1.
                int rounded = Mathf.RoundToInt(a / 5f) * 5;
                rounded = Mathf.Clamp(rounded, 1, total - 1);
                a = rounded;
            }
            a = Mathf.Clamp(a, 1, total - 1);
            return new List<int> { a, total - a };
        }

        // Deviation-window-aware round-to-5 partition: each bus is snapped to a
        // multiple of 5 within [min,max] wherever the window contains one
        // (best-effort — falls back to a non-multiple only when the window is too
        // narrow to contain any multiple of 5), summing EXACTLY to total. Reuses
        // the same [min,max] window as the plain Partition, so Deviation still
        // shapes the capacity spread even when Round-to-5 is on (MED-1: the two
        // knobs are independent — Round-to-5 must not collapse the window down to
        // a fixed avg-derived bus count).
        private static List<int> RoundFiveWindowPartition(int total, int min, int max, int avg, System.Random rng)
        {
            var caps = new List<int>();
            if (total <= 0) return caps;
            if (max < min) max = min;
            avg = Mathf.Clamp(avg, min, max);

            if (total <= max)
            {
                caps.Add(total); // single bus (may be < min only when total < min — mirrors Partition)
                return caps;
            }

            int remaining = total;
            while (remaining > max)
            {
                // Sample a capacity from the deviation window (not just avg ± jitter)
                // so a wide window actually produces a spread of bus sizes, then snap
                // it to the nearest in-window multiple of 5.
                int hi = Mathf.Min(max, remaining - min);
                if (hi < min) hi = min;
                int sample = rng != null ? rng.Next(min, hi + 1) : avg;
                int take = SnapToMultipleOf5InRange(sample, min, hi);
                if (take <= 0) take = min;
                caps.Add(take);
                remaining -= take;
            }
            caps.Add(remaining); // final chunk — a multiple of 5 when the running remainder allows, else the sole exception
            return caps;
        }

        // Nearest multiple of 5 to `value` that stays within [lo,hi], when one
        // exists in that range; otherwise falls back to clamping `value` into
        // range (a non-multiple — best-effort, only happens when the window is
        // narrower than 5).
        private static int SnapToMultipleOf5InRange(int value, int lo, int hi)
        {
            int m = Mathf.RoundToInt(value / 5f) * 5;
            if (m < lo) m = Mathf.CeilToInt(lo / 5f) * 5;
            if (m > hi) m = Mathf.FloorToInt(hi / 5f) * 5;
            if (m < lo || m > hi) return Mathf.Clamp(value, lo, hi);
            return m;
        }
    }
}
