using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers the plain-text model card preview in the Model Cache view.
    /// </summary>
    internal sealed class ModelCardTextTests
    {
        /// <summary>
        /// Verifies YAML metadata, HTML, images, and Markdown markup are removed, and link text and headings stay.
        /// </summary>
        [Test]
        public void ToPlainText_KeepsReadableTextOnly()
        {
            const string markdown =
                "---\r\nlicense: mit\r\ntags:\r\n- depth\r\n---\r\n" +
                "# MiDaS\r\n\r\n<!-- hidden -->\r\n![banner](images/banner.png)\r\n" +
                "<div align=\"center\">Depth estimation</div>\r\n\r\n\r\n\r\n" +
                "> See the **paper**, _by the authors_, and [the code](https://github.com/isl-org/MiDaS).\r\n" +
                "Keep snake_case_names.\r\n" +
                "```python\r\nmodel = load()\r\n```\r\n";

            var text = ModelCardText.ToPlainText(markdown);

            Assert.That(text, Is.EqualTo("MiDaS\n\nDepth estimation\n\nSee the paper, by the authors, and the code.\nKeep snake_case_names.\nmodel = load()"));
        }

        /// <summary>
        /// Verifies an empty card, and a card with only metadata, have no text.
        /// </summary>
        [TestCase(null)]
        [TestCase("   ")]
        [TestCase("---\nlicense: mit\n---\n")]
        public void ToPlainText_ReturnsEmptyWithoutText(string markdown)
        {
            Assert.That(ModelCardText.ToPlainText(markdown), Is.Empty);
        }

        /// <summary>
        /// Verifies a dash rule later in the card is not taken for metadata.
        /// </summary>
        [Test]
        public void ToPlainText_KeepsTextWhenThereIsNoMetadata()
        {
            Assert.That(ModelCardText.ToPlainText("Intro\n---\nMore"), Is.EqualTo("Intro\n---\nMore"));
        }

        /// <summary>
        /// Verifies the preview keeps the first lines and reports when lines were left out.
        /// </summary>
        [Test]
        public void GetPreview_CutsAfterMaxLines()
        {
            var preview = ModelCardText.GetPreview("a\nb\n\nc\nd", 3, 100, out var truncated);
            Assert.That(preview, Is.EqualTo("a\nb"));
            Assert.That(truncated, Is.True);

            Assert.That(ModelCardText.GetPreview("a\nb", 3, 100, out truncated), Is.EqualTo("a\nb"));
            Assert.That(truncated, Is.False);
        }

        /// <summary>
        /// Verifies a long paragraph is cut at a word near the character limit, with an ellipsis.
        /// </summary>
        [Test]
        public void GetPreview_CutsLongTextAtAWord()
        {
            var preview = ModelCardText.GetPreview("one two three four five", 10, 12, out var truncated);

            Assert.That(preview, Is.EqualTo("one two…"));
            Assert.That(truncated, Is.True);
        }
    }
}
