using NUnit.Framework;
using Hoppa.AudioBalance.Editor;

namespace Hoppa.AudioBalance.Editor.Tests
{
    /// <summary>
    /// Covers the two parts of the guide button that are actually logic: resolving the packaged
    /// markdown on disk, and rendering it. The button itself -- and <c>AudioBalanceGuide.Open</c>
    /// as a whole -- is deliberately NOT tested: its payload is
    /// <c>Application.OpenURL</c> plus a modal <c>EditorUtility.DisplayDialog</c>, neither of
    /// which can be asserted on in an EditMode run without stubbing the Editor API. A test around
    /// it would assert nothing, so none is written.
    /// </summary>
    public class AudioBalanceGuideTests
    {
        private static string RenderedGuide()
        {
            string md = AudioBalanceGuide.LoadGuideMarkdown();
            Assert.IsNotNull(md, "Guide markdown did not resolve; the render assertions below cannot mean anything.");
            return MarkdownToHtml.Convert(md, "T");
        }

        // The <main> element holds the converted document body. The CSS in <head> legitimately
        // contains '*' (the universal selector), so body-only assertions must exclude it.
        private static string Body(string html)
        {
            int start = html.IndexOf("<main>", System.StringComparison.Ordinal);
            int end = html.IndexOf("</main>", System.StringComparison.Ordinal);
            Assert.Greater(start, -1, "no <main> in rendered document");
            Assert.Greater(end, start, "no closing </main> in rendered document");
            return html.Substring(start + "<main>".Length, end - start - "<main>".Length);
        }

        [Test]
        public void PackagedGuide_ResolvesOnDisk()
        {
            string md = AudioBalanceGuide.LoadGuideMarkdown();

            Assert.IsNotNull(md, "guide markdown not found at " + AudioBalanceGuide.PackageGuidePath);
            Assert.IsNotEmpty(md);
        }

        [Test]
        public void PackagedGuide_RendersAsFullDocument()
        {
            string html = RenderedGuide();

            Assert.IsTrue(html.Contains("<!doctype html>"));
            Assert.IsTrue(html.Contains("<title>T</title>"));
        }

        [Test]
        public void PackagedGuide_RendersItsHeadings()
        {
            string html = RenderedGuide();

            Assert.IsTrue(html.Contains("<h1>Audio Balance — Designer Guide</h1>"),
                "top-level heading of the shipped guide did not render");
            Assert.IsTrue(html.Contains("<h2>Workflow</h2>"));
        }

        [Test]
        public void PackagedGuide_RendersItsTables()
        {
            string html = RenderedGuide();

            Assert.IsTrue(html.Contains("<table>"));
            Assert.IsTrue(html.Contains("<th>Category</th>"), "category table header did not render");
            Assert.IsTrue(html.Contains("<td>Music</td>"), "category table body did not render");
        }

        [Test]
        public void PackagedGuide_RendersItsLists()
        {
            string html = RenderedGuide();

            Assert.IsTrue(html.Contains("<ol>"), "the numbered Workflow steps did not render");
            Assert.IsTrue(html.Contains("<ul>"), "the bulleted 'Three things worth knowing' did not render");
        }

        [Test]
        public void PackagedGuide_RendersEmphasis()
        {
            string html = RenderedGuide();

            Assert.IsTrue(html.Contains("<em>outlier</em>"), "single-asterisk emphasis did not render");
            Assert.IsTrue(html.Contains("<strong>Write Table</strong>"), "bold did not render");
        }

        /// <summary>
        /// The end-to-end guard that the converter's construct coverage actually matches what this
        /// specific guide uses. Any unsupported inline markup shows up to the designer as raw
        /// asterisks, which is exactly the defect this catches.
        /// </summary>
        [Test]
        public void PackagedGuide_LeavesNoLiteralAsterisks()
        {
            string body = Body(RenderedGuide());

            Assert.IsFalse(body.Contains("*"),
                "rendered guide body still contains a literal '*', so some emphasis markup was not converted");
        }
    }
}
