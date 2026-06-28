using System;
using System.Collections.Generic;

namespace Hoppa.YAK.Sim
{
    // Click-pattern complexity helper (engine-agnostic, plain C#).
    //
    // Two responsibilities:
    //   • Score(taps, K)  — measure how "complex" a column-tap sequence is, 1..10.
    //   • Build(K, n, C)  — construct a target tap pattern for a desired complexity
    //                       (see Task 2), honoring the boss's rules R25–R31.
    //
    // Complexity is a property of the click SEQUENCE: pure round-robin (1,2,3,1,2,3)
    // is the trivial minimum; long same-column runs plus unpredictable jumps are the
    // maximum. Two blended signals: deviation-from-round-robin and max-run-length.
    public static class YakClickPattern
    {
        // R27: max consecutive taps on the same column for a complexity level.
        // NOTE: the boss's written TABLE (used here) and his formula
        // `max(2, min(5, 1 + C/2))` disagree at C3 and C8; we follow the table.
        // (C1-2 => 2, C3-5 => 3, C6-8 => 4, C9-10 => 5.) Flagged to the lead for
        // boss confirmation.
        public static int MaxRepeat(int complexity)
        {
            int c = complexity < 1 ? 1 : (complexity > 10 ? 10 : complexity);
            if (c <= 2) return 2;
            if (c <= 5) return 3;
            if (c <= 8) return 4;
            return 5;
        }

        // Complexity of a tap sequence in [1,10]. Degenerate inputs → 1.
        public static float Score(IReadOnlyList<int> taps, int numColumns)
        {
            if (taps == null || taps.Count <= 1 || numColumns <= 1) return 1f;
            int n = taps.Count;

            // (1) Max consecutive same-column run, normalized: run 1 → 0, run ≥5 → 1.
            int maxRun = 1, run = 1;
            for (int i = 1; i < n; i++)
            {
                run = taps[i] == taps[i - 1] ? run + 1 : 1;
                if (run > maxRun) maxRun = run;
            }
            float runScore = Clamp01((maxRun - 1) / 4f);

            // (2) Deviation from the pure round-robin successor (prev+1)%K:
            // 0 = exact round-robin, 1 = never the successor.
            int deviations = 0;
            for (int i = 1; i < n; i++)
                if (taps[i] != (taps[i - 1] + 1) % numColumns) deviations++;
            float rrScore = (float)deviations / (n - 1);

            float c01 = 0.6f * rrScore + 0.4f * runScore;
            float mapped = 1f + 9f * c01;
            return mapped < 1f ? 1f : (mapped > 10f ? 10f : mapped);
        }

        // Build a tap pattern of length `numSpools` over `numColumns`, with variety
        // scaled by complexity (1..10). Each column gets ≈ numSpools/numColumns taps
        // (±1, R28). Low C → near round-robin with short runs; high C → long runs
        // (up to MaxRepeat) and non-successor jumps (R28–R31). Never the pure
        // round-robin cycle (R25).
        public static int[] Build(int numColumns, int numSpools, int complexity, Random rng)
        {
            int k = numColumns < 1 ? 1 : numColumns;
            int n = numSpools < 0 ? 0 : numSpools;
            var result = new int[n];
            if (n == 0) return result;
            if (k == 1) return result; // all zeros — only column

            rng ??= new Random();
            int maxRepeat = MaxRepeat(complexity);
            float c01 = Clamp01((complexity - 1) / 9f);

            // Per-column quota: n/k each, first (n % k) columns get one extra (R28).
            var quota = new int[k];
            for (int c = 0; c < k; c++) quota[c] = n / k;
            for (int r = 0; r < n % k; r++) quota[r]++;

            int prev = -1, prevRun = 0;
            var weights = new double[k];
            for (int i = 0; i < n; i++)
            {
                double total = 0;
                for (int c = 0; c < k; c++)
                {
                    weights[c] = 0;
                    if (quota[c] <= 0) continue;
                    double w = quota[c]; // weighted-random by remaining quota (R28/R29)
                    if (c == prev)
                    {
                        if (prevRun >= maxRepeat) continue;       // soft run cap
                        w *= Lerp(0.15, 2.5, c01);                // high C favors runs
                    }
                    else if (c == (prev + 1) % k)
                    {
                        w *= Lerp(1.6, 0.35, c01);                // high C avoids the obvious successor
                    }
                    else
                    {
                        w *= Lerp(0.5, 1.3, c01);                 // high C favors distant jumps
                    }
                    weights[c] = w;
                    total += w;
                }

                int pick;
                if (total <= 0)
                {
                    // Only the capped `prev` has quota left → forced (quota exhaustion).
                    pick = prev;
                }
                else
                {
                    double roll = rng.NextDouble() * total;
                    pick = k - 1;
                    for (int c = 0; c < k; c++) { roll -= weights[c]; if (roll <= 0) { pick = c; break; } }
                }

                result[i] = pick;
                quota[pick]--;
                prevRun = pick == prev ? prevRun + 1 : 1;
                prev = pick;
            }

            // R25 guard: if construction still landed on the exact round-robin cycle,
            // swap two non-adjacent positions of different columns to break it.
            if (IsPureRoundRobin(result, k))
                PerturbSwap(result, rng);

            return result;
        }

        private static bool IsPureRoundRobin(int[] p, int k)
        {
            if (p.Length < 2) return false;
            for (int i = 1; i < p.Length; i++)
                if (p[i] != (p[i - 1] + 1) % k) return false;
            return true;
        }

        private static void PerturbSwap(int[] p, Random rng)
        {
            // Find any two positions with different columns at least 2 apart and swap.
            for (int a = 0; a < p.Length; a++)
                for (int b = a + 2; b < p.Length; b++)
                    if (p[a] != p[b]) { (p[a], p[b]) = (p[b], p[a]); return; }
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
