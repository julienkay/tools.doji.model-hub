using NUnit.Framework;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers manifest row UI rules that should stay consistent across manifest editing surfaces.
    /// </summary>
    internal sealed class ManifestModelEntryTests
    {
        /// <summary>
        /// Ensures invalid repository revisions cannot open the file-selection popup.
        /// </summary>
        [Test]
        public void CanOpenFileSelection_ReturnsFalseForInvalidRevision()
        {
            var entry = new ManifestModelEntry("onnx-community", "tiny-clip-test-ONNX", "test");
            var validationState = new ManifestModelValidationState(
                entry.ModelId,
                entry.Revision,
                ManifestModelValidationStatus.Invalid,
                "Revision `test` was not found on Hugging Face.");

            Assert.That(ManifestModelEntry.CanOpenFileSelection(entry, validationState, true), Is.False);
        }

        /// <summary>
        /// Ensures valid repository revisions can open the file-selection popup.
        /// </summary>
        [Test]
        public void CanOpenFileSelection_ReturnsTrueForValidRevision()
        {
            var entry = new ManifestModelEntry("onnx-community", "tiny-clip-test-ONNX", "main");

            Assert.That(
                ManifestModelEntry.CanOpenFileSelection(
                    entry,
                    new ManifestModelValidationState(entry.ModelId, entry.Revision, ManifestModelValidationStatus.Valid, null),
                    true),
                Is.True);
        }

        /// <summary>
        /// Ensures rows with pending validation cannot open the file-selection popup.
        /// </summary>
        [Test]
        public void CanOpenFileSelection_ReturnsFalseForUnknownRevisionWhenValidationIsAvailable()
        {
            var entry = new ManifestModelEntry("onnx-community", "tiny-clip-test-ONNX", "main");

            Assert.That(ManifestModelEntry.CanOpenFileSelection(entry, null, true), Is.False);
        }

        /// <summary>
        /// Ensures incomplete model rows cannot open the file-selection popup.
        /// </summary>
        [Test]
        public void CanOpenFileSelection_ReturnsFalseForMissingModelId()
        {
            var entry = new ManifestModelEntry(string.Empty, string.Empty, "main");

            Assert.That(ManifestModelEntry.CanOpenFileSelection(entry, null, true), Is.False);
        }

        /// <summary>
        /// Ensures partial owner/model rows stay incomplete until both text fields have content.
        /// </summary>
        [Test]
        public void HasCompleteModelId_ReturnsTrueOnlyWhenOwnerAndModelArePresent()
        {
            Assert.That(new ManifestModelEntry(string.Empty, "repo", "main").HasCompleteModelId, Is.False);
            Assert.That(new ManifestModelEntry("owner", string.Empty, "main").HasCompleteModelId, Is.False);
            Assert.That(new ManifestModelEntry("owner", "repo", "main").HasCompleteModelId, Is.True);
        }

        /// <summary>
        /// Ensures apply-side row validation rejects both empty rows and partially filled owner/model pairs.
        /// </summary>
        [Test]
        public void IsIncomplete_ReturnsTrueForEmptyOrPartialRows()
        {
            Assert.That(ManifestModelEntry.IsIncomplete(new ManifestModelEntry(string.Empty, string.Empty, "main")), Is.True);
            Assert.That(ManifestModelEntry.IsIncomplete(new ManifestModelEntry("owner", string.Empty, "main")), Is.True);
            Assert.That(ManifestModelEntry.IsIncomplete(new ManifestModelEntry(string.Empty, "repo", "main")), Is.True);
            Assert.That(ManifestModelEntry.IsIncomplete(new ManifestModelEntry("owner", "repo", "main")), Is.False);
        }

        /// <summary>
        /// Ensures the model id field accepts plain ids and pasted Hugging Face URLs.
        /// </summary>
        [TestCase("julienkay/sentis-MiDaS", "julienkay", "sentis-MiDaS")]
        [TestCase("  julienkay/sentis-MiDaS  ", "julienkay", "sentis-MiDaS")]
        [TestCase("https://huggingface.co/julienkay/sentis-MiDaS", "julienkay", "sentis-MiDaS")]
        [TestCase("https://huggingface.co/julienkay/sentis-MiDaS/tree/main/onnx", "julienkay", "sentis-MiDaS")]
        [TestCase("huggingface.co/julienkay/sentis-MiDaS?library=onnx", "julienkay", "sentis-MiDaS")]
        public void TryParseModelId_AcceptsIdsAndUrls(string input, string expectedOwner, string expectedName)
        {
            Assert.That(ManifestModelEntry.TryParseModelId(input, out var owner, out var name), Is.True);
            Assert.That(owner, Is.EqualTo(expectedOwner));
            Assert.That(name, Is.EqualTo(expectedName));
        }

        /// <summary>
        /// Ensures text without both owner and name is rejected.
        /// </summary>
        [TestCase("")]
        [TestCase("sentis-MiDaS")]
        [TestCase("https://huggingface.co/julienkay")]
        public void TryParseModelId_RejectsIncompleteIds(string input)
        {
            Assert.That(ManifestModelEntry.TryParseModelId(input, out _, out _), Is.False);
        }
    }
}
