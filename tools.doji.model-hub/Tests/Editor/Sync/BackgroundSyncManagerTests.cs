using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers repository filtering used by per-repo background sync actions.
    /// </summary>
    internal sealed class BackgroundSyncManagerTests
    {
        private TestCacheScope _cacheScope;
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "Doji.ModelHub.BackgroundSyncManagerTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
            _cacheScope = new TestCacheScope(Path.Combine(_rootDirectory, "SharedCache"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }

            _cacheScope?.Dispose();
            _cacheScope = null;
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
                    "Assets/AI Model Hub/AI Model Hub Project Manifest.asset",
                    null),
                "Project",
                "project",
                "/tmp/project",
                "Assets/AI Model Hub/AI Model Hub Project Manifest.asset",
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
                "tools.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/tools.doji.test",
                "/tmp/tools.doji.test/model-hub-manifest.json",
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
                "tools.doji.invalid",
                "Invalid Package",
                "1.0.0",
                "/tmp/tools.doji.invalid",
                "/tmp/tools.doji.invalid/model-hub-manifest.json",
                new ModelDependencyDefinition(
                    $"doji/invalid-{Guid.NewGuid():N}",
                    "main"));
            var validDependency = new DiscoveredModelDependency(
                "tools.doji.valid",
                "Valid Package",
                "1.0.0",
                "/tmp/tools.doji.valid",
                "/tmp/tools.doji.valid/model-hub-manifest.json",
                new ModelDependencyDefinition(
                    $"doji/valid-{Guid.NewGuid():N}",
                    "main"));
            RepositoryValidationCacheService.WriteCachedRepositoryExists(invalidDependency.Repository, false);

            var discoveryReport = new DiscoveryReport(
                new[] { invalidDependency, validDependency },
                Array.Empty<ManifestDiscoveryIssue>());

            var pendingKeys = BackgroundSyncManager.BuildPendingDependencyKeys(discoveryReport, _ => true);

            Assert.That(pendingKeys, Has.Length.EqualTo(1));
            Assert.That(pendingKeys[0], Is.EqualTo($"tools.doji.valid:{validDependency.ModelId}"));
        }

        /// <summary>
        /// Only damage on disk counts as damaged cache; AI Model Hub's own index never does.
        /// </summary>
        [Test]
        public void DamagedCacheCodes_ContainsExactlyExpectedCodes()
        {
            var expected = new HashSet<DependencyReasonCode>
            {
                DependencyReasonCode.RawCacheRevisionRefUnreadable,
            };

            Assert.That(DependencyStatus.DamagedCacheCodes, Is.EquivalentTo(expected));
        }

        /// <summary>
        /// Verifies DamagedCacheCodes does not include codes that mean a normal download or user choice.
        /// </summary>
        [Test]
        public void DamagedCacheCodes_ExcludesNonCorruptionCodes()
        {
            Assert.That(DependencyStatus.DamagedCacheCodes, Has.No.Member(DependencyReasonCode.InvalidRepository));
            Assert.That(DependencyStatus.DamagedCacheCodes, Has.No.Member(DependencyReasonCode.RawCacheRevisionRefMissing));
            Assert.That(DependencyStatus.DamagedCacheCodes, Has.No.Member(DependencyReasonCode.None));
        }

        /// <summary>
        /// Verifies BuildPendingDependencyKeys returns a damaged-cache dependency when filtered by DamagedCacheCodes.
        /// Damage: the revision ref exists but cannot be read, because another handle holds it exclusively.
        /// </summary>
        [Test]
        public void BuildPendingDependencyKeys_IncludesDamagedDependency_WhenFilteredByDamagedCacheCodes()
        {
            var commitHash = "deadbeef01";
            var modelId = $"doji/corrupt-{Guid.NewGuid():N}";
            var dependency = new DiscoveredModelDependency(
                "tools.doji.corrupt",
                "Corrupt Package",
                "1.0.0",
                "/tmp/tools.doji.corrupt",
                "/tmp/tools.doji.corrupt/model-hub-manifest.json",
                new ModelDependencyDefinition(modelId, "main"));

            var revRefPath = CachePaths.GetRevisionRefPath(modelId, "main");
            Directory.CreateDirectory(Path.GetDirectoryName(revRefPath));
            File.WriteAllText(revRefPath, commitHash);
            Directory.CreateDirectory(CachePaths.GetSnapshotRoot(modelId, commitHash));

            var discoveryReport = new DiscoveryReport(
                new[] { dependency },
                Array.Empty<ManifestDiscoveryIssue>());

            string[] pendingKeys;
            using (new FileStream(revRefPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                pendingKeys = BackgroundSyncManager.BuildPendingDependencyKeys(
                    discoveryReport,
                    dep =>
                    {
                        var statusReport = new StatusService().BuildStatusReport(
                            new DiscoveryReport(new[] { dep }, Array.Empty<ManifestDiscoveryIssue>()));
                        var status = statusReport.DependencyStatuses[0];
                        return DependencyStatus.DamagedCacheCodes.Contains(status.ReasonCode);
                    });
            }

            Assert.That(pendingKeys, Has.Length.EqualTo(1));
            Assert.That(pendingKeys[0], Is.EqualTo($"tools.doji.corrupt:{modelId}"));
        }

        /// <summary>
        /// Verifies BuildPendingDependencyKeys returns nothing when filter is corruption-only
        /// but dependency has no cache at all (RawCacheRevisionRefMissing, not a corruption code).
        /// </summary>
        [Test]
        public void BuildPendingDependencyKeys_ExcludesMissingDependency_WhenFilteredByDamagedCacheCodes()
        {
            var modelId = $"doji/missing-{Guid.NewGuid():N}";
            var dependency = new DiscoveredModelDependency(
                "tools.doji.missing",
                "Missing Package",
                "1.0.0",
                "/tmp/tools.doji.missing",
                "/tmp/tools.doji.missing/model-hub-manifest.json",
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
                    return DependencyStatus.DamagedCacheCodes.Contains(status.ReasonCode);
                });

            Assert.That(pendingKeys, Is.Empty);
        }

        /// <summary>
        /// Without AI Model Hub's index, status reads the snapshot from disk: a selected file that is there counts as
        /// cached, for example when Python's huggingface_hub downloaded it.
        /// </summary>
        [Test]
        public void StatusService_ReadsSnapshotFromDisk_WhenIndexIsMissing()
        {
            var commitHash = "aabbccdd01";
            var modelId = $"doji/disk-{Guid.NewGuid():N}";
            var dependency = CreateDependency(modelId, new[] { new ModelDependencyFileDefinition("onnx/model.onnx") });
            WriteRef(modelId, commitHash);
            var modelPath = CachePaths.GetSnapshotFilePath(modelId, commitHash, "onnx/model.onnx");
            Directory.CreateDirectory(Path.GetDirectoryName(modelPath));
            File.WriteAllText(modelPath, "onnx");

            var status = BuildStatus(dependency);

            Assert.That(status.ReasonCode, Is.EqualTo(DependencyReasonCode.ImportBindingMissing), status.MaintainerMessage);
            Assert.That(status.RawMetadata.Files.Single().CachedFilePath, Is.EqualTo(modelPath));
        }

        /// <summary>
        /// An index entry that describes another repository is ignored. Without a file list, a whole-repository
        /// dependency needs an update instead of being reported as damaged.
        /// </summary>
        [Test]
        public void StatusService_IgnoresIndexForAnotherRepository()
        {
            var commitHash = "aabbccdd02";
            var modelId = $"doji/meta-mismatch-{Guid.NewGuid():N}";
            var dependency = CreateDependency(modelId, null);
            WriteRef(modelId, commitHash);
            Directory.CreateDirectory(CachePaths.GetSnapshotRoot(modelId, commitHash));
            WriteIndex(modelId, commitHash, "doji/other-model");

            var status = BuildStatus(dependency);

            Assert.That(status.ReasonCode, Is.EqualTo(DependencyReasonCode.RawCacheFilesMissing));
            Assert.That(status.MaintainerMessage, Does.Contain("file list is not known"));
        }

        /// <summary>
        /// A repository id that differs only in letter case is the same Hugging Face repository, so the index
        /// still applies.
        /// </summary>
        [Test]
        public void StatusService_UsesIndexWhenRepositoryDiffersOnlyByCase()
        {
            var commitHash = "aabbccdd03";
            var modelId = $"doji/case-{Guid.NewGuid():N}";
            var dependency = CreateDependency(modelId, null);
            WriteRef(modelId, commitHash);
            Directory.CreateDirectory(CachePaths.GetSnapshotRoot(modelId, commitHash));
            WriteIndex(modelId, commitHash, modelId.ToUpperInvariant());

            var status = BuildStatus(dependency);

            Assert.That(status.ReasonCode, Is.EqualTo(DependencyReasonCode.RawCacheFilesMissing));
            Assert.That(status.MaintainerMessage, Does.Contain("model.onnx"));
            Assert.That(DependencyStatus.DamagedCacheCodes.Contains(status.ReasonCode), Is.False);
        }

        private static DiscoveredModelDependency CreateDependency(string modelId, ModelDependencyFileDefinition[] files)
        {
            return new DiscoveredModelDependency(
                "tools.doji.disk",
                "Disk Package",
                "1.0.0",
                "/tmp/tools.doji.disk",
                "/tmp/tools.doji.disk/model-hub-manifest.json",
                new ModelDependencyDefinition(modelId, "main", files));
        }

        private static void WriteRef(string modelId, string commitHash)
        {
            var revRefPath = CachePaths.GetRevisionRefPath(modelId, "main");
            Directory.CreateDirectory(Path.GetDirectoryName(revRefPath));
            File.WriteAllText(revRefPath, commitHash);
        }

        private static void WriteIndex(string modelId, string commitHash, string indexedRepository)
        {
            var indexPath = CachePaths.GetSnapshotMetadataPath(modelId, commitHash);
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath));
            File.WriteAllText(indexPath, Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                Repository = indexedRepository,
                RequestedRevision = "main",
                ResolvedCommitHash = commitHash,
                RepositoryFiles = new[] { new { RepoPath = "model.onnx", SizeInBytes = 1 } },
                Files = Array.Empty<object>()
            }));
        }

        private static DependencyStatus BuildStatus(DiscoveredModelDependency dependency)
        {
            return new StatusService()
                .BuildStatusReport(new DiscoveryReport(new[] { dependency }, Array.Empty<ManifestDiscoveryIssue>()))
                .DependencyStatuses
                .Single();
        }
    }
}
