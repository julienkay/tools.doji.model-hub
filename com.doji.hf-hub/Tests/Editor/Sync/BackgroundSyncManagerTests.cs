using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers repository filtering used by per-repo background sync actions.
    /// </summary>
    internal sealed class BackgroundSyncManagerTests
    {
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "Doji.ModelHub.BackgroundSyncManagerTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
            Environment.SetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT", Path.Combine(_rootDirectory, "SharedCache"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }

            Environment.SetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT", null);
        }


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

        /// <summary>
        /// Verifies CorruptionHealCodes contains exactly the three codes that indicate cache corruption
        /// and not transient-missing or invalid-repo codes.
        /// </summary>
        [Test]
        public void CorruptionHealCodes_ContainsExactlyExpectedCodes()
        {
            var expected = new HashSet<DependencyReasonCode>
            {
                DependencyReasonCode.RawCacheMetadataInvalid,
                DependencyReasonCode.RawCacheMetadataMismatch,
                DependencyReasonCode.RawCacheRevisionRefUnreadable,
            };

            Assert.That(BackgroundSyncManager.CorruptionHealCodes, Is.EquivalentTo(expected));
        }

        /// <summary>
        /// Verifies CorruptionHealCodes does not include codes that should NOT trigger auto-heal.
        /// </summary>
        [Test]
        public void CorruptionHealCodes_ExcludesNonCorruptionCodes()
        {
            Assert.That(BackgroundSyncManager.CorruptionHealCodes, Has.No.Member(DependencyReasonCode.InvalidRepository));
            Assert.That(BackgroundSyncManager.CorruptionHealCodes, Has.No.Member(DependencyReasonCode.RawCacheRevisionRefMissing));
            Assert.That(BackgroundSyncManager.CorruptionHealCodes, Has.No.Member(DependencyReasonCode.None));
        }

        /// <summary>
        /// Verifies BuildPendingDependencyKeys returns a corrupt dependency when filtered by CorruptionHealCodes.
        /// Corruption state: rev ref exists, snapshot dir exists, metadata JSON is absent.
        /// </summary>
        [Test]
        public void BuildPendingDependencyKeys_IncludesCorruptDependency_WhenFilteredByCorruptionHealCodes()
        {
            var commitHash = "deadbeef01";
            var modelId = $"doji/corrupt-{Guid.NewGuid():N}";
            var dependency = new DiscoveredModelDependency(
                "com.doji.corrupt",
                "Corrupt Package",
                "1.0.0",
                "/tmp/com.doji.corrupt",
                "/tmp/com.doji.corrupt/hf-hub-manifest.json",
                "manifesthash-corrupt",
                new ModelDependencyDefinition(modelId, "main"));

            var revRefPath = CachePaths.GetRevisionRefPath(modelId, "main");
            Directory.CreateDirectory(Path.GetDirectoryName(revRefPath));
            File.WriteAllText(revRefPath, commitHash);

            var snapshotRoot = CachePaths.GetSnapshotRoot(modelId, commitHash);
            Directory.CreateDirectory(snapshotRoot);

            var discoveryReport = new DiscoveryReport(
                new[] { dependency },
                Array.Empty<ManifestDiscoveryIssue>());

            var pendingKeys = BackgroundSyncManager.BuildPendingDependencyKeys(
                discoveryReport,
                dep =>
                {
                    var statusReport = new StatusService().BuildStatusReport(
                        new DiscoveryReport(new[] { dep }, Array.Empty<ManifestDiscoveryIssue>()));
                    var status = statusReport.DependencyStatuses[0];
                    return BackgroundSyncManager.CorruptionHealCodes.Contains(status.ReasonCode);
                });

            Assert.That(pendingKeys, Has.Length.EqualTo(1));
            Assert.That(pendingKeys[0], Is.EqualTo($"com.doji.corrupt:{modelId}"));
        }

        /// <summary>
        /// Verifies BuildPendingDependencyKeys returns nothing when filter is corruption-only
        /// but dependency has no cache at all (RawCacheRevisionRefMissing, not a corruption code).
        /// </summary>
        [Test]
        public void BuildPendingDependencyKeys_ExcludesMissingDependency_WhenFilteredByCorruptionHealCodes()
        {
            var modelId = $"doji/missing-{Guid.NewGuid():N}";
            var dependency = new DiscoveredModelDependency(
                "com.doji.missing",
                "Missing Package",
                "1.0.0",
                "/tmp/com.doji.missing",
                "/tmp/com.doji.missing/hf-hub-manifest.json",
                "manifesthash-missing",
                new ModelDependencyDefinition(modelId, "main"));

            var discoveryReport = new DiscoveryReport(
                new[] { dependency },
                Array.Empty<ManifestDiscoveryIssue>());

            var pendingKeys = BackgroundSyncManager.BuildPendingDependencyKeys(
                discoveryReport,
                dep =>
                {
                    var statusReport = new StatusService().BuildStatusReport(
                        new DiscoveryReport(new[] { dep }, Array.Empty<ManifestDiscoveryIssue>()));
                    var status = statusReport.DependencyStatuses[0];
                    return BackgroundSyncManager.CorruptionHealCodes.Contains(status.ReasonCode);
                });

            Assert.That(pendingKeys, Is.Empty);
        }

        /// <summary>
        /// Verifies that a corrupt dependency's reason code is RawCacheMetadataInvalid when
        /// the revision ref and snapshot dir exist but the metadata JSON is absent.
        /// </summary>
        [Test]
        public void StatusService_ReturnsRawCacheMetadataInvalid_WhenSnapshotDirExistsButMetadataAbsent()
        {
            var commitHash = "aabbccdd01";
            var modelId = $"doji/meta-invalid-{Guid.NewGuid():N}";
            var dependency = new DiscoveredModelDependency(
                "com.doji.meta-invalid",
                "Meta Invalid Package",
                "1.0.0",
                "/tmp/com.doji.meta-invalid",
                "/tmp/com.doji.meta-invalid/hf-hub-manifest.json",
                "manifesthash-metainvalid",
                new ModelDependencyDefinition(modelId, "main"));

            var revRefPath = CachePaths.GetRevisionRefPath(modelId, "main");
            Directory.CreateDirectory(Path.GetDirectoryName(revRefPath));
            File.WriteAllText(revRefPath, commitHash);

            var snapshotRoot = CachePaths.GetSnapshotRoot(modelId, commitHash);
            Directory.CreateDirectory(snapshotRoot);

            var discoveryReport = new DiscoveryReport(
                new[] { dependency },
                Array.Empty<ManifestDiscoveryIssue>());
            var statusReport = new StatusService().BuildStatusReport(discoveryReport);

            Assert.That(statusReport.DependencyStatuses, Has.Length.EqualTo(1));
            Assert.That(statusReport.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.RawCacheMetadataInvalid));
        }

        /// <summary>
        /// Verifies StatusService returns RawCacheMetadataMismatch when metadata repository field
        /// does not match the declared dependency repository.
        /// </summary>
        [Test]
        public void StatusService_ReturnsRawCacheMetadataMismatch_WhenMetadataRepositoryDiffers()
        {
            var commitHash = "aabbccdd02";
            var modelId = $"doji/meta-mismatch-{Guid.NewGuid():N}";
            var dependency = new DiscoveredModelDependency(
                "com.doji.meta-mismatch",
                "Meta Mismatch Package",
                "1.0.0",
                "/tmp/com.doji.meta-mismatch",
                "/tmp/com.doji.meta-mismatch/hf-hub-manifest.json",
                "manifesthash-metamismatch",
                new ModelDependencyDefinition(modelId, "main"));

            var revRefPath = CachePaths.GetRevisionRefPath(modelId, "main");
            Directory.CreateDirectory(Path.GetDirectoryName(revRefPath));
            File.WriteAllText(revRefPath, commitHash);

            var snapshotRoot = CachePaths.GetSnapshotRoot(modelId, commitHash);
            Directory.CreateDirectory(snapshotRoot);

            var metadataPath = CachePaths.GetSnapshotMetadataPath(modelId, commitHash);
            var mismatchedMetadata = new
            {
                Repository = "doji/other-model",
                RequestedRevision = "main",
                ResolvedCommitHash = commitHash,
                RepositoryFiles = Array.Empty<object>(),
                Files = Array.Empty<object>()
            };
            File.WriteAllText(metadataPath, Newtonsoft.Json.JsonConvert.SerializeObject(mismatchedMetadata));

            var discoveryReport = new DiscoveryReport(
                new[] { dependency },
                Array.Empty<ManifestDiscoveryIssue>());
            var statusReport = new StatusService().BuildStatusReport(discoveryReport);

            Assert.That(statusReport.DependencyStatuses, Has.Length.EqualTo(1));
            Assert.That(statusReport.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.RawCacheMetadataMismatch));
        }
    }
}
