using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers reading the whole shared cache for the Model Hub cache overview, and removing from it.
    /// </summary>
    internal sealed class CacheOverviewServiceTests
    {
        private const string Commit = "0123456789abcdef0123456789abcdef01234567";

        private TestCacheScope _cacheScope;
        private string _rootDirectory;
        private string _cacheRoot;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.CacheOverviewTests", Guid.NewGuid().ToString("N"));
            _cacheRoot = Path.Combine(_rootDirectory, "SharedCache");
            Directory.CreateDirectory(_cacheRoot);
            _cacheScope = new TestCacheScope(_cacheRoot);
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
        /// Verifies repository ids, types, refs, sizes, and partial downloads come from the folders on disk, and that
        /// folders that are not repositories are skipped.
        /// </summary>
        [Test]
        public void Scan_ReadsRepositoriesFilesAndPartialDownloads()
        {
            var model = CreateRepository("models--doji--test-model");
            WriteFile(Path.Combine(model, "snapshots", Commit, "onnx", "model.onnx"), 100);
            WriteFile(Path.Combine(model, "blobs", "abc.incomplete"), 40);
            WriteFile(Path.Combine(model, "blobs", "def"), 10);
            var dataset = CreateRepository("datasets--doji--test-data");
            WriteFile(Path.Combine(dataset, "snapshots", Commit, "data.csv"), 5);
            Directory.CreateDirectory(Path.Combine(_cacheRoot, ".locks", "models--doji--test-model"));
            File.WriteAllText(Path.Combine(_cacheRoot, "version.txt"), "1");

            var overview = CacheOverviewService.Scan(_cacheRoot);

            Assert.That(overview.Repositories.Select(repository => repository.Repository), Is.EqualTo(new[] { "doji/test-model", "doji/test-data" }));
            var modelEntry = overview.Repositories[0];
            Assert.That(modelEntry.RepoType, Is.EqualTo("model"));
            Assert.That(modelEntry.Refs, Is.EqualTo(new[] { "main" }));
            Assert.That(modelEntry.SizeInBytes, Is.EqualTo(110));
            Assert.That(modelEntry.PartialSizeInBytes, Is.EqualTo(40));
            Assert.That(modelEntry.Files.Single(file => !file.IsPartial && file.SizeInBytes == 100).Name, Is.EqualTo("onnx/model.onnx"));
            Assert.That(overview.Repositories[1].RepoType, Is.EqualTo("dataset"));
            Assert.That(overview.TotalSizeInBytes, Is.EqualTo(155));
        }

        /// <summary>
        /// Verifies a blob that a snapshot links to is counted once and named by the linking repository path.
        /// </summary>
        [Test]
        public void Scan_NamesLinkedBlobsByRepositoryPath()
        {
            Assume.That(CacheFiles.AreSymlinksSupported(_rootDirectory), Is.True, "This machine cannot create symbolic links.");
            var model = CreateRepository("models--doji--test-model");
            var blob = Path.Combine(model, "blobs", "etag-model");
            WriteFile(blob, 64);
            CacheFiles.LinkSnapshotFile(blob, Path.Combine(model, "snapshots", Commit, "onnx", "model.onnx"), true);

            var repository = CacheOverviewService.Scan(_cacheRoot).Repositories.Single();

            Assert.That(repository.Files.Single().Name, Is.EqualTo("onnx/model.onnx"));
            Assert.That(repository.SizeInBytes, Is.EqualTo(64));
        }

        /// <summary>
        /// Verifies Remove from Cache deletes the repository, its lock folder, and HF Hub's index for it.
        /// </summary>
        [Test]
        public void RemoveRepository_DeletesRepositoryLocksAndIndex()
        {
            var model = CreateRepository("models--doji--test-model");
            WriteFile(Path.Combine(model, "blobs", "etag-model"), 10);
            var indexFolder = Path.Combine(_cacheScope.DataRoot, "index", "models--doji--test-model");
            Directory.CreateDirectory(indexFolder);
            File.WriteAllText(Path.Combine(indexFolder, Commit + ".json"), "{}");

            CacheOverviewService.RemoveRepository(CacheOverviewService.Scan(_cacheRoot).Repositories.Single(), _cacheRoot);

            Assert.That(Directory.Exists(model), Is.False);
            Assert.That(Directory.Exists(Path.Combine(_cacheRoot, ".locks", "models--doji--test-model")), Is.False);
            Assert.That(Directory.Exists(indexFolder), Is.False);
        }

        /// <summary>
        /// Verifies nothing is deleted while another program holds a blob lock of the repository.
        /// </summary>
        [Test]
        public void RemoveRepository_KeepsRepository_WhenBlobIsLocked()
        {
            var model = CreateRepository("models--doji--test-model");
            WriteFile(Path.Combine(model, "blobs", "etag-model"), 10);
            var repository = CacheOverviewService.Scan(_cacheRoot).Repositories.Single();
            Assert.That(BlobLock.TryAcquire(Path.Combine(_cacheRoot, ".locks", "models--doji--test-model", "etag-model.lock"), out var heldLock), Is.True);

            using (heldLock)
            {
                Assert.Throws<IOException>(() => CacheOverviewService.RemoveRepository(repository, _cacheRoot));
            }

            Assert.That(File.Exists(Path.Combine(model, "blobs", "etag-model")), Is.True);
        }

        /// <summary>
        /// Verifies deleting a partial download removes the file and its resume state and keeps complete files.
        /// </summary>
        [Test]
        public void DeletePartialFile_RemovesPartialAndResumeState()
        {
            var model = CreateRepository("models--doji--test-model");
            WriteFile(Path.Combine(model, "blobs", "etag-model"), 10);
            WriteFile(Path.Combine(model, "blobs", "etag-part.downloading"), 20);
            File.WriteAllText(Path.Combine(model, "blobs", "etag-part.downloading.json"), "{}");
            var repository = CacheOverviewService.Scan(_cacheRoot).Repositories.Single();

            CacheOverviewService.DeletePartialFile(repository, repository.Files.Single(file => file.IsPartial), _cacheRoot);

            Assert.That(Directory.GetFiles(Path.Combine(model, "blobs")).Select(Path.GetFileName), Is.EqualTo(new[] { "etag-model" }));
        }

        private string CreateRepository(string folderName)
        {
            var folder = Path.Combine(_cacheRoot, folderName);
            Directory.CreateDirectory(Path.Combine(folder, "refs"));
            File.WriteAllText(Path.Combine(folder, "refs", "main"), Commit);
            return folder;
        }

        private static void WriteFile(string path, int length)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, new byte[length]);
        }
    }
}
