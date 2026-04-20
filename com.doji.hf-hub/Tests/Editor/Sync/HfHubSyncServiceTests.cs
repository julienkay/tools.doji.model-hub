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
    internal sealed class HfHubSyncServiceTests
    {
        /// <summary>
        /// Verifies successful sync returns cached snapshot and imported runtime snapshot artifact.
        /// </summary>
        [Test]
        public async Task SyncAsync_ReturnsSuccessForImportedDependencies()
        {
            var dependency = CreateDependency("doji/test-model");
            var discoveryReport = new HfHubDiscoveryReport(
                new[] { dependency },
                Array.Empty<HfHubManifestDiscoveryIssue>());
            var registryService = new FakeRuntimeLocatorRegistryService();
            var syncService = new HfHubSyncService(new FakeRawCacheService(), new FakeProjectImportService(), registryService);

            var report = await syncService.SyncAsync(discoveryReport, CancellationToken.None);

            Assert.That(report.SuccessCount, Is.EqualTo(1));
            Assert.That(report.FailureCount, Is.EqualTo(0));
            Assert.That(report.ModelResults[0].Success, Is.True);
            Assert.That(report.ModelResults[0].CachedFile.SnapshotRootPath, Is.EqualTo("/cache/doji/test-model/abc123"));
            Assert.That(report.ModelResults[0].ImportedArtifact.SnapshotRootAssetPath, Is.EqualTo("Assets/StreamingAssets/hf-hub/doji/test-model"));
            Assert.That(registryService.CallCount, Is.EqualTo(1));
            Assert.That(registryService.LastResults.Length, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies import failures are aggregated without dropping discovery issues.
        /// </summary>
        [Test]
        public async Task SyncAsync_CollectsImportFailuresAndDiscoveryIssues()
        {
            var dependency = CreateDependency("doji/broken-model");
            var discoveryIssue = new HfHubManifestDiscoveryIssue("com.doji.bad", "/tmp/hf-hub-manifest.json", "Bad manifest");
            var discoveryReport = new HfHubDiscoveryReport(
                new[] { dependency },
                new[] { discoveryIssue });
            var registryService = new FakeRuntimeLocatorRegistryService();
            var syncService = new HfHubSyncService(new FakeRawCacheService(), new FakeProjectImportService("doji/broken-model"), registryService);

            var report = await syncService.SyncAsync(discoveryReport, CancellationToken.None);

            Assert.That(report.SuccessCount, Is.EqualTo(0));
            Assert.That(report.FailureCount, Is.EqualTo(1));
            Assert.That(report.DiscoveryIssues.Count, Is.EqualTo(1));
            Assert.That(report.ModelResults[0].Success, Is.False);
            Assert.That(report.ModelResults[0].ErrorMessage, Does.Contain("Simulated import failure"));
            Assert.That(registryService.CallCount, Is.EqualTo(1));
        }

        private static HfHubDiscoveredModelDependency CreateDependency(string modelId)
        {
            return new HfHubDiscoveredModelDependency(
                "com.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/com.doji.test",
                "/tmp/com.doji.test/hf-hub-manifest.json",
                "manifesthash",
                new HfHubModelDependencyDefinition(
                    modelId,
                    "main"));
        }

        private sealed class FakeRawCacheService : IHfHubRawCacheService
        {
            public Task<HfHubCachedSnapshot> GetOrDownloadAsync(
                HfHubDiscoveredModelDependency dependency,
                IProgress<DownloadProgress> progress,
                CancellationToken cancellationToken)
            {
                var metadata = new HfHubRawCacheMetadata
                {
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = "abc123",
                    CachedSnapshotRoot = "/cache/doji/test-model/abc123",
                    Files = new[]
                    {
                        new HfHubRawCacheFileMetadata
                        {
                            RepoPath = "models/model.onnx",
                            CachedFilePath = "/cache/doji/test-model/abc123/models/model.onnx"
                        }
                    },
                    DownloadedAtUtc = DateTime.UtcNow.ToString("O")
                };

                return Task.FromResult(new HfHubCachedSnapshot(
                    "/cache/doji/test-model/abc123",
                    "/cache/doji/test-model/abc123/snapshot.hfhub.json",
                    metadata));
            }
        }

        private sealed class FakeProjectImportService : IHfHubProjectImportService
        {
            private readonly string _failingModelId;

            public FakeProjectImportService(string failingModelId = null)
            {
                _failingModelId = failingModelId;
            }

            public Task<HfHubImportedModelArtifact> ImportAsync(
                HfHubDiscoveredModelDependency dependency,
                HfHubCachedSnapshot cachedSnapshot,
                CancellationToken cancellationToken)
            {
                if (dependency.ModelId == _failingModelId)
                {
                    throw new InvalidOperationException("Simulated import failure");
                }

                var metadata = new HfHubImportedModelMetadata
                {
                    PackageId = dependency.PackageId,
                    ModelId = dependency.ModelId,
                    ManifestHash = dependency.ManifestHash,
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = cachedSnapshot.Metadata.ResolvedCommitHash,
                    RawSnapshotRootPath = cachedSnapshot.SnapshotRootPath,
                    RawMetadataPath = cachedSnapshot.MetadataPath,
                    SentisVersion = "2.6.1",
                    UnityVersion = "6000.3.10f1",
                    RuntimeFiles = new[]
                    {
                        new HfHubImportedRuntimeFileMetadata
                        {
                            RepoPath = "models/model.onnx",
                            Kind = "sentis",
                            AssetPath = "Assets/StreamingAssets/hf-hub/doji/test-model/models/model.sentis",
                            RelativePath = "hf-hub/doji/test-model/models/model.sentis"
                        }
                    },
                    ImportedAtUtc = DateTime.UtcNow.ToString("O")
                };

                return Task.FromResult(new HfHubImportedModelArtifact(
                    "Assets/StreamingAssets/hf-hub/doji/test-model",
                    $"Library/Doji/HfHub/import-state/{dependency.PackageId}/{dependency.ModelId}/hf-hub-import.json",
                    metadata));
            }
        }

        private sealed class FakeRuntimeLocatorRegistryService : IHfHubRuntimeLocatorRegistryService
        {
            public int CallCount { get; private set; }
            public HfHubModelSyncResult[] LastResults { get; private set; }

            public void WriteRegistries(System.Collections.Generic.IReadOnlyList<HfHubModelSyncResult> modelResults)
            {
                CallCount++;
                LastResults = modelResults == null
                    ? Array.Empty<HfHubModelSyncResult>()
                    : System.Linq.Enumerable.ToArray(modelResults);
            }
        }
    }
}
