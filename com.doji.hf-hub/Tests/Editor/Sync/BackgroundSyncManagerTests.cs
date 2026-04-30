using NUnit.Framework;
using System.IO;
using System;

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

        /// <summary>
        /// Verifies scoped per-repository sync starts from a clean persisted queue instead of resuming unrelated old work.
        /// </summary>
        [Test]
        public void ClearPersistedStateForNewScopedSync_RemovesSavedQueueFile()
        {
            var statePath = BackgroundSyncManager.GetPersistedStatePath();
            var directory = Path.GetDirectoryName(statePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(statePath, "stale queue");

            BackgroundSyncManager.ClearPersistedStateForNewScopedSync();

            Assert.That(File.Exists(statePath), Is.False);
        }

        [Test]
        public void BuildPendingDependencyKeys_SkipsRepositoriesKnownInvalid()
        {
            var invalidDependency = new DiscoveredModelDependency(
                "com.doji.invalid",
                "Invalid Package",
                "1.0.0",
                "/tmp/com.doji.invalid",
                "/tmp/com.doji.invalid/hf-hub-manifest.json",
                "manifesthash-invalid",
                new ModelDependencyDefinition(
                    $"doji/invalid-{Guid.NewGuid():N}",
                    "main"));
            var validDependency = new DiscoveredModelDependency(
                "com.doji.valid",
                "Valid Package",
                "1.0.0",
                "/tmp/com.doji.valid",
                "/tmp/com.doji.valid/hf-hub-manifest.json",
                "manifesthash-valid",
                new ModelDependencyDefinition(
                    $"doji/valid-{Guid.NewGuid():N}",
                    "main"));
            RepositoryValidationCacheService.WriteCachedRepositoryExists(invalidDependency.Repository, false);

            var discoveryReport = new DiscoveryReport(
                new[] { invalidDependency, validDependency },
                Array.Empty<ManifestDiscoveryIssue>());

            var pendingKeys = BackgroundSyncManager.BuildPendingDependencyKeys(discoveryReport, _ => true);

            Assert.That(pendingKeys, Has.Length.EqualTo(1));
            Assert.That(pendingKeys[0], Is.EqualTo($"com.doji.valid:{validDependency.ModelId}"));
        }
    }
}
