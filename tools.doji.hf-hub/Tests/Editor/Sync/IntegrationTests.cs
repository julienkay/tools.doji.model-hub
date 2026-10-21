using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Real network integration tests against public HuggingFace repositories.
    /// These tests use real `HttpClient` and `RawCacheService`, isolate cache root into temp folder,
    /// and verify end-to-end metadata resolution plus raw cache download for known public entries.
    ///
    /// These tests are marked with `Explicit` on purpose:
    /// - they depend on live public network access
    /// - they depend on external repository contents staying available
    /// - they are slower and less deterministic than normal editor unit tests
    ///
    /// Normal "run all tests" should stay fast and stable, so these only run when selected intentionally.
    /// </summary>
    internal sealed class IntegrationTests
    {
        private TestCacheScope _cacheScope;
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "Doji.ModelHub.IntegrationTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
            _cacheScope = new TestCacheScope(Path.Combine(_rootDirectory, "SharedCache"));
        }

        [TearDown]
        public void TearDown()
        {
            _cacheScope?.Dispose();
            _cacheScope = null;
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        /// <summary>
        /// Uses a small public dummy model repo.
        /// Verifies live repo resolution, commit hash capture, and raw snapshot download.
        /// `Explicit` means this runs only when intentionally selected in Unity Test Runner.
        /// </summary>
        [Test]
        [Explicit("Network integration test against live HuggingFace Hub.")]
        public async Task SyncDummyRepository_ResolvesAndCachesSnapshot()
        {
            var dependency = CreateDependency(
                packageId: "tools.doji.integration.dummy",
                modelId: "hf-internal-testing/huggingface-hub-dummy-repository",
                revision: "main");

            var rawCacheService = new RawCacheService();

            var cachedFile = await rawCacheService.GetOrDownloadAsync(dependency, null, CancellationToken.None);

            Assert.That(Directory.Exists(cachedFile.SnapshotRootPath), Is.True);
            Assert.That(cachedFile.Metadata.Files.Length, Is.GreaterThan(0));
            Assert.That(cachedFile.Metadata.Repository, Is.EqualTo("hf-internal-testing/huggingface-hub-dummy-repository"));
            Assert.That(cachedFile.Metadata.ResolvedCommitHash, Has.Length.EqualTo(40));
            Assert.That(File.ReadAllText(CachePaths.GetRevisionRefPath(cachedFile.Metadata.Repository, "main")), Is.EqualTo(cachedFile.Metadata.ResolvedCommitHash));
        }

        /// <summary>
        /// Uses a public ONNX repository with real model artifacts.
        /// Verifies live resolution and raw-cache download for repo snapshot that includes actual ONNX file under nested `onnx/` folder.
        /// `Explicit` means this runs only when intentionally selected in Unity Test Runner.
        /// </summary>
        [Test]
        [Explicit("Network integration test against live HuggingFace Hub.")]
        public async Task SyncTinyClipOnnxModel_ResolvesAndCachesSnapshot()
        {
            var dependency = CreateDependency(
                packageId: "tools.doji.integration.clip",
                modelId: "onnx-community/tiny-clip-test-ONNX",
                revision: "main");

            var rawCacheService = new RawCacheService();

            var cachedFile = await rawCacheService.GetOrDownloadAsync(dependency, null, CancellationToken.None);

            Assert.That(Directory.Exists(cachedFile.SnapshotRootPath), Is.True);
            Assert.That(cachedFile.TryGetFilePath("onnx/model.onnx", out var onnxPath), Is.True);
            Assert.That(File.Exists(onnxPath), Is.True);
            Assert.That(new FileInfo(onnxPath).Length, Is.GreaterThan(0));
            Assert.That(cachedFile.Metadata.ResolvedCommitHash, Has.Length.EqualTo(40));
            StringAssert.Contains(Path.Combine("snapshots", cachedFile.Metadata.ResolvedCommitHash, "onnx", "model.onnx"), onnxPath);
        }

        private static DiscoveredModelDependency CreateDependency(
            string packageId,
            string modelId,
            string revision)
        {
            return new DiscoveredModelDependency(
                packageId,
                packageId,
                "1.0.0",
                $"/tmp/{packageId}",
                $"/tmp/{packageId}/hf-hub-manifest.json",
                "integration-manifest-hash",
                new ModelDependencyDefinition(
                    modelId,
                    revision));
        }
    }
}
