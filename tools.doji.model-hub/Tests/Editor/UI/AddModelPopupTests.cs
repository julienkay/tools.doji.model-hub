using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers when the Add Model popup lets a model be added for its repository format.
    /// </summary>
    internal sealed class AddModelPopupTests
    {
        [TestCase(RepositoryModelFormat.Mixed, 0, true)]
        [TestCase(RepositoryModelFormat.Mixed, 1, false)]
        [TestCase(RepositoryModelFormat.None, 0, true)]
        [TestCase(RepositoryModelFormat.Onnx, 0, false)]
        [TestCase(RepositoryModelFormat.Sentis, 0, false)]
        public void GetFormatProblem_AsksForFilesOnlyWhenTheWholeRepositoryCannotLoad(RepositoryModelFormat format, int selected, bool blocked)
        {
            Assert.That(AddModelPopup.GetFormatProblem(format, selected) != null, Is.EqualTo(blocked));
        }

        [Test]
        public void GetFormatProblem_DoesNotBlockWhileTheFormatIsUnknown()
        {
            Assert.That(AddModelPopup.GetFormatProblem(null, 0), Is.Null);
        }
    }
}