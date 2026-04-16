using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers raw-cache path, metadata, lock, and cache-hit behavior.
    /// Tests isolate cache root with environment override and use a fake HTTP client so no live Hub traffic is involved.
    /// </summary>
    internal sealed class HfHubRawCacheTests
    {
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
            // Each test gets isolated cache root so path and file assertions cannot leak across runs.
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "Doji.ModelHub.RawCacheTests",
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

        [Test]
        public void GetSnapshotPaths_UseRepoRevisionAndSourceFileShape()
        {
            // Pure path-shape test. No IO beyond local temp paths.
            var repository = "doji/test-model";
            var commitHash = "abc123";
            var sourceFile = "models/encoder/model.onnx";

            var snapshotPath = HfHubCachePaths.GetSnapshotFilePath(repository, commitHash, sourceFile);
            var metadataPath = HfHubCachePaths.GetSnapshotMetadataPath(repository, commitHash, sourceFile);
            var lockPath = HfHubCachePaths.GetSnapshotLockPath(repository, commitHash, sourceFile);
            var refPath = HfHubCachePaths.GetRevisionRefPath(repository, "main");

            StringAssert.Contains(Path.Combine("raw", "doji--test-model", "snapshots", "abc123"), snapshotPath);
            StringAssert.EndsWith(Path.Combine("models", "encoder", "model.onnx"), snapshotPath);
            Assert.That(metadataPath, Is.EqualTo(snapshotPath + ".hfhub.json"));
            Assert.That(lockPath, Is.EqualTo(snapshotPath + ".lock"));
            StringAssert.Contains(Path.Combine("raw", "doji--test-model", "refs", "main"), refPath);
        }

        [Test]
        public void TryReadMetadata_ReturnsFalseForMissingOrInvalidJson()
        {
            // Service is real. Only metadata file contents are mocked by writing missing/invalid files directly.
            var service = new HfHubRawCacheService();
            var missingPath = Path.Combine(_rootDirectory, "missing.hfhub.json");
            var invalidPath = Path.Combine(_rootDirectory, "invalid.hfhub.json");
            File.WriteAllText(invalidPath, "{ invalid json");

            Assert.That(service.TryReadMetadata(missingPath, out var missingMetadata), Is.False);
            Assert.That(missingMetadata, Is.Null);
            Assert.That(service.TryReadMetadata(invalidPath, out var invalidMetadata), Is.False);
            Assert.That(invalidMetadata, Is.Null);
        }

        [Test]
        public void TryReadMetadata_ReadsValidSidecar()
        {
            // Service is real. Sidecar JSON is written directly to verify deserialization contract.
            var service = new HfHubRawCacheService();
            var metadataPath = Path.Combine(_rootDirectory, "file.onnx.hfhub.json");
            var json = @"{
  ""Repository"": ""doji/test-model"",
  ""RequestedRevision"": ""main"",
  ""ResolvedCommitHash"": ""abc123"",
  ""SourceFile"": ""model.onnx"",
  ""EntityTag"": ""etag-1"",
  ""SizeInBytes"": 42,
  ""CachedFilePath"": ""/tmp/model.onnx"",
  ""DownloadedAtUtc"": ""2026-04-15T12:00:00.0000000Z""
}";
            File.WriteAllText(metadataPath, json);

            Assert.That(service.TryReadMetadata(metadataPath, out var metadata), Is.True);
            Assert.That(metadata.Repository, Is.EqualTo("doji/test-model"));
            Assert.That(metadata.RequestedRevision, Is.EqualTo("main"));
            Assert.That(metadata.ResolvedCommitHash, Is.EqualTo("abc123"));
            Assert.That(metadata.SourceFile, Is.EqualTo("model.onnx"));
            Assert.That(metadata.SizeInBytes, Is.EqualTo(42));
        }

        [Test]
        public void AcquireLock_BlocksSecondOpenUntilDisposed()
        {
            // Lock behavior test uses real file lock path and asserts second acquisition fails before first is disposed.
            var lockPath = Path.Combine(_rootDirectory, "cache.lock");
            using var firstLock = FileLock.Acquire(lockPath, TimeSpan.FromSeconds(1));

            Assert.Throws<IOException>(() =>
            {
                using var _ = FileLock.Acquire(lockPath, TimeSpan.Zero);
            });
        }

        [Test]
        public async Task GetOrDownloadAsync_DownloadsFileWritesMetadataAndRef()
        {
            // Fake client controls resolved commit and downloaded file bytes.
            // Test verifies service writes snapshot file, metadata sidecar, and revision ref on first cache fill.
            var resolvedFile = new HfHubResolvedFileVersion(
                "doji/test-model",
                "main",
                "abc123",
                "models/model.onnx",
                "https://huggingface.co/doji/test-model/resolve/main/models/model.onnx",
                "etag-1",
                5);
            var httpClient = new FakeHfHubHttpClient(resolvedFile, "hello");
            var service = new HfHubRawCacheService(httpClient);
            var dependency = CreateDependency();

            var cachedFile = await service.GetOrDownloadAsync(dependency, null, CancellationToken.None);

            Assert.That(File.Exists(cachedFile.FilePath), Is.True);
            Assert.That(File.ReadAllText(cachedFile.FilePath), Is.EqualTo("hello"));
            Assert.That(File.Exists(cachedFile.MetadataPath), Is.True);
            Assert.That(File.ReadAllText(HfHubCachePaths.GetRevisionRefPath("doji/test-model", "main")), Is.EqualTo("abc123"));
            Assert.That(cachedFile.Metadata.EntityTag, Is.EqualTo("etag-1"));
            Assert.That(httpClient.ResolveCallCount, Is.EqualTo(1));
            Assert.That(httpClient.DownloadCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task GetOrDownloadAsync_ReusesCacheWhenMetadataAndFileMatch()
        {
            // First fake client seeds cache. Second fake client would write different contents if download happened.
            // Test verifies second call resolves metadata but skips download when cache file and sidecar still match.
            var resolvedFile = new HfHubResolvedFileVersion(
                "doji/test-model",
                "main",
                "abc123",
                "models/model.onnx",
                "https://huggingface.co/doji/test-model/resolve/main/models/model.onnx",
                "etag-1",
                5);
            var seedClient = new FakeHfHubHttpClient(resolvedFile, "hello");
            var service = new HfHubRawCacheService(seedClient);
            var dependency = CreateDependency();

            var firstCachedFile = await service.GetOrDownloadAsync(dependency, null, CancellationToken.None);
            var reuseClient = new FakeHfHubHttpClient(resolvedFile, "other");
            var reuseService = new HfHubRawCacheService(reuseClient);

            var secondCachedFile = await reuseService.GetOrDownloadAsync(dependency, null, CancellationToken.None);

            Assert.That(secondCachedFile.FilePath, Is.EqualTo(firstCachedFile.FilePath));
            Assert.That(File.ReadAllText(secondCachedFile.FilePath), Is.EqualTo("hello"));
            Assert.That(reuseClient.ResolveCallCount, Is.EqualTo(1));
            Assert.That(reuseClient.DownloadCallCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Creates one discovered dependency without running package discovery.
        /// This keeps tests focused on cache behavior rather than manifest scanning.
        /// </summary>
        private static HfHubDiscoveredModelDependency CreateDependency()
        {
            return new HfHubDiscoveredModelDependency(
                "com.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/com.doji.test",
                "/tmp/com.doji.test/hf-hub-manifest.json",
                "manifesthash",
                new HfHubModelDependencyDefinition(
                    "test-model",
                    "Test Model",
                    "doji/test-model",
                    "main",
                    "models/model.onnx"));
        }

        /// <summary>
        /// Minimal fake HTTP client for cache tests.
        /// It returns one preconfigured resolved file version and writes deterministic file contents on download.
        /// Call counters let tests distinguish cache hit from actual download.
        /// </summary>
        private sealed class FakeHfHubHttpClient : IHfHubHttpClient
        {
            private readonly string _downloadContents;
            private readonly HfHubResolvedFileVersion _resolvedFile;

            public FakeHfHubHttpClient(HfHubResolvedFileVersion resolvedFile, string downloadContents)
            {
                _resolvedFile = resolvedFile;
                _downloadContents = downloadContents;
            }

            public int ResolveCallCount { get; private set; }
            public int DownloadCallCount { get; private set; }

            public Task<HfHubResolvedFileVersion> ResolveFileVersionAsync(
                HfHubDiscoveredModelDependency dependency,
                CancellationToken cancellationToken)
            {
                ResolveCallCount++;
                return Task.FromResult(_resolvedFile);
            }

            public Task DownloadFileAsync(
                HfHubResolvedFileVersion resolvedFile,
                string targetPath,
                IProgress<DownloadProgress> progress,
                CancellationToken cancellationToken)
            {
                DownloadCallCount++;
                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(targetPath, _downloadContents);
                progress?.Report(new DownloadProgress(_downloadContents.Length, _downloadContents.Length));
                return Task.CompletedTask;
            }
        }
    }
}
