using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Hoppa.AudioBalance.Editor
{
    /// <summary>
    /// ACKNOWLEDGED FORK. This is a deliberate copy of
    /// <c>com.hoppa.leveleditor.core/Editor/Guide/MarkdownToHtml.cs</c>, kept
    /// character-for-character identical to it apart from this comment and the namespace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is duplicated rather than referenced because <c>com.hoppa.audiobalance</c> declares
    /// NO dependencies on purpose -- an audio tool must stay usable standalone, and making it
    /// require the level-editor package just to render a help page would be the wrong coupling
    /// for the sake of ~150 lines. There is no shared/common package for the two to depend on,
    /// and inventing one is a larger architectural change than a help button warrants.
    /// </para>
    /// <para>
    /// The honest cost is drift. If you change one copy, change the other: the files are meant
    /// to diff clean. As of this writing the original had been modified exactly zero times
    /// since it was introduced, which is why copying it was judged cheap.
    /// </para>
    /// </remarks>
    // Tiny, dependency-free Markdown → HTML converter, scoped to the constructs the
    // guides use: ATX headings, bold, italic, inline code, fenced code blocks,
    // unordered/ordered lists, blockquotes, horizontal rules, pipe tables, links,
    // and paragraphs. Not a full CommonMark implementation — just enough to render
    // the bundled guide as a clean, styled page.
    public static class MarkdownToHtml
    {
        public static string Convert(string markdown, string title)
        {
            string body = ConvertBody(markdown ?? string.Empty);
            return Wrap(title ?? "Guide", body);
        }

        // ── Block parsing ────────────────────────────────────────────────────────

        private static string ConvertBody(string md)
        {
            var lines = md.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var sb = new StringBuilder();
            int i = 0;

            while (i < lines.Length)
            {
                string line = lines[i];

                // Fenced code block ``` … ```
                if (line.TrimStart().StartsWith("```"))
                {
                    i++;
                    var code = new StringBuilder();
                    while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                    {
                        code.Append(Escape(lines[i])).Append('\n');
                        i++;
                    }
                    if (i < lines.Length) i++; // closing fence
                    sb.Append("<pre><code>").Append(code).Append("</code></pre>\n");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

                if (IsHr(line)) { sb.Append("<hr>\n"); i++; continue; }

                int h = HeadingLevel(line, out string htext);
                if (h > 0) { sb.Append("<h").Append(h).Append('>').Append(Inline(htext)).Append("</h").Append(h).Append(">\n"); i++; continue; }

                if (line.Contains("|") && i + 1 < lines.Length && IsTableSeparator(lines[i + 1]))
                { i = Table(lines, i, sb); continue; }

                if (line.TrimStart().StartsWith(">"))
                {
                    var quote = new StringBuilder();
                    while (i < lines.Length && lines[i].TrimStart().StartsWith(">"))
                    {
                        if (quote.Length > 0) quote.Append(' ');
                        quote.Append(Inline(StripQuote(lines[i])));
                        i++;
                    }
                    sb.Append("<blockquote>").Append(quote).Append("</blockquote>\n");
                    continue;
                }

                if (IsUnordered(line))
                {
                    sb.Append("<ul>\n");
                    while (i < lines.Length && IsUnordered(lines[i]))
                    { sb.Append("<li>").Append(Inline(ListText(lines[i]))).Append("</li>\n"); i++; }
                    sb.Append("</ul>\n");
                    continue;
                }

                if (IsOrdered(line))
                {
                    sb.Append("<ol>\n");
                    while (i < lines.Length && IsOrdered(lines[i]))
                    { sb.Append("<li>").Append(Inline(OrderedText(lines[i]))).Append("</li>\n"); i++; }
                    sb.Append("</ol>\n");
                    continue;
                }

                // Paragraph — accumulate until a blank or structural line.
                var para = new StringBuilder();
                while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && !IsStructural(lines, i))
                {
                    if (para.Length > 0) para.Append(' ');
                    para.Append(lines[i].Trim());
                    i++;
                }
                sb.Append("<p>").Append(Inline(para.ToString())).Append("</p>\n");
            }

            return sb.ToString();
        }

        private static bool IsStructural(string[] lines, int i)
        {
            string line = lines[i];
            if (line.TrimStart().StartsWith("```")) return true;
            if (IsHr(line)) return true;
            if (HeadingLevel(line, out _) > 0) return true;
            if (line.TrimStart().StartsWith(">")) return true;
            if (IsUnordered(line) || IsOrdered(line)) return true;
            if (line.Contains("|") && i + 1 < lines.Length && IsTableSeparator(lines[i + 1])) return true;
            return false;
        }

        // ── Tables ───────────────────────────────────────────────────────────────

        private static int Table(string[] lines, int i, StringBuilder sb)
        {
            var headers = SplitRow(lines[i]);
            i += 2; // header row + separator row
            sb.Append("<table>\n<thead><tr>");
            foreach (var c in headers) sb.Append("<th>").Append(Inline(c)).Append("</th>");
            sb.Append("</tr></thead>\n<tbody>\n");

            while (i < lines.Length && lines[i].Contains("|") && !string.IsNullOrWhiteSpace(lines[i]))
            {
                var cells = SplitRow(lines[i]);
                sb.Append("<tr>");
                foreach (var c in cells) sb.Append("<td>").Append(Inline(c)).Append("</td>");
                sb.Append("</tr>\n");
                i++;
            }
            sb.Append("</tbody>\n</table>\n");
            return i;
        }

        private static List<string> SplitRow(string line)
        {
            string t = line.Trim();
            if (t.StartsWith("|")) t = t.Substring(1);
            if (t.EndsWith("|")) t = t.Substring(0, t.Length - 1);
            var cells = new List<string>();
            foreach (var part in t.Split('|')) cells.Add(part.Trim());
            return cells;
        }

        private static bool IsTableSeparator(string line)
        {
            string t = line.Trim();
            if (!t.Contains("|") && !t.Contains("-")) return false;
            // Every non-pipe char must be one of - : space.
            foreach (char c in t)
                if (c != '|' && c != '-' && c != ':' && c != ' ') return false;
            return t.Contains("-");
        }

        // ── Line classifiers ───────────────────────────────────────────────────────

        private static bool IsHr(string line)
        {
            string t = line.Trim();
            return t == "---" || t == "***" || t == "___";
        }

        private static int HeadingLevel(string line, out string text)
        {
            text = null;
            string t = line.TrimStart();
            int n = 0;
            while (n < t.Length && t[n] == '#') n++;
            if (n < 1 || n > 6 || n >= t.Length || t[n] != ' ') return 0;
            text = t.Substring(n + 1).Trim();
            return n;
        }

        private static bool IsUnordered(string line)
        {
            string t = line.TrimStart();
            return t.StartsWith("- ") || t.StartsWith("* ");
        }

        private static string ListText(string line) => line.TrimStart().Substring(2).Trim();

        private static readonly Regex OrderedRe = new Regex(@"^\s*\d+\.\s+(.*)$");

        private static bool IsOrdered(string line) => OrderedRe.IsMatch(line);

        private static string OrderedText(string line)
        {
            var m = OrderedRe.Match(line);
            return m.Success ? m.Groups[1].Value.Trim() : line.Trim();
        }

        private static string StripQuote(string line)
        {
            string t = line.TrimStart();
            t = t.Substring(1); // drop '>'
            return t.TrimStart();
        }

        // ── Inline formatting ──────────────────────────────────────────────────────

        private static readonly Regex CodeRe = new Regex("`([^`]+)`");
        private static readonly Regex BoldRe = new Regex(@"\*\*([^*]+)\*\*");
        private static readonly Regex LinkRe = new Regex(@"\[([^\]]+)\]\(([^)]+)\)");

        // Single-asterisk emphasis. Applied AFTER BoldRe so that '**x**' is already a
        // <strong> and leaves no loose asterisks behind for this to mis-pair. The content
        // must start and end with a non-space character, which keeps arithmetic prose
        // ('2 * 3 * 4') from collapsing into emphasis.
        private static readonly Regex ItalicRe = new Regex(@"\*([^\s*](?:[^*\n]*[^\s*])?)\*");

        private static string Inline(string text)
        {
            string s = Escape(text);
            s = CodeRe.Replace(s, "<code>$1</code>");
            s = LinkRe.Replace(s, "<a href=\"$2\">$1</a>");
            s = BoldRe.Replace(s, "<strong>$1</strong>");
            s = ItalicRe.Replace(s, "<em>$1</em>");
            return s;
        }

        private static string Escape(string s)
            => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        // ── HTML shell ─────────────────────────────────────────────────────────────

        private static string Wrap(string title, string body)
        {
            return
"<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n" +
"<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n" +
"<title>" + Escape(title) + "</title>\n<style>\n" + Css + "\n</style>\n</head>\n<body>\n" +
"<main>\n" + body + "</main>\n</body>\n</html>\n";
        }

        private const string Css = @"
:root { color-scheme: light dark; }
* { box-sizing: border-box; }
body { margin: 0; background: #ffffff; color: #1f2328;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif;
  line-height: 1.65; }
main { max-width: 820px; margin: 0 auto; padding: 40px 24px 80px; }
h1, h2, h3, h4 { line-height: 1.25; margin-top: 1.6em; margin-bottom: .5em; font-weight: 600; }
h1 { font-size: 2em; padding-bottom: .3em; border-bottom: 1px solid #d0d7de; margin-top: 0; }
h2 { font-size: 1.5em; padding-bottom: .3em; border-bottom: 1px solid #d0d7de; }
h3 { font-size: 1.2em; }
p { margin: .8em 0; }
a { color: #0969da; text-decoration: none; }
a:hover { text-decoration: underline; }
ul, ol { padding-left: 1.6em; margin: .6em 0; }
li { margin: .25em 0; }
code { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: .88em; background: #eff1f3; padding: .15em .4em; border-radius: 6px; }
pre { background: #f6f8fa; border: 1px solid #d0d7de; border-radius: 8px;
  padding: 14px 16px; overflow-x: auto; line-height: 1.45; }
pre code { background: none; padding: 0; font-size: .85em; }
blockquote { margin: 1em 0; padding: .2em 1em; color: #57606a;
  border-left: 4px solid #d0d7de; background: #f6f8fa; border-radius: 0 6px 6px 0; }
hr { border: 0; border-top: 1px solid #d0d7de; margin: 2em 0; }
table { border-collapse: collapse; width: 100%; margin: 1em 0; display: block; overflow-x: auto; }
th, td { border: 1px solid #d0d7de; padding: 8px 12px; text-align: left; vertical-align: top; }
th { background: #f6f8fa; font-weight: 600; }
tr:nth-child(even) td { background: #fafbfc; }
@media (prefers-color-scheme: dark) {
  body { background: #0d1117; color: #e6edf3; }
  h1, h2 { border-bottom-color: #30363d; }
  a { color: #4493f8; }
  code { background: #262c36; }
  pre { background: #161b22; border-color: #30363d; }
  blockquote { color: #9198a1; border-left-color: #30363d; background: #161b22; }
  hr { border-top-color: #30363d; }
  th, td { border-color: #30363d; }
  th { background: #161b22; }
  tr:nth-child(even) td { background: #11151b; }
}
";
    }
}
