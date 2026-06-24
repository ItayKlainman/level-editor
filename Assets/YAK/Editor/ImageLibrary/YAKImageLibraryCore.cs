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

        // <slug>_<hash8>.png — deterministic and collision-free across distinct ideas.
        public static string IdeaToFileName(string idea)
        {
            string trimmed = (idea ?? string.Empty).Trim();
            var sb = new StringBuilder();
            bool prevDash = false;
            foreach (char c in trimmed.ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) { sb.Append(c); prevDash = false; }
                else if (!prevDash && sb.Length > 0) { sb.Append('-'); prevDash = true; }
            }
            string slug = sb.ToString().Trim('-');
            if (slug.Length == 0) slug = "idea";
            if (slug.Length > 40) slug = slug.Substring(0, 40).Trim('-');
            return slug + "_" + Fnv1aHex(trimmed) + ".png";
        }

        public static List<string> FindMissing(IEnumerable<string> ideas, IEnumerable<string> existingFileNames)
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existingFileNames != null)
                foreach (var f in existingFileNames)
                    if (!string.IsNullOrEmpty(f)) existing.Add(Path.GetFileName(f));

            var missing = new List<string>();
            if (ideas == null) return missing;
            foreach (var idea in ideas)
                if (!existing.Contains(IdeaToFileName(idea)))
                    missing.Add(idea);
            return missing;
        }

        private static string Fnv1aHex(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                foreach (char c in s) { h ^= c; h *= 16777619u; }
                return h.ToString("x8");
            }
        }
    }
}
