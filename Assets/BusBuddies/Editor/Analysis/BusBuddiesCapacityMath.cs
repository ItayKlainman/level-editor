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
        // undersized bus (= total), the documented exception. Never strands a
        // remainder below min. (Extracted from the original autofiller so the
        // rule-aware PartitionColor can build on it.)
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
        //                 minimizes the number of non-multiples).
        //   noSingleBus : a color that would occupy a single bus is re-split into
        //                 two (respecting round-to-5 when also enabled).
        public static List<int> PartitionColor(
            int total, int min, int max, int avg, bool noSingleBus, bool roundToFive, System.Random rng)
        {
            List<int> caps;
            if (total <= 0) return new List<int>();

            if (roundToFive)
            {
                int n = EstimateBusCount(total, avg);
                caps = RoundFivePartition(total, n);
            }
            else
            {
                caps = Partition(total, min, max, avg, rng);
            }

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

        // Distribute `total` across `n` buses maximizing multiples of 5, summing
        // exactly. When total % 5 != 0 exactly one bus carries the remainder (the
        // single non-multiple); otherwise every bus is a multiple of 5. Guarantees
        // positive buses (drops any zero slots).
        private static List<int> RoundFivePartition(int total, int n)
        {
            var caps = new List<int>();
            if (total <= 0) return caps;

            int fives = total / 5;       // number of whole 5-blocks
            int rem   = total % 5;       // 0..4 → the sole non-multiple when > 0

            // Can't make more positive buses than there are 5-blocks (+1 for a
            // remainder-only bus). Keeps every bus positive.
            int maxBuses = fives + (rem > 0 ? 1 : 0);
            if (maxBuses < 1) maxBuses = 1;
            n = Mathf.Clamp(n, 1, maxBuses);

            if (rem > 0)
            {
                // Reserve one bus for the remainder; the rest are pure multiples of 5.
                int fivesBuses = n - 1;
                if (fivesBuses >= 1)
                {
                    Spread5(caps, fives, fivesBuses); // fives >= fivesBuses (n <= fives+1) → all positive
                    caps.Add(rem);
                }
                else
                {
                    // n == 1 → a single bus carries everything (one non-multiple).
                    caps.Add(total);
                }
            }
            else
            {
                // Every bus a multiple of 5, spread as evenly as possible (n <= fives).
                Spread5(caps, fives, n);
            }
            return caps;
        }

        // Append `count` buses whose capacities are multiples of 5 spreading
        // `fives` 5-blocks as evenly as possible.
        private static void Spread5(List<int> caps, int fives, int count)
        {
            if (count <= 0) { if (fives > 0) caps.Add(fives * 5); return; }
            int baseBlocks = fives / count;
            int extra = fives % count;
            for (int i = 0; i < count; i++)
            {
                int blocks = baseBlocks + (i < extra ? 1 : 0);
                caps.Add(blocks * 5);
            }
        }
    }
}
