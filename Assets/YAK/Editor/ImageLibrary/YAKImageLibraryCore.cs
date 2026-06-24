using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Hoppa.YAK.Editor
{
    // Pure, Unity-free decision core for the Image Library tool. No network, no
    // disk, no EditorPrefs — everything here is unit-tested. The window/client do I/O.
    public static class YAKImageLibraryCore
    {
        // One idea per line; trims; skips blank + '#' comment lines; de-dupes
        // case-insensitively preserving first-seen order.
        public static List<string> ParseIdeas(string raw)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(raw)) return result;
            foreach (var line in raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                if (seen.Add(t)) result.Add(t);
            }
            return result;
        }
    }
}
