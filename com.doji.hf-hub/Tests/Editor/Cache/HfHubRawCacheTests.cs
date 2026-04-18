using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers raw-cache snapshot path, metadata, lock, and cache-hit behavior.
    /// </summary>
    internal sealed class HfHubRawCacheTests
    {
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
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

        /// <summary>
        /// Verifies cache paths are repo-snapshot-rooted, with repo files stored underneath and metadata beside the snapshot.
        /// </summary>
        [Test]
        public void GetSnapshotPaths_UseRepoRevisionAndSnapshotShape()
        {
            var repository = "doji/test-model";
            var commitHash = "abc123";
            var repoFile = "models/encoder/model.onnx";

            var snapshotRoot = HfHubCachePaths.GetSnapshotRoot(repository, commitHash);
            var snapshotPath = HfHubCachePaths.GetSnapshotFilePath(repository, commitHash, repoFile);
            var metadataPath = HfHubCachePaths.GetSnapshotMetadataPath(repository, commitHash);
            var lockPath = HfHubCachePaths.GetSnapshotLockPath(repository, commitHash);
            var refPath = HfHubCachePaths.GetRevisionRefPath(repository, "main");

            StringAssert.Contains(Path.Combine("raw", "doji--test-model", "snapshots", "abc123"), snapshotRoot);
            StringAssert.EndsWith(Path.Combine("models", "encoder", "model.onnx"), snapshotPath);
            Assert.That(metadataPath, Is.EqualTo(Path.Combine(snapshotRoot, "snapshot.hfhub.json")));
            Assert.That(lockPath, Is.EqualTo(metadataPath + ".lock"));
            StringAssert.Contains(Path.Combine("raw", "doji--test-model", "refs", "main"), refPath);
        }

        /// <summary>
        /// Verifies raw snapshot metadata reader rejects missing and invalid files.
        /// </summary>
        [Test]
        public void TryReadMetadata_ReturnsFalseForMissingOrInvalidJson()
        {
            var service = new HfHubRawCacheService();
            var missingPath = Path.Combine(_rootDirectory, "missing.hfhub.json");
            var invalidPath = Path.Combine(_rootDirectory, "invalid.hfhub.json");
            File.WriteAllText(invalidPath, "{ invalid json");

            Assert.That(service.TryReadMetadata(missingPath, out var missingMetadata), Is.False);
            Assert.That(missingMetadata, Is.Null);
            Assert.That(service.TryReadMetadata(invalidPath, out var invalidMetadata), Is.False);
            Assert.That(invalidMetadata, Is.Null);
        }

        /// <summary>
        /// Verifies raw snapshot metadata reader deserializes repo file lists.
        /// </summary>
        [Test]
        public void TryReadMetadata_ReadsValidSnapshotSidecar()
        {
            var service = new HfHubRawCacheService();
            var metadataPath = Path.Combine(_rootDirectory, "snapshot.hfhub.json");
            var json = @"{
  ""Repository"": ""doji/test-model"",
  ""RequestedRevision"": ""main"",
  ""ResolvedCommitHash"": ""abc123"",
  ""CachedSnapshotRoot"": ""/tmp/snapshot"",
  ""Files"": [
    {
      ""RepoPath"": ""model.onnx"",
      ""CachedFilePath"": ""/tmp/snapshot/model.onnx"",
      ""SizeInBytes"": 42
    }
  ],
  ""DownloadedAtUtc"": ""2026-04-15T12:00:00.0000000Z""
}";
            File.WriteAllText(metadataPath, json);

            Assert.That(service.TryReadMetadata(metadataPath, out var metadata), Is.True);
            Assert.That(metadata.Repository, Is.EqualTo("doji/test-model"));
            Assert.That(metadata.RequestedRevision, Is.EqualTo("main"));
            Assert.That(metadata.ResolvedCommitHash, Is.EqualTo("abc123"));
            Assert.That(metadata.Files, Has.Length.EqualTo(1));
            Assert.That(metadata.Files[0].RepoPath, Is.EqualTo("model.onnx"));
            Assert.That(metadata.Files[0].SizeInBytes, Is.EqualTo(42));
        }

        /// <summary>
        /// Verifies snapshot lock blocks a second acquisition until first holder is disposed.
        /// </summary>
        [Test]
        public void AcquireLock_BlocksSecondOpenUntilDisposed()
        {
            var lockPath = Path.Combine(_rootDirectory, "cache.lock");
            using var firstLock = FileLock.Acquire(lockPath, TimeSpan.FromSeconds(1));

            Assert.Throws<IOException>(() =>
            {
                using var _ = FileLock.Acquire(lockPath, TimeSpan.Zero);
            });
        }

        /// <summary>
        /// Verifies first snapshot fill downloads every repo file, writes metadata, and writes revision ref.
        /// </summary>
        [Test]
        public async Task GetOrDownloadAsync_DownloadsSnapshotWritesMetadataAndRef()
        {
            var resolvedSnapshot = CreateResolvedSnapshot();
            var httpClient = new FakeHfHubHttpClient(resolvedSnapshot);
            var service = new HfHubRawCacheService(httpClient);
            var dependency = CreateDependency();

            var cachedSnapshot = await service.GetOrDownloadAsync(dependency, null, CancellationToken.None);

            Assert.That(Directory.Exists(cachedSnapshot.SnapshotRootPath), Is.True);
            Assert.That(cachedSnapshot.Metadata.Files, Has.Length.EqualTo(2));
            Assert.That(File.Exists(cachedSnapshot.Metadata.Files[0].CachedFilePath), Is.True);
            Assert.That(File.ReadAllText(HfHubCachePaths.GetRevisionRefPath("doji/test-model", "main")), Is.EqualTo("abc123"));
            Assert.That(httpClient.ResolveCallCount, Is.EqualTo(1));
            Assert.That(httpClient.DownloadCallCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies cache hit skips repo file downloads when snapshot metadata and files already match.
        /// </summary>
        [Test]
        public async Task GetOrDownloadAsync_ReusesSnapshotWhenMetadataAndFilesMatch()
        {
            var resolvedSnapshot = CreateResolvedSnapshot();
            var seedClient = new FakeHfHubHttpClient(resolvedSnapshot);
            var service = new HfHubRawCacheService(seedClient);
            var dependency = CreateDependency();

            var firstSnapshot = await service.GetOrDownloadAsync(dependency, null, CancellationToken.None);
            var reuseClient = new FakeHfHubHttpClient(resolvedSnapshot);
            var reuseService = new HfHubRawCacheService(reuseClient);

            var secondSnapshot = await reuseService.GetOrDownloadAsync(dependency, null, CancellationToken.None);

            Assert.That(secondSnapshot.SnapshotRootPath, Is.EqualTo(firstSnapshot.SnapshotRootPath));
            Assert.That(reuseClient.ResolveCallCount, Is.EqualTo(1));
            Assert.That(reuseClient.DownloadCallCount, Is.EqualTo(0));
        }

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
                    "main"));
        }

        private static HfHubResolvedSnapshotVersion CreateResolvedSnapshot()
        {
            return new HfHubResolvedSnapshotVersion(
                "doji/test-model",
                "main",
                "abc123",
                new[]
                {
                    new HfHubResolvedRepoFile("config.json", "https://cdn.example/config.json", "etag-config", 6),
                    new HfHubResolvedRepoFile("models/model.onnx", "https://cdn.example/model.onnx", "etag-model", 5)
                });
        }

        private sealed class FakeHfHubHttpClient : IHfHubHttpClient
        {
            private readonly HfHubResolvedSnapshotVersion _resolvedSnapshot;

            public FakeHfHubHttpClient(HfHubResolvedSnapshotVersion resolvedSnapshot)
            {
                _resolvedSnapshot = resolvedSnapshot;
            }

            public int ResolveCallCount { get; private set; }
            public int DownloadCallCount { get; private set; }

            public Task<HfHubResolvedSnapshotVersion> ResolveSnapshotVersionAsync(
                HfHubDiscoveredModelDependency dependency,
                CancellationToken cancellationToken)
            {
                ResolveCallCount++;
                return Task.FromResult(_resolvedSnapshot);
            }

            public Task DownloadFileAsync(
                HfHubResolvedRepoFile resolvedFile,
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

                var fileContents = new string('x', (int)(resolvedFile.SizeInBytes ?? 1));
                File.WriteAllText(targetPath, fileContents);
                progress?.Report(new DownloadProgress(1, 1));
                return Task.CompletedTask;
            }
        }
    }
}
