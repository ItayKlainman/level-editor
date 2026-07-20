using NUnit.Framework;
using Hoppa.AudioBalance.Editor;

namespace Hoppa.AudioBalance.Editor.Tests
{
    /// <summary>
    /// ACKNOWLEDGED FORK, paired with the copied
    /// <see cref="Hoppa.AudioBalance.Editor.MarkdownToHtml"/> -- see that file for why the
    /// converter is duplicated rather than referenced. This suite mirrors
    /// <c>com.hoppa.leveleditor.core/Tests/Editor/MarkdownToHtmlTests.cs</c> so the copy cannot
    /// rot silently: if the two implementations drift, one of these fails.
    /// </summary>
    public class MarkdownToHtmlTests
    {
        private static string Html(string md) => MarkdownToHtml.Convert(md, "T");

        [Test]
        public void Heading_LevelsMapToHTags()
        {
            Assert.IsTrue(Html("# One").Contains("<h1>One</h1>"));
            Assert.IsTrue(Html("### Three").Contains("<h3>Three</h3>"));
        }

        [Test]
        public void HashWithoutSpace_IsNotAHeading()
        {
            Assert.IsFalse(Html("#notaheading").Contains("<h1>"));
        }

        [Test]
        public void Bold_And_InlineCode()
        {
            Assert.IsTrue(Html("a **b** c").Contains("<strong>b</strong>"));
            Assert.IsTrue(Html("use `Convert` now").Contains("<code>Convert</code>"));
        }

        [Test]
        public void Italic_Renders()
        {
            Assert.IsTrue(Html("the *outlier* marker").Contains("<em>outlier</em>"));
        }

        [Test]
        public void Bold_IsNotEatenByItalic()
        {
            string html = Html("a **b** c");
            Assert.IsTrue(html.Contains("<strong>b</strong>"));
            Assert.IsFalse(html.Contains("<em>"));
        }

        [Test]
        public void ListMarker_IsNotItalic()
        {
            string html = Html("* one\n* two");
            Assert.IsTrue(html.Contains("<li>one</li>"));
            Assert.IsFalse(html.Contains("<em>"));
        }

        [Test]
        public void Link_Renders()
        {
            Assert.IsTrue(Html("[site](https://x.io)").Contains("<a href=\"https://x.io\">site</a>"));
        }

        [Test]
        public void FencedCodeBlock_IsPreCode_AndEscaped()
        {
            string html = Html("```\na < b\n```");
            Assert.IsTrue(html.Contains("<pre><code>"));
            Assert.IsTrue(html.Contains("a &lt; b"));
        }

        [Test]
        public void UnorderedList()
        {
            string html = Html("- one\n- two");
            Assert.IsTrue(html.Contains("<ul>"));
            Assert.IsTrue(html.Contains("<li>one</li>"));
            Assert.IsTrue(html.Contains("<li>two</li>"));
        }

        [Test]
        public void OrderedList()
        {
            string html = Html("1. first\n2. second");
            Assert.IsTrue(html.Contains("<ol>"));
            Assert.IsTrue(html.Contains("<li>first</li>"));
        }

        [Test]
        public void Table_HeaderAndRows()
        {
            string html = Html("| A | B |\n|---|---|\n| 1 | 2 |");
            Assert.IsTrue(html.Contains("<table>"));
            Assert.IsTrue(html.Contains("<th>A</th>"));
            Assert.IsTrue(html.Contains("<td>1</td>"));
            Assert.IsTrue(html.Contains("<td>2</td>"));
        }

        [Test]
        public void Blockquote_And_HorizontalRule()
        {
            Assert.IsTrue(Html("> note").Contains("<blockquote>note</blockquote>"));
            Assert.IsTrue(Html("---").Contains("<hr>"));
        }

        [Test]
        public void Paragraph_EscapesHtml()
        {
            string html = Html("2 < 3 & 4 > 1");
            Assert.IsTrue(html.Contains("2 &lt; 3 &amp; 4 &gt; 1"));
        }

        [Test]
        public void Convert_ProducesFullDocumentWithTitle()
        {
            string html = MarkdownToHtml.Convert("# Hi", "My Guide");
            Assert.IsTrue(html.Contains("<!doctype html>"));
            Assert.IsTrue(html.Contains("<title>My Guide</title>"));
            Assert.IsTrue(html.Contains("<h1>Hi</h1>"));
        }

        [Test]
        public void NullInput_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => MarkdownToHtml.Convert(null, null));
        }
    }
}
