using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers the free-space check before a sync: what a download and the following conversion need on each drive,
    /// the message when it does not fit, and that a sync stops before it downloads anything.
    /// </summary>
    internal sealed class DiskSpaceCheckTests
    {
        private string _root;
        private TestCacheScope _cacheScope;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.DiskSpaceCheckTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _cacheScope = new TestCacheScope(Path.Combine(_root, "cache", "hub"));
        }

        [TearDown]
        public void TearDown()
        {
            DiskSpaceCheck.DiskSpace = null;
            _cacheScope.Dispose();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        /// <summary>
        /// Requirements on one drive add up; a drive with enough space is not reported.
        /// </summary>
        [Test]
        public void FindShortfalls_AddsUpRequirementsPerDrive()
        {
            var disk = new FakeDiskSpace()
                .Add(Path.Combine(_root, "c"), "C:\\", 100)
                .Add(Path.Combine(_root, "g"), "G:\\", 1000);

            var shortfalls = DiskSpaceCheck.FindShortfalls(
                new[]
                {
                    new DiskSpaceRequirement(Path.Combine(_root, "c", "hub"), 60, "downloads"),
                    new DiskSpaceRequirement(Path.Combine(_root, "c", "data"), 50, "converted models"),
                    new DiskSpaceRequirement(Path.Combine(_root, "g", "other"), 900, "downloads")
                },
                disk);

            Assert.That(shortfalls, Has.Length.EqualTo(1));
            Assert.That(shortfalls[0].DriveRoot, Is.EqualTo("C:\\"));
            Assert.That(shortfalls[0].NeededBytes, Is.EqualTo(110));
            Assert.That(shortfalls[0].AvailableBytes, Is.EqualTo(100));
            Assert.That(shortfalls[0].Purposes, Is.EqualTo(new[] { "downloads", "converted models" }));
        }

        /// <summary>
        /// The message names each full drive with the needed and the free size.
        /// </summary>
        [Test]
        public void EnsureFits_ThrowsWithNeededAndFreeSizePerDrive()
        {
            var disk = new FakeDiskSpace()
                .Add(Path.Combine(_root, "c"), "C:\\", 1024)
                .Add(Path.Combine(_root, "g"), "G:\\", 10);

            var exception = Assert.Throws<InsufficientDiskSpaceException>(() => DiskSpaceCheck.EnsureFits(
                "download owner/model",
                new[]
                {
                    new DiskSpaceRequirement(Path.Combine(_root, "c"), 2048, "downloads"),
                    new DiskSpaceRequirement(Path.Combine(_root, "g"), 20, "converted models")
                },
                disk));

            Assert.That(
                exception.Message,
                Is.EqualTo("Not enough disk space to download owner/model. C:\\ needs 2 KB for downloads, 1 KB free. G:\\ needs 20 B for converted models, 10 B free. Free up space or choose other folders in Preferences > AI Model Hub."));
        }

        /// <summary>
        /// A download needs the missing files in the cache and, when a conversion follows, about the size of the
        /// downloaded ONNX files and their external weights in the converted store.
        /// </summary>
        [Test]
        public void BuildDownloadRequirements_CountsDownloadsAndConversions()
        {
            var files = new[]
            {
                new ResolvedRepoFile("config.json", "u", "e1", 10),
                new ResolvedRepoFile("onnx/model.onnx", "u", "e2", 100),
                new ResolvedRepoFile("onnx/model.onnx_data", "u", "e3", 1000),
                new ResolvedRepoFile("onnx/other.onnx", "u", "e4", 7)
            };
            var missing = files.Take(3).ToArray();

            var withConversion = RawCacheService.BuildDownloadRequirements(files, missing, conversionFollows: true);
            var downloadOnly = RawCacheService.BuildDownloadRequirements(files, missing, conversionFollows: false);

            Assert.That(withConversion[0].Folder, Is.EqualTo(CachePaths.GetCacheRoot()));
            Assert.That(withConversion[0].Bytes, Is.EqualTo(1110));
            Assert.That(withConversion[1].Folder, Is.EqualTo(CachePaths.GetConvertedRoot()));
            Assert.That(withConversion[1].Bytes, Is.EqualTo(1100));
            Assert.That(downloadOnly, Has.Length.EqualTo(1));
        }

        /// <summary>
        /// Converting cached ONNX files needs about their size, with external weights, in the converted store.
        /// </summary>
        [Test]
        public void BuildConversionRequirement_CountsPendingModelsWithWeights()
        {
            var cached = new[]
            {
                new RawCacheFileMetadata { RepoPath = "model.onnx", SizeInBytes = 100 },
                new RawCacheFileMetadata { RepoPath = "model.onnx_data", SizeInBytes = 1000 },
                new RawCacheFileMetadata { RepoPath = "done.onnx", SizeInBytes = 5 }
            };

            var requirement = ProjectImportService.BuildConversionRequirement(new[] { "model.onnx" }, cached);

            Assert.That(requirement.Folder, Is.EqualTo(CachePaths.GetConvertedRoot()));
            Assert.That(requirement.Bytes, Is.EqualTo(1100));
        }

        /// <summary>
        /// A sync whose download and conversion do not fit on the drive stops before it downloads a file.
        /// </summary>
        [Test]
        public async Task GetOrDownloadAsync_StopsBeforeDownloadingWhenTheDriveIsFull()
        {
            DiskSpaceCheck.DiskSpace = new FakeDiskSpace().Add(_root, "T:\\", 12);
            var client = new CountingHttpClient();
            var service = new RawCacheService(client);

            InsufficientDiskSpaceException exception = null;
            try
            {
                await service.GetOrDownloadAsync(CreateDependency(), null, CancellationToken.None, conversionFollows: true);
            }
            catch (InsufficientDiskSpaceException caught)
            {
                exception = caught;
            }

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.Message, Does.StartWith("Not enough disk space to download doji/test-model. T:\\ needs 16 B for downloads and converted models, 12 B free."));
            Assert.That(client.DownloadCallCount, Is.EqualTo(0));
        }

        /// <summary>
        /// The same download fits when only the download is checked.
        /// </summary>
        [Test]
        public async Task GetOrDownloadAsync_DownloadsWhenItFits()
        {
            DiskSpaceCheck.DiskSpace = new FakeDiskSpace().Add(_root, "T:\\", 12);
            var client = new CountingHttpClient();
            var service = new RawCacheService(client);

            await service.GetOrDownloadAsync(CreateDependency(), null, CancellationToken.None, conversionFollows: false);

            Assert.That(client.DownloadCallCount, Is.EqualTo(2));
        }

        private static DiscoveredModelDependency CreateDependency()
        {
            return new DiscoveredModelDependency(
                "tools.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/tools.doji.test",
                "/tmp/tools.doji.test/model-hub-manifest.json",
                new ModelDependencyDefinition("doji/test-model", "main"));
        }

        private sealed class CountingHttpClient : IHttpClient
        {
            private static readonly ResolvedSnapshotVersion Snapshot = new(
                "doji/test-model",
                "main",
                "abc123",
                new[]
                {
                    new RepositoryFileMetadata { RepoPath = "config.json", SizeInBytes = 6 },
                    new RepositoryFileMetadata { RepoPath = "models/model.onnx", SizeInBytes = 5 }
                },
                new[]
                {
                    new ResolvedRepoFile("config.json", "https://cdn.example/config.json", "etag-config", 6),
                    new ResolvedRepoFile("models/model.onnx", "https://cdn.example/model.onnx", "etag-model", 5)
                });

            public int DownloadCallCount { get; private set; }

            public Task<ResolvedSnapshotVersion> ResolveSnapshotVersionAsync(DiscoveredModelDependency dependency, CancellationToken cancellationToken)
            {
                return Task.FromResult(Snapshot);
            }

            public Task<ResolvedRepositoryInventory> GetRepositoryInventoryAsync(string repository, string revision, CancellationToken cancellationToken)
            {
                return Task.FromResult(new ResolvedRepositoryInventory(repository, revision, Snapshot.ResolvedCommitHash, Snapshot.RepositoryFiles.ToArray()));
            }

            public Task DownloadFileAsync(ResolvedRepoFile resolvedFile, string targetPath, IProgress<Doji.Downloads.DownloadProgress> progress, CancellationToken cancellationToken)
            {
                DownloadCallCount++;
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                File.WriteAllText(targetPath, new string('x', (int)(resolvedFile.SizeInBytes ?? 1)));
                return Task.CompletedTask;
            }
        }
    }
}
