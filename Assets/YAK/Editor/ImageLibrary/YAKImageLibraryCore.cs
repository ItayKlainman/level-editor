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

        // One theme prompt per block, blocks separated by one or more blank lines
        // (prompts are multi-sentence paragraphs). A leading enumerator ("1)", "2.",
        // "-", "*") is stripped; '#' lines are comments. De-dupes case-insensitively
        // preserving first-seen order.
        public static List<string> ParsePrompts(string raw)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(raw)) return result;

            string norm = raw.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var block in norm.Split(new[] { "\n\n" }, StringSplitOptions.None))
            {
                var lines = new List<string>();
                foreach (var line in block.Split('\n'))
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t.StartsWith("#")) continue;
                    lines.Add(t);
                }
                if (lines.Count == 0) continue;
                string text = StripEnumerator(string.Join(" ", lines));
                if (text.Length == 0) continue;
                if (seen.Add(text)) result.Add(text);
            }
            return result;
        }

        // <themeSlug>_<hash8-of-token>.png. The theme names the group; the caller's
        // per-image unique token keeps every generated image distinct so re-running a
        // batch accumulates new levels rather than overwriting (each run's AI-picked
        // subject differs). Deterministic for a given (theme, token).
        public static string ThemeToFileName(string theme, string uniqueToken)
        {
            return Slug((theme ?? string.Empty).Trim(), "theme") + "_" + Fnv1aHex(uniqueToken ?? string.Empty) + ".png";
        }

        public const string DefaultStylePreamble =
            "A single centered {idea}, flat bold cartoon illustration, big solid fill colors, " +
            "thick clean chunky shapes, clear readable silhouette. Fill the ENTIRE background with " +
            "one flat uniform solid color that clearly contrasts the subject and is not a color used " +
            "in the subject — absolutely no gradient, no vignette, no glow, no radial lighting, no " +
            "background texture. Do NOT draw ANY outline, border, stroke, drop shadow, or halo around " +
            "the subject — no outline of any color; the subject is flat fills only. No shading, no " +
            "text, no photorealism, no frame or border. When it suits the subject, give it a cute " +
            "kawaii face — simple dot eyes and a clear expression matching a mood (sleepy, cheerful, " +
            "proud, grumpy, mischievous) — and optionally one small simple prop for personality. Skip " +
            "the face for subjects where it would look odd (buildings, scenery, plain geometric " +
            "objects). For creatures, characters, and fantasy subjects a dynamic pose and a little " +
            "extra detail are welcome; keep everyday objects clean and minimal. Keep everything bold " +
            "and simple enough to read at low resolution.";

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

        // Convert-friendly wrapper for a THEME prompt: the model invents the subject,
        // these rules keep it a clean single-subject, no-outline, solid-background image
        // that the image→grid converter turns into a solvable level. {theme} is replaced
        // with the theme text.
        public const string DefaultThemeStylePreamble =
            "A single centered subject, flat bold cartoon illustration, big solid fill colors, " +
            "thick clean chunky shapes, clear readable silhouette. Fill the ENTIRE background with " +
            "one flat uniform solid color that clearly contrasts the subject and is not a color used " +
            "in the subject — absolutely no gradient, no vignette, no glow, no radial lighting, no " +
            "background texture. Do NOT draw ANY outline, border, stroke, drop shadow, or halo around " +
            "the subject — no outline of any color; the subject is flat fills only. No shading, no " +
            "text, no photorealism, no frame or border. Exactly ONE subject, centered, filling most of " +
            "the frame — not a busy scene. Keep everything bold and simple enough to read at low " +
            "resolution. Theme: {theme}";

        public static string BuildThemePrompt(string theme, IReadOnlyList<string> colorDescriptors, string themePreamble)
        {
            string preamble = string.IsNullOrEmpty(themePreamble) ? DefaultThemeStylePreamble : themePreamble;
            string t = (theme ?? string.Empty).Trim();
            string body = preamble.Contains("{theme}")
                ? preamble.Replace("{theme}", t)
                : preamble.TrimEnd() + " Theme: " + t + ".";
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

        // Drops a leading list enumerator: "1)", "2.", "-", "*" (with trailing space).
        private static string StripEnumerator(string s)
        {
            string t = (s ?? string.Empty).TrimStart();
            int i = 0;
            if (i < t.Length && (t[i] == '-' || t[i] == '*'))
            {
                i++;
            }
            else
            {
                int d = i;
                while (d < t.Length && t[d] >= '0' && t[d] <= '9') d++;
                if (d > i && d < t.Length && (t[d] == '.' || t[d] == ')')) i = d + 1;
                else i = 0;
            }
            return (i > 0 ? t.Substring(i) : t).TrimStart();
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
