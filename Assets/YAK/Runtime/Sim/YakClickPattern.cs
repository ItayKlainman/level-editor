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

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
