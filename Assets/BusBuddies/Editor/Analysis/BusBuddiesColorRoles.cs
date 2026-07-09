using System;
using System.Collections.Generic;

namespace Hoppa.BusBuddies.Editor
{
    // Automatic main-vs-background color classification for the "dig" axis
    // (Approach A). A color is MAIN when its pixel share of the whole board is
    // >= the share threshold; everything else is background. The level's
    // designated outline color is excluded from "main" by default — burying the
    // black silhouette outline is not the intent.
    public static class BusBuddiesColorRoles
    {
        // Returns the set of main color ids (ordinal-keyed). `outlineId` may be
        // null; when non-null and excludeOutline is true it is never counted as main.
        public static HashSet<string> ClassifyMain(
            IReadOnlyDictionary<string, int> perColor,
            float shareThreshold,
            string outlineId,
            bool excludeOutline)
        {
            var main = new HashSet<string>(StringComparer.Ordinal);
            if (perColor == null || perColor.Count == 0) return main;

            long total = 0;
            foreach (var kv in perColor) total += kv.Value;
            if (total <= 0) return main;

            foreach (var kv in perColor)
            {
                // Ordinal (exact) match — colors are keyed with StringComparer.Ordinal
                // everywhere else in the pipeline (perColor, the main-color HashSet,
                // BusBuddiesDigArranger's color grouping); case-insensitive matching
                // here would be the odd one out.
                if (excludeOutline && outlineId != null &&
                    string.Equals(kv.Key, outlineId, StringComparison.Ordinal))
                    continue;

                float share = kv.Value / (float)total;
                if (share >= shareThreshold) main.Add(kv.Key);
            }
            return main;
        }
    }
}
