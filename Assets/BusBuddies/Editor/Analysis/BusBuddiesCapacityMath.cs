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
        // EXACT SUM, and — as of the re-tweak fix — every bus lies in [min,max]
        // whenever that's mathematically achievable for the WHOLE total (bucket
        // count `k` is computed upfront from `total`, not discovered by greedily
        // peeling — a greedy peel can wander into an unnecessary shortfall near
        // the tail even when a valid all-in-window split of the full total
        // exists). Only a total that's genuinely too small for its forced bucket
        // count (< min alone, or the `kMax < kMin` gap below) may land under min.
        // (Extracted from the original autofiller so the rule-aware PartitionColor
        // can build on it.)
        public static List<int> Partition(int total, int min, int max, int avg, System.Random rng)
        {
            if (total <= 0) return new List<int>();
            if (max < min) max = min;
            avg = Mathf.Clamp(avg, min, max);

            if (total <= max)
                return new List<int> { total }; // single bus (may be < min only when total < min — accepted)

            int k = ChooseBucketCount(total, min, max, avg, out bool feasible);
            if (!feasible)
                return k <= 2 ? BalancedPair(total, max, roundToFive: false) : EvenSplit(total, k, max);

            return DistributeWindow(total, k, min, max, avg, roundToFive: false, rng);
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
                caps = BalancedPair(total, max, roundToFive);

            return caps;
        }

        // Split `total` (>= 2) into two positive buses summing exactly to total,
        // capped at `max`, preferring one part to land on a multiple of 5 — but
        // ONLY when the snap keeps the pair reasonably balanced (never strands a
        // near-zero half; that was the old bug, e.g. total=6 → [5,1]). Used both
        // by the No-1-bus forced split (PartitionColor) and as the k<=2 fallback
        // when ChooseBucketCount reports total is genuinely too small to keep 2
        // buses in-window (total < 2*min) — a clean, balanced pair instead of a
        // tiny straggler.
        private static List<int> BalancedPair(int total, int max, bool roundToFive)
        {
            int a = total / 2;
            int b = total - a;
            if (roundToFive && total >= 10)
            {
                int m = Mathf.RoundToInt(a / 5f) * 5;
                int altB = total - m;
                bool inRange = m >= 1 && m <= max && altB >= 1 && altB <= max;
                bool balanced = inRange && Mathf.Min(m, altB) >= total / 3;
                if (balanced) { a = m; b = altB; }
            }
            a = Mathf.Clamp(a, 1, total - 1);
            if (a > max) a = max;
            b = total - a;
            return new List<int> { a, b };
        }

        // Deviation-window-aware round-to-5 partition: each bus is snapped to a
        // multiple of 5 within [min,max] wherever the window contains one
        // (best-effort — falls back to a non-multiple only when the window is too
        // narrow to contain any multiple of 5), summing EXACTLY to total. Reuses
        // the same [min,max] window as the plain Partition, so Deviation still
        // shapes the capacity spread even when Round-to-5 is on (MED-1: the two
        // knobs are independent — Round-to-5 must not collapse the window down to
        // a fixed avg-derived bus count).
        //
        // NEW invariant (re-tweak fix): when total >= min, every produced bus lies
        // in [min,max] — never strand a round-to-5 remainder below min. A total in
        // [min,max] is a single bus (even non-multiple-of-5, e.g. 22 → [22], not
        // [20,2]); a total > max splits into `k` buses computed UPFRONT from the
        // whole total (see ChooseBucketCount) so a valid all-in-window split is
        // never missed by a greedy peel wandering into an avoidable shortfall near
        // the tail. Only a total that's genuinely too small to form its forced
        // bucket count in-window (ChooseBucketCount reports infeasible) falls back
        // to a balanced (not degenerate) split.
        private static List<int> RoundFiveWindowPartition(int total, int min, int max, int avg, System.Random rng)
        {
            if (total <= 0) return new List<int>();
            if (max < min) max = min;
            avg = Mathf.Clamp(avg, min, max);

            if (total <= max)
                return new List<int> { total }; // single in-window bus, even non-multiple-of-5

            int k = ChooseBucketCount(total, min, max, avg, out bool feasible);
            if (!feasible)
                return k <= 2 ? BalancedPair(total, max, roundToFive: true) : EvenSplit(total, k, max);

            return DistributeWindow(total, k, min, max, avg, roundToFive: true, rng);
        }

        // How many buses `total` (> max) should split into, and whether that
        // count can keep EVERY bus within [min,max]. kMin = fewest buses that
        // keep each <= max; kMax = most buses that keep each >= min. Feasible
        // whenever kMax >= kMin (true unless total falls in the narrow
        // max < total < kMin*min gap — e.g. window [20,30]: total in (30,40)).
        // The ideal count (closest to total/avg) is clamped into the feasible
        // range so Deviation still governs bus count around avg.
        private static int ChooseBucketCount(int total, int min, int max, int avg, out bool feasible)
        {
            int kMin = Mathf.CeilToInt(total / (float)max);
            int kMax = Mathf.FloorToInt(total / (float)min);
            feasible = kMax >= kMin;
            if (!feasible) return kMin;
            int kIdeal = Mathf.Max(kMin, Mathf.RoundToInt(total / (float)avg));
            return Mathf.Clamp(kIdeal, kMin, kMax);
        }

        // Split `total` into exactly `k` buses, each guaranteed in [min,max]
        // (the caller only calls this when that's feasible for `total`/`k`).
        // Peels buses one at a time; at every step the sampled range is bounded
        // on BOTH sides so the REMAINING buckets can still each land in
        // [min,max] — this is what makes the whole-total upfront `k` actually
        // pay off (a naive per-step [min, remaining-min] bound, as the old code
        // used, can still wander into a shortfall near the tail). Sampling
        // within that (often wide) per-step range — not just avg ± jitter —
        // preserves a genuine spread of bus sizes for a wide Deviation window.
        // roundToFive snaps each take to the nearest in-range multiple of 5
        // (best-effort; the final bucket inherits whatever multiple-of-5-ness
        // the running remainder allows).
        private static List<int> DistributeWindow(
            int total, int k, int min, int max, int avg, bool roundToFive, System.Random rng)
        {
            var caps = new List<int>();
            int remaining = total;
            for (int i = 0; i < k - 1; i++)
            {
                int restCount = k - i - 1; // buses remaining AFTER this one
                int loBound = Mathf.Max(min, remaining - max * restCount);
                int hiBound = Mathf.Min(max, remaining - min * restCount);
                if (hiBound < loBound) hiBound = loBound; // defensive; feasible k never hits this

                int take;
                if (roundToFive)
                {
                    int sample = rng != null ? rng.Next(loBound, hiBound + 1) : avg;
                    take = SnapToMultipleOf5InRange(sample, loBound, hiBound);
                    if (take < loBound || take > hiBound) take = Mathf.Clamp(sample, loBound, hiBound);
                }
                else
                {
                    int jitter = rng != null ? rng.Next(-1, 2) : 0;
                    take = Mathf.Clamp(avg + jitter, loBound, hiBound);
                }
                caps.Add(take);
                remaining -= take;
            }
            caps.Add(remaining); // final bucket — guaranteed in [min,max] by the loop's invariant
            return caps;
        }

        // Rare defensive fallback: `total` can't keep every one of its `k`
        // (> 2) forced buses >= min. Distribute as evenly as possible (floor +
        // remainder), capped at max — balanced, never a near-zero straggler.
        private static List<int> EvenSplit(int total, int k, int max)
        {
            var caps = new List<int>(k);
            int baseVal = total / k, rem = total % k, sum = 0;
            for (int i = 0; i < k; i++)
            {
                int v = Mathf.Min(max, baseVal + (i < rem ? 1 : 0));
                caps.Add(v);
                sum += v;
            }
            int diff = total - sum;
            if (diff != 0) caps[caps.Count - 1] += diff; // absorb any clamp-driven remainder
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
