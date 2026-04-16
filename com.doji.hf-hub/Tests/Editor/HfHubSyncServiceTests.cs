using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers sync orchestration only.
    /// Raw cache interaction is mocked through `FakeRawCacheService`, so tests verify aggregation of success, failure,
    /// and discovery issues without depending on real downloads or cache IO.
    /// </summary>
    internal sealed class HfHubSyncServiceTests
    {
        [Test]
        public async Task SyncAsync_ReturnsSuccessForCachedDependencies()
        {
            // Fake raw cache always succeeds and returns deterministic cached file path.
            var dependency = CreateDependency("ok-model");
            var discoveryReport = new HfHubDiscoveryReport(
                new[] { dependency },
                Array.Empty<HfHubManifestDiscoveryIssue>());
            var syncService = new HfHubSyncService(new FakeRawCacheService());

            var report = await syncService.SyncAsync(discoveryReport, CancellationToken.None);

            Assert.That(report.SuccessCount, Is.EqualTo(1));
            Assert.That(report.FailureCount, Is.EqualTo(0));
            Assert.That(report.ModelResults[0].Success, Is.True);
            Assert.That(report.ModelResults[0].CachedFile.FilePath, Is.EqualTo("/cache/ok-model.onnx"));
        }

        [Test]
        public async Task SyncAsync_CollectsFailuresAndDiscoveryIssues()
        {
            // Fake raw cache throws for one chosen model id.
            // Test verifies sync keeps discovery issues and converts exception into per-model failure result.
            var dependency = CreateDependency("broken-model");
            var discoveryIssue = new HfHubManifestDiscoveryIssue("com.doji.bad", "/tmp/hf-hub-manifest.json", "Bad manifest");
            var discoveryReport = new HfHubDiscoveryReport(
                new[] { dependency },
                new[] { discoveryIssue });
            var syncService = new HfHubSyncService(new FakeRawCacheService("broken-model"));

            var report = await syncService.SyncAsync(discoveryReport, CancellationToken.None);

            Assert.That(report.SuccessCount, Is.EqualTo(0));
            Assert.That(report.FailureCount, Is.EqualTo(1));
            Assert.That(report.DiscoveryIssues.Count, Is.EqualTo(1));
            Assert.That(report.ModelResults[0].Success, Is.False);
            Assert.That(report.ModelResults[0].ErrorMessage, Does.Contain("Simulated download failure"));
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
        /// It either returns synthetic cached file data or throws a controlled exception for one model id.
        /// </summary>
        private sealed class FakeRawCacheService : IHfHubRawCacheService
        {
            private readonly string _failingModelId;

            public FakeRawCacheService(string failingModelId = null)
            {
                _failingModelId = failingModelId;
            }

            public Task<HfHubCachedRawFile> GetOrDownloadAsync(
                HfHubDiscoveredModelDependency dependency,
                CancellationToken cancellationToken)
            {
                if (dependency.ModelId == _failingModelId)
                {
                    throw new InvalidOperationException("Simulated download failure");
                }

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
    }
}
