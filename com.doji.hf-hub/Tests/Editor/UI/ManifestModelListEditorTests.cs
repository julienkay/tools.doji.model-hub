using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers manifest row UI rules that should stay consistent across manifest editing surfaces.
    /// </summary>
    internal sealed class ManifestModelListEditorTests
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

            Assert.That(ManifestModelListEditor.CanOpenFileSelection(entry, validationState, true), Is.False);
        }

        /// <summary>
        /// Ensures valid repository revisions can open the file-selection popup.
        /// </summary>
        [Test]
        public void CanOpenFileSelection_ReturnsTrueForValidRevision()
        {
            var entry = new ManifestModelEntry("onnx-community", "tiny-clip-test-ONNX", "main");

            Assert.That(
                ManifestModelListEditor.CanOpenFileSelection(
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

            Assert.That(ManifestModelListEditor.CanOpenFileSelection(entry, null, true), Is.False);
        }

        /// <summary>
        /// Ensures incomplete model rows cannot open the file-selection popup.
        /// </summary>
        [Test]
        public void CanOpenFileSelection_ReturnsFalseForMissingModelId()
        {
            var entry = new ManifestModelEntry(string.Empty, string.Empty, "main");

            Assert.That(ManifestModelListEditor.CanOpenFileSelection(entry, null, true), Is.False);
        }
    }
}
