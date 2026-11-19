using System;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers sync orchestration using repo-snapshot cache and import fakes.
    /// </summary>
    internal sealed class SyncServiceTests
    {
        /// <summary>
        /// Verifies successful sync returns cached snapshot and imported runtime snapshot artifact.
        /// </summary>
        [Test]
        public async Task SyncAsync_ReturnsSuccessForImportedDependencies()
        {
            var dependency = CreateDependency("doji/test-model");
            var discoveryReport = new DiscoveryReport(
                new[] { dependency },
                Array.Empty<ManifestDiscoveryIssue>());
            var syncService = new SyncService(new FakeRawCacheService(), new FakeProjectImportService());

            var report = await syncService.SyncAsync(discoveryReport, CancellationToken.None);

            Assert.That(report.SuccessCount, Is.EqualTo(1));
            Assert.That(report.FailureCount, Is.EqualTo(0));
            Assert.That(report.ModelResults[0].Success, Is.True);
            Assert.That(report.ModelResults[0].CachedFile.SnapshotRootPath, Is.EqualTo("/cache/doji/test-model/abc123"));
            Assert.That(report.ModelResults[0].ImportedArtifact, Is.Not.Null);
        }

        /// <summary>
        /// Verifies import failures are aggregated without dropping discovery issues.
        /// </summary>
        [Test]
        public async Task SyncAsync_CollectsImportFailuresAndDiscoveryIssues()
        {
            var dependency = CreateDependency("doji/broken-model");
            var discoveryIssue = new ManifestDiscoveryIssue("tools.doji.bad", "/tmp/model-hub-manifest.json", "Bad manifest");
            var discoveryReport = new DiscoveryReport(
                new[] { dependency },
                new[] { discoveryIssue });
            var syncService = new SyncService(new FakeRawCacheService(), new FakeProjectImportService("doji/broken-model"));

            var report = await syncService.SyncAsync(discoveryReport, CancellationToken.None);

            Assert.That(report.SuccessCount, Is.EqualTo(0));
            Assert.That(report.FailureCount, Is.EqualTo(1));
            Assert.That(report.DiscoveryIssues.Count, Is.EqualTo(1));
            Assert.That(report.ModelResults[0].Success, Is.False);
            Assert.That(report.ModelResults[0].ErrorMessage, Does.Contain("Simulated import failure"));
        }

        /// <summary>
        /// Verifies sync blocks repositories that are declared at multiple requested revisions.
        /// </summary>
        [Test]
        public async Task SyncAsync_FailsConflictingRepositoryRevisionsWithoutDownloadingOrImporting()
        {
            var mainDependency = CreateDependency("doji/test-model", "tools.doji.main", "main");
            var releaseDependency = CreateDependency("doji/test-model", "tools.doji.release", "refs/tags/v1");
            var discoveryReport = new DiscoveryReport(
                new[] { mainDependency, releaseDependency },
                Array.Empty<ManifestDiscoveryIssue>());
            var rawCacheService = new FakeRawCacheService();
            var importService = new FakeProjectImportService();
            var syncService = new SyncService(rawCacheService, importService);

            var report = await syncService.SyncAsync(discoveryReport, CancellationToken.None);

            Assert.That(report.SuccessCount, Is.EqualTo(0));
            Assert.That(report.FailureCount, Is.EqualTo(2));
            Assert.That(report.ModelResults[0].ErrorMessage, Does.Contain("multiple revisions"));
            Assert.That(report.ModelResults[1].ErrorMessage, Does.Contain("multiple revisions"));
            Assert.That(rawCacheService.CallCount, Is.EqualTo(0));
            Assert.That(importService.CallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task SyncAsync_DownloadOnly_SkipsMaterialization()
        {
            var dependency = CreateDependency("doji/test-model");
            var discoveryReport = new DiscoveryReport(
                new[] { dependency },
                Array.Empty<ManifestDiscoveryIssue>());
            var rawCacheService = new FakeRawCacheService();
            var importService = new FakeProjectImportService();
            var syncService = new SyncService(rawCacheService, importService);

            var report = await syncService.SyncAsync(discoveryReport, SyncOperationMode.DownloadOnly, CancellationToken.None);

            Assert.That(report.SuccessCount, Is.EqualTo(1));
            Assert.That(report.ModelResults[0].Mode, Is.EqualTo(SyncOperationMode.DownloadOnly));
            Assert.That(report.ModelResults[0].CachedFile, Is.Not.Null);
            Assert.That(report.ModelResults[0].ImportedArtifact, Is.Null);
            Assert.That(rawCacheService.CallCount, Is.EqualTo(1));
            Assert.That(importService.CallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task SyncAsync_MaterializeOnly_UsesCachedSnapshotWithoutDownload()
        {
            var dependency = CreateDependency("doji/test-model");
            var discoveryReport = new DiscoveryReport(
                new[] { dependency },
                Array.Empty<ManifestDiscoveryIssue>());
            var rawCacheService = new FakeRawCacheService();
            var importService = new FakeProjectImportService();
            var syncService = new SyncService(rawCacheService, importService);

            var report = await syncService.SyncAsync(discoveryReport, SyncOperationMode.MaterializeOnly, CancellationToken.None);

            Assert.That(report.SuccessCount, Is.EqualTo(1));
            Assert.That(report.ModelResults[0].Mode, Is.EqualTo(SyncOperationMode.MaterializeOnly));
            Assert.That(report.ModelResults[0].ImportedArtifact, Is.Not.Null);
            Assert.That(rawCacheService.CallCount, Is.EqualTo(0));
            Assert.That(rawCacheService.GetCachedCallCount, Is.EqualTo(1));
            Assert.That(importService.CallCount, Is.EqualTo(1));
        }

        private static DiscoveredModelDependency CreateDependency(
            string modelId,
            string packageId = "tools.doji.test",
            string revision = "main")
        {
            return new DiscoveredModelDependency(
                packageId,
                "Test Package",
                "1.0.0",
                $"/tmp/{packageId}",
                $"/tmp/{packageId}/model-hub-manifest.json",
                new ModelDependencyDefinition(
                    modelId,
                    revision));
        }

        private sealed class FakeRawCacheService : IRawCacheService
        {
            public int CallCount { get; private set; }
            public int GetCachedCallCount { get; private set; }

            public Task<CachedSnapshot> GetOrDownloadAsync(
                DiscoveredModelDependency dependency,
                IProgress<DownloadProgress> progress,
                CancellationToken cancellationToken)
            {
                CallCount++;
                var metadata = new RawCacheMetadata
                {
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = "abc123",
                    CachedSnapshotRoot = "/cache/doji/test-model/abc123",
                    Files = new[]
                    {
                        new RawCacheFileMetadata
                        {
                            RepoPath = "models/model.onnx",
                            CachedFilePath = "/cache/doji/test-model/abc123/models/model.onnx"
                        }
                    },
                    DownloadedAtUtc = DateTime.UtcNow.ToString("O")
                };

                return Task.FromResult(new CachedSnapshot(
                    "/cache/doji/test-model/abc123",
                    "/cache/doji/test-model/abc123/snapshot.modelhub.json",
                    metadata));
            }

            public CachedSnapshot GetCachedOrThrow(DiscoveredModelDependency dependency)
            {
                GetCachedCallCount++;
                var metadata = new RawCacheMetadata
                {
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = "abc123",
                    CachedSnapshotRoot = "/cache/doji/test-model/abc123",
                    Files = new[]
                    {
                        new RawCacheFileMetadata
                        {
                            RepoPath = "models/model.onnx",
                            CachedFilePath = "/cache/doji/test-model/abc123/models/model.onnx"
                        }
                    },
                    DownloadedAtUtc = DateTime.UtcNow.ToString("O")
                };

                return new CachedSnapshot(
                    "/cache/doji/test-model/abc123",
                    "/cache/doji/test-model/abc123/snapshot.modelhub.json",
                    metadata);
            }
        }

        private sealed class FakeProjectImportService : IProjectImportService
        {
            private readonly string _failingModelId;
            public int CallCount { get; private set; }

            public FakeProjectImportService(string failingModelId = null)
            {
                _failingModelId = failingModelId;
            }

            public Task<ImportedModelArtifact> ImportAsync(
                DiscoveredModelDependency dependency,
                CachedSnapshot cachedSnapshot,
                CancellationToken cancellationToken,
                bool allowConversion = true)
            {
                CallCount++;
                if (dependency.ModelId == _failingModelId)
                {
                    throw new InvalidOperationException("Simulated import failure");
                }

                var metadata = new ImportedModelMetadata
                {
                    PackageId = dependency.PackageId,
                    ModelId = dependency.ModelId,
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = cachedSnapshot.Metadata.ResolvedCommitHash,
                    RawSnapshotRootPath = cachedSnapshot.SnapshotRootPath,
                    RawMetadataPath = cachedSnapshot.MetadataPath,
                    SentisVersion = "2.6.1",
                    UnityVersion = "6000.3.10f1",
                    RuntimeFiles = new[]
                    {
                        new ImportedRuntimeFileMetadata
                        {
                            RepoPath = "models/model.onnx",
                            Kind = "sentis",
                            Format = "onnx",
                            FilePath = "/data/converted/models--doji--test-model/key.sentis"
                        }
                    },
                    ImportedAtUtc = DateTime.UtcNow.ToString("O")
                };

                return Task.FromResult(new ImportedModelArtifact(
                    ProjectPaths.GetImportBindingMetadataFilePath(dependency.PackageId, dependency.ModelId),
                    metadata,
                    ProjectPaths.GetSharedImportMetadataFilePath(dependency.Repository, cachedSnapshot.Metadata.ResolvedCommitHash, "2.6.1", new ProjectModelRecipeSource().ComputeRecipeSetHash(dependency.Repository, new[] { "models/model.onnx" }))));
            }
        }
    }
}
