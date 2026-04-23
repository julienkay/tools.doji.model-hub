using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers repository filtering used by per-repo background sync actions.
    /// </summary>
    internal sealed class BackgroundSyncManagerTests
    {
        /// <summary>
        /// Verifies background sync labels keep project manifest owners visible as Project.
        /// </summary>
        [Test]
        public void GetDependencyDisplayLabel_UsesProjectLabelForProjectManifestOwners()
        {
            var dependency = new DiscoveredModelDependency(
                ProjectManifestDiscovery.GetOwnerId(
                    "5a899f24b6fd4a1ba127ab1a1b267c7e",
                    "Assets/HF Hub/HF Hub Project Manifest.asset",
                    null),
                "Project",
                "project",
                "/tmp/project",
                "Assets/HF Hub/HF Hub Project Manifest.asset",
                "manifesthash",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "main"));

            Assert.That(BackgroundSyncManager.GetDependencyDisplayLabel(dependency), Is.EqualTo("Project/doji/test-model"));
        }

        /// <summary>
        /// Verifies repository-specific sync matches only exact repository and revision.
        /// </summary>
        [Test]
        public void MatchesRepository_ReturnsTrueOnlyForExactRepositoryAndRevision()
        {
            var dependency = new DiscoveredModelDependency(
                "com.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/com.doji.test",
                "/tmp/com.doji.test/hf-hub-manifest.json",
                "manifesthash",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "main"));

            Assert.That(BackgroundSyncManager.MatchesRepository(dependency, "doji/test-model", "main"), Is.True);
            Assert.That(BackgroundSyncManager.MatchesRepository(dependency, "doji/test-model", "dev"), Is.False);
            Assert.That(BackgroundSyncManager.MatchesRepository(dependency, "doji/other-model", "main"), Is.False);
        }
    }
}
