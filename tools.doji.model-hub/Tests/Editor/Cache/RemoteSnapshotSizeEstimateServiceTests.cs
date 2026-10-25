using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers the pre-download size and file-list lookup that Model Hub shows before a model is downloaded.
    /// </summary>
    internal sealed class RemoteSnapshotSizeEstimateServiceTests
    {
        private string _repository;

        [SetUp]
        public void SetUp()
        {
            _repository = $"modelhub-tests/estimate-{Guid.NewGuid():N}";
        }

        [TearDown]
        public void TearDown()
        {
            var folder = Path.GetDirectoryName(Path.GetDirectoryName(GetMetadataPath("main")));
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        /// <summary>
        /// A manifest without a revision still gets a file list and sizes, stored under "main".
        /// </summary>
        [Test]
        public void EnsureEstimateLookup_ResolvesManifestWithoutRevisionAsMain()
        {
            var service = new RemoteSnapshotSizeEstimateService(new FakeInventoryClient());

            service.EnsureEstimateLookup(CreateDependency(revision: string.Empty));

            Assert.That(service.TryGetRepositoryInventory(_repository, string.Empty, out var files), Is.True);
            Assert.That(files.Length, Is.EqualTo(2));
            Assert.That(service.TryGetEstimatedCacheSize(_repository, "main", out var size), Is.True);
            Assert.That(size, Is.EqualTo(30));
        }

        /// <summary>
        /// An estimate saved in the older format, with a commit but no file list, is fetched again.
        /// </summary>
        [Test]
        public void EnsureEstimateLookup_RefreshesEstimateWithoutFileList()
        {
            var metadataPath = GetMetadataPath("main");
            Directory.CreateDirectory(Path.GetDirectoryName(metadataPath));
            File.WriteAllText(metadataPath, JsonConvert.SerializeObject(new
            {
                Repository = _repository,
                RequestedRevision = "main",
                EstimatedCacheSizeBytes = 99,
                ResolvedCommitHash = "old-commit",
                AttemptedAtUtc = DateTime.UtcNow.ToString("O")
            }));
            var service = new RemoteSnapshotSizeEstimateService(new FakeInventoryClient());

            service.EnsureEstimateLookup(CreateDependency("main"));

            Assert.That(service.TryGetRepositoryInventory(_repository, "main", out var files), Is.True);
            Assert.That(files.Length, Is.EqualTo(2));
        }

        private string GetMetadataPath(string revision)
        {
            return ProjectPaths.ToProjectAbsolutePath(ProjectPaths.GetCacheSizeEstimateMetadataPath(_repository, revision));
        }

        private DiscoveredModelDependency CreateDependency(string revision)
        {
            return new DiscoveredModelDependency(
                "tools.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/tools.doji.test",
                "/tmp/tools.doji.test/model-hub-manifest.json",
                "manifesthash",
                new ModelDependencyDefinition(_repository, revision));
        }

        private sealed class FakeInventoryClient : IHttpClient
        {
            public Task<ResolvedRepositoryInventory> GetRepositoryInventoryAsync(
                string repository,
                string revision,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new ResolvedRepositoryInventory(
                    repository,
                    revision,
                    "commit",
                    new[]
                    {
                        new RepositoryFileMetadata { RepoPath = "onnx/small.onnx", SizeInBytes = 10 },
                        new RepositoryFileMetadata { RepoPath = "onnx/large.onnx", SizeInBytes = 20 }
                    }));
            }

            public Task<ResolvedSnapshotVersion> ResolveSnapshotVersionAsync(
                DiscoveredModelDependency dependency,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task DownloadFileAsync(
                ResolvedRepoFile resolvedFile,
                string targetPath,
                IProgress<DownloadProgress> progress,
                CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }
    }
}
