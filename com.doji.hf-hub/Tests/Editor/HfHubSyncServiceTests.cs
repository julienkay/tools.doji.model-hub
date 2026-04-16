using System;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers sync orchestration only.
    /// Raw cache and project import are both mocked, so tests verify result aggregation and stage-to-stage handoff
    /// without depending on real downloads, Unity importers, or project asset IO.
    /// </summary>
    internal sealed class HfHubSyncServiceTests
    {
        [Test]
        public async Task SyncAsync_ReturnsSuccessForImportedDependencies()
        {
            // Fake raw cache returns deterministic raw file data.
            // Fake project import then returns deterministic `.sentis` asset path.
            var dependency = CreateDependency("ok-model");
            var discoveryReport = new HfHubDiscoveryReport(
                new[] { dependency },
                Array.Empty<HfHubManifestDiscoveryIssue>());
            var syncService = new HfHubSyncService(new FakeRawCacheService(), new FakeProjectImportService());

            var report = await syncService.SyncAsync(discoveryReport, CancellationToken.None);

            Assert.That(report.SuccessCount, Is.EqualTo(1));
            Assert.That(report.FailureCount, Is.EqualTo(0));
            Assert.That(report.ModelResults[0].Success, Is.True);
            Assert.That(report.ModelResults[0].CachedFile.FilePath, Is.EqualTo("/cache/ok-model.onnx"));
            Assert.That(report.ModelResults[0].ImportedArtifact.SentisAssetPath, Is.EqualTo("Assets/Doji/HfHub/Generated/com.doji.test/ok-model/model.sentis"));
        }

        [Test]
        public async Task SyncAsync_CollectsImportFailuresAndDiscoveryIssues()
        {
            // Fake raw cache succeeds.
            // Fake import throws for one chosen model id so test verifies sync keeps discovery issues and
            // converts conversion/import exceptions into per-model failure result.
            var dependency = CreateDependency("broken-model");
            var discoveryIssue = new HfHubManifestDiscoveryIssue("com.doji.bad", "/tmp/hf-hub-manifest.json", "Bad manifest");
            var discoveryReport = new HfHubDiscoveryReport(
                new[] { dependency },
                new[] { discoveryIssue });
            var syncService = new HfHubSyncService(new FakeRawCacheService(), new FakeProjectImportService("broken-model"));

            var report = await syncService.SyncAsync(discoveryReport, CancellationToken.None);

            Assert.That(report.SuccessCount, Is.EqualTo(0));
            Assert.That(report.FailureCount, Is.EqualTo(1));
            Assert.That(report.DiscoveryIssues.Count, Is.EqualTo(1));
            Assert.That(report.ModelResults[0].Success, Is.False);
            Assert.That(report.ModelResults[0].ErrorMessage, Does.Contain("Simulated import failure"));
        }

        /// <summary>
        /// Creates one normalized dependency directly so test can focus on sync aggregation.
        /// </summary>
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
                    "Test Model",
                    "doji/test-model",
                    "main",
                    "models/model.onnx"));
        }

        /// <summary>
        /// Minimal fake cache service for sync tests.
        /// It always returns synthetic cached raw file data so import stage can be exercised separately.
        /// </summary>
        private sealed class FakeRawCacheService : IHfHubRawCacheService
        {
            public Task<HfHubCachedRawFile> GetOrDownloadAsync(
                HfHubDiscoveredModelDependency dependency,
                IProgress<DownloadProgress> progress,
                CancellationToken cancellationToken)
            {
                var metadata = new HfHubRawCacheMetadata
                {
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = "abc123",
                    SourceFile = dependency.SourceFile,
                    CachedFilePath = $"/cache/{dependency.ModelId}.onnx",
                    DownloadedAtUtc = DateTime.UtcNow.ToString("O")
                };

                return Task.FromResult(new HfHubCachedRawFile(
                    $"/cache/{dependency.ModelId}.onnx",
                    $"/cache/{dependency.ModelId}.onnx.hfhub.json",
                    metadata));
            }
        }

        /// <summary>
        /// Minimal fake project import service for sync tests.
        /// It either returns synthetic generated artifact data or throws a controlled exception for one model id.
        /// </summary>
        private sealed class FakeProjectImportService : IHfHubProjectImportService
        {
            private readonly string _failingModelId;

            public FakeProjectImportService(string failingModelId = null)
            {
                _failingModelId = failingModelId;
            }

            public Task<HfHubImportedModelArtifact> ImportAsync(
                HfHubDiscoveredModelDependency dependency,
                HfHubCachedRawFile cachedRawFile,
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
                    ResolvedCommitHash = cachedRawFile.Metadata.ResolvedCommitHash,
                    SourceFile = dependency.SourceFile,
                    RawFilePath = cachedRawFile.FilePath,
                    SentisVersion = "2.6.1",
                    UnityVersion = "6000.3.10f1",
                    SentisAssetPath = $"Assets/Doji/HfHub/Generated/{dependency.PackageId}/{dependency.ModelId}/model.sentis",
                    ImportedAtUtc = DateTime.UtcNow.ToString("O")
                };

                return Task.FromResult(new HfHubImportedModelArtifact(
                    metadata.SentisAssetPath,
                    $"Assets/Doji/HfHub/Generated/{dependency.PackageId}/{dependency.ModelId}/hf-hub-import.json",
                    metadata));
            }
        }
    }
}
