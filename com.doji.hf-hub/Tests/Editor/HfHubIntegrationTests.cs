using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Real network integration tests against public HuggingFace repositories.
    /// These tests use real `HfHubHttpClient` and `HfHubRawCacheService`, isolate cache root into temp folder,
    /// and verify end-to-end metadata resolution plus raw cache download for known public entries.
    ///
    /// These tests are marked with `Explicit` on purpose:
    /// - they depend on live public network access
    /// - they depend on external repository contents staying available
    /// - they are slower and less deterministic than normal editor unit tests
    ///
    /// Normal "run all tests" should stay fast and stable, so these only run when selected intentionally.
    /// </summary>
    internal sealed class HfHubIntegrationTests
    {
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "Doji.ModelHub.IntegrationTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
            Environment.SetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT", Path.Combine(_rootDirectory, "SharedCache"));
        }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT", null);
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        /// <summary>
        /// Uses a small public dummy model repo with only `README.md`.
        /// Verifies live HEAD resolution, commit hash capture, and real raw-cache download for a tiny text file.
        /// `Explicit` means this runs only when intentionally selected in Unity Test Runner.
        /// </summary>
        [Test]
        [Explicit("Network integration test against live HuggingFace Hub.")]
        public async Task SyncDummyRepositoryReadme_ResolvesAndCachesFile()
        {
            var dependency = CreateDependency(
                packageId: "com.doji.integration.dummy",
                modelId: "dummy-readme",
                repository: "hf-internal-testing/huggingface-hub-dummy-repository",
                revision: "main",
                sourceFile: "README.md");

            var rawCacheService = new HfHubRawCacheService();

            var cachedFile = await rawCacheService.GetOrDownloadAsync(dependency, CancellationToken.None);

            Assert.That(File.Exists(cachedFile.FilePath), Is.True);
            Assert.That(new FileInfo(cachedFile.FilePath).Length, Is.GreaterThan(0));
            Assert.That(cachedFile.Metadata.Repository, Is.EqualTo("hf-internal-testing/huggingface-hub-dummy-repository"));
            Assert.That(cachedFile.Metadata.ResolvedCommitHash, Has.Length.EqualTo(40));
            Assert.That(File.ReadAllText(HfHubCachePaths.GetRevisionRefPath(cachedFile.Metadata.Repository, "main")), Is.EqualTo(cachedFile.Metadata.ResolvedCommitHash));
        }

        /// <summary>
        /// Uses a public ONNX repository with real model artifacts.
        /// Verifies live resolution and raw-cache download for actual ONNX file under nested `onnx/` folder.
        /// `Explicit` means this runs only when intentionally selected in Unity Test Runner.
        /// </summary>
        [Test]
        [Explicit("Network integration test against live HuggingFace Hub.")]
        public async Task SyncTinyClipOnnxModel_ResolvesAndCachesOnnxFile()
        {
            var dependency = CreateDependency(
                packageId: "com.doji.integration.clip",
                modelId: "tiny-clip-onnx",
                repository: "onnx-community/tiny-clip-test-ONNX",
                revision: "main",
                sourceFile: "onnx/model.onnx");

            var rawCacheService = new HfHubRawCacheService();

            var cachedFile = await rawCacheService.GetOrDownloadAsync(dependency, CancellationToken.None);

            Assert.That(File.Exists(cachedFile.FilePath), Is.True);
            Assert.That(new FileInfo(cachedFile.FilePath).Length, Is.GreaterThan(0));
            Assert.That(cachedFile.Metadata.SourceFile, Is.EqualTo("onnx/model.onnx"));
            Assert.That(cachedFile.Metadata.ResolvedCommitHash, Has.Length.EqualTo(40));
            StringAssert.Contains(Path.Combine("snapshots", cachedFile.Metadata.ResolvedCommitHash, "onnx", "model.onnx"), cachedFile.FilePath);
        }

        private static HfHubDiscoveredModelDependency CreateDependency(
            string packageId,
            string modelId,
            string repository,
            string revision,
            string sourceFile)
        {
            return new HfHubDiscoveredModelDependency(
                packageId,
                packageId,
                "1.0.0",
                $"/tmp/{packageId}",
                $"/tmp/{packageId}/hf-hub-manifest.json",
                "integration-manifest-hash",
                new HfHubModelDependencyDefinition(
                    modelId,
                    modelId,
                    repository,
                    revision,
                    sourceFile));
        }
    }
}
