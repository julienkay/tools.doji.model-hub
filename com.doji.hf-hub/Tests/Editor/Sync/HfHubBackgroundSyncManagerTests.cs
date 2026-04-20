using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers repository filtering used by per-repo background sync actions.
    /// </summary>
    internal sealed class HfHubBackgroundSyncManagerTests
    {
        /// <summary>
        /// Verifies repository-specific sync matches only exact repository and revision.
        /// </summary>
        [Test]
        public void MatchesRepository_ReturnsTrueOnlyForExactRepositoryAndRevision()
        {
            var dependency = new HfHubDiscoveredModelDependency(
                "com.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/com.doji.test",
                "/tmp/com.doji.test/hf-hub-manifest.json",
                "manifesthash",
                new HfHubModelDependencyDefinition(
                    "doji/test-model",
                    "main"));

            Assert.That(HfHubBackgroundSyncManager.MatchesRepository(dependency, "doji/test-model", "main"), Is.True);
            Assert.That(HfHubBackgroundSyncManager.MatchesRepository(dependency, "doji/test-model", "dev"), Is.False);
            Assert.That(HfHubBackgroundSyncManager.MatchesRepository(dependency, "doji/other-model", "main"), Is.False);
        }
    }
}
