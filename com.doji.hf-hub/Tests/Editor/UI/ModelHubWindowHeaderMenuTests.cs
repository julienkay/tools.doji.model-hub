using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers top-toolbar add menu labels and tooltips.
    /// </summary>
    internal sealed class ModelHubWindowHeaderMenuTests
    {
        /// <summary>
        /// Verifies toolbar add-menu labels stay aligned with expected UX copy.
        /// </summary>
        [Test]
        public void HeaderAddMenu_UsesExpectedLabelsAndTooltips()
        {
            Assert.That(ModelHubWindowTestContent.AddModelToProjectLabel, Is.EqualTo("Add model to project..."));
            Assert.That(ModelHubWindowTestContent.AddModelToProjectTooltip, Is.EqualTo("Add one model entry to the project-scoped manifest stored under ProjectSettings."));
            Assert.That(ModelHubWindowTestContent.AddModelToPackageLabel, Is.EqualTo("Add model to package..."));
            Assert.That(ModelHubWindowTestContent.AddModelToPackageTooltip, Is.EqualTo("Create or edit hf-hub-manifest.json in your package root."));
        }

        private static class ModelHubWindowTestContent
        {
            public const string AddModelToProjectLabel = "Add model to project...";
            public const string AddModelToProjectTooltip = "Add one model entry to the project-scoped manifest stored under ProjectSettings.";
            public const string AddModelToPackageLabel = "Add model to package...";
            public const string AddModelToPackageTooltip = "Create or edit hf-hub-manifest.json in your package root.";
        }
    }
}
