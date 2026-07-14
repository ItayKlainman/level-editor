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
            return Slug(trimmed, "idea") + "_" + Fnv1aHex(trimmed) + ".png";
        }

        // The art-narrative asset: '#' lines are comments, the rest is one style
        // paragraph (hard-wrapped lines are rejoined with spaces). Returns "" when the
        // asset carries no style text, so callers fall back to DefaultStylePreamble.
        public static string ParseStylePrompt(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var lines = new List<string>();
            foreach (var line in raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                lines.Add(t);
            }
            return string.Join(" ", lines).Trim();
        }

        // The art narrative: a premium pixel-art COLLECTIBLE ICON, distilled from the
        // brief's five reference prompts (subjects stripped — those live in ideas.txt).
        // Fallback only: the editable source of truth is the config's style-prompt asset
        // (Assets/YAK/SourceImages/prompts.txt). Two clauses are load-bearing for the
        // image→grid converter — the outline ban (an outline smears into a thick dark
        // ring when downscaled) and the single flat background.
        public const string DefaultStylePreamble =
            "A single centered {idea}, drawn as a premium pixel-art collectible icon. Chunky blocky " +
            "pixel shapes, crisp hard edges, big flat solid fill colors, bold instantly recognizable " +
            "silhouette — the kind of cute collectible icon you'd want a whole set of. Give it real " +
            "personality: oversized expressive eyes, a clear readable emotion (cheerful, sleepy, " +
            "surprised, grumpy, smug, mischievous), and a playful pose; charming, wholesome and funny. " +
            "Skip the face only where it would look odd (plain geometric objects, scenery). Fill the " +
            "ENTIRE background with one flat uniform solid color that clearly contrasts the subject and " +
            "is not a color used in the subject — absolutely no gradient, no vignette, no glow, no " +
            "radial lighting, no background texture. Do NOT draw ANY outline, border, stroke, drop " +
            "shadow, or halo around the subject — no outline of any color; the subject is flat fills " +
            "only. No anti-aliasing fringe, no shading or dithering, no text, no photorealism, no frame " +
            "or border. Keep every shape bold, chunky and simple enough to read clearly at low resolution.";

        public static string BuildPrompt(string idea, IReadOnlyList<string> colorDescriptors, string stylePreamble)
        {
            string preamble = string.IsNullOrEmpty(stylePreamble) ? DefaultStylePreamble : stylePreamble;
            string subject = (idea ?? string.Empty).Trim();
            string body = preamble.Contains("{idea}")
                ? preamble.Replace("{idea}", subject)
                : preamble.TrimEnd() + " Subject: " + subject + ".";
            if (colorDescriptors != null && colorDescriptors.Count > 0)
                body += " Use only these flat solid colors: " + string.Join(", ", colorDescriptors) + ".";
            return body;
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

        // Lowercase a-z0-9 slug, non-alphanumerics collapse to single dashes,
        // capped at 40 chars; falls back to `fallback` when nothing survives.
        private static string Slug(string text, string fallback)
        {
            var sb = new StringBuilder();
            bool prevDash = false;
            foreach (char c in (text ?? string.Empty).ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) { sb.Append(c); prevDash = false; }
                else if (!prevDash && sb.Length > 0) { sb.Append('-'); prevDash = true; }
            }
            string slug = sb.ToString().Trim('-');
            if (slug.Length == 0) slug = fallback;
            if (slug.Length > 40) slug = slug.Substring(0, 40).Trim('-');
            return slug;
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
