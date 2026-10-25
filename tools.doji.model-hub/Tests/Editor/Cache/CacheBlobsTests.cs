using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers the blob store that Editor sync and player downloads share. Calls run on the thread pool, because the
    /// Editor main thread is blocked while a test runs.
    /// </summary>
    internal sealed class CacheBlobsTests
    {
        private const string Repository = "doji/blob-test";
        private const string Commit = "1111111111111111111111111111111111111111";
        private const string OtherCommit = "2222222222222222222222222222222222222222";
        private const string Etag = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        private TestCacheScope _cacheScope;
        private List<string> _downloads;

        [SetUp]
        public void SetUp()
        {
            _cacheScope = new TestCacheScope(Path.Combine(Path.GetTempPath(), "modelhub-blob-tests", Guid.NewGuid().ToString("N")));
            _downloads = new List<string>();
        }

        [TearDown]
        public void TearDown()
        {
            _cacheScope.Dispose();
            if (Directory.Exists(_cacheScope.CacheRoot))
            {
                Directory.Delete(_cacheScope.CacheRoot, true);
            }
        }

        /// <summary>
        /// A file with an etag lands in blobs/&lt;etag&gt;, and the snapshot path reads the same content.
        /// </summary>
        [Test]
        public void StoreAsync_WritesTheBlobAndLinksTheSnapshot()
        {
            var snapshotPath = Store(Commit, "onnx/model.onnx", Etag, 5, "model");

            Assert.That(snapshotPath, Is.EqualTo(CachePaths.GetSnapshotFilePath(Repository, Commit, "onnx/model.onnx")));
            Assert.That(_downloads, Is.EqualTo(new[] { CachePaths.GetBlobPath(Repository, Etag) }));
            Assert.That(File.ReadAllText(CachePaths.GetBlobPath(Repository, Etag)), Is.EqualTo("model"));
            Assert.That(File.ReadAllText(snapshotPath), Is.EqualTo("model"));
        }

        /// <summary>
        /// A second revision with the same file reuses the blob without downloading it again.
        /// </summary>
        [Test]
        public void StoreAsync_ReusesACompleteBlobForAnotherRevision()
        {
            Store(Commit, "onnx/model.onnx", Etag, 5, "model");
            _downloads.Clear();

            var snapshotPath = Store(OtherCommit, "onnx/model.onnx", Etag, 5, "never");

            Assert.That(_downloads, Is.Empty);
            Assert.That(File.ReadAllText(snapshotPath), Is.EqualTo("model"));
        }

        /// <summary>
        /// A blob with the wrong size, for example left by a crash, is downloaded again.
        /// </summary>
        [Test]
        public void StoreAsync_DownloadsAgainWhenTheBlobHasTheWrongSize()
        {
            var blobPath = CachePaths.GetBlobPath(Repository, Etag);
            Directory.CreateDirectory(Path.GetDirectoryName(blobPath));
            File.WriteAllText(blobPath, "mo");

            var snapshotPath = Store(Commit, "onnx/model.onnx", Etag, 5, "model");

            Assert.That(_downloads, Has.Count.EqualTo(1));
            Assert.That(File.ReadAllText(snapshotPath), Is.EqualTo("model"));
        }

        /// <summary>
        /// Without a known size, any existing blob counts as complete, as for files Python downloaded.
        /// </summary>
        [Test]
        public void StoreAsync_AcceptsAnExistingBlobWhenTheSizeIsUnknown()
        {
            var blobPath = CachePaths.GetBlobPath(Repository, Etag);
            Directory.CreateDirectory(Path.GetDirectoryName(blobPath));
            File.WriteAllText(blobPath, "python");

            var snapshotPath = Store(Commit, "config.json", Etag, null, "never");

            Assert.That(_downloads, Is.Empty);
            Assert.That(File.ReadAllText(snapshotPath), Is.EqualTo("python"));
        }

        /// <summary>
        /// A file without an etag is written straight to the snapshot, and no blob is created.
        /// </summary>
        [Test]
        public void StoreAsync_WritesFilesWithoutAnEtagIntoTheSnapshot()
        {
            var snapshotPath = Store(Commit, "nested/config.json", null, null, "{}");

            Assert.That(_downloads, Is.EqualTo(new[] { snapshotPath }));
            Assert.That(File.ReadAllText(snapshotPath), Is.EqualTo("{}"));
            Assert.That(Directory.Exists(CachePaths.GetBlobsRoot(Repository)), Is.False);
        }

        /// <summary>
        /// While another program holds the blob lock, the store reports that it waits and downloads nothing; a
        /// cancelled wait leaves no file behind.
        /// </summary>
        [Test]
        public void StoreAsync_WaitsForTheBlobLockAndCanBeCancelled()
        {
            var lockPath = CachePaths.GetBlobLockPath(Repository, Etag);
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath));
            Assert.That(BlobLock.TryAcquire(lockPath, out var holder), Is.True);
            using (holder)
            using (var cancellation = new CancellationTokenSource())
            {
                var waiting = 0;
                var store = Task.Run(() => CacheBlobs.StoreAsync(
                    Repository, Commit, "onnx/model.onnx", Etag, 5,
                    path => Write(path, "model"),
                    () => Interlocked.Increment(ref waiting),
                    cancellation.Token));

                Assert.That(store.Wait(300), Is.False);
                cancellation.Cancel();

                Assert.That(() => store.Wait(5000), Throws.InstanceOf<AggregateException>()
                    .With.InnerException.InstanceOf<OperationCanceledException>());
                Assert.That(waiting, Is.EqualTo(1));
                Assert.That(_downloads, Is.Empty);
                Assert.That(File.Exists(CachePaths.GetBlobPath(Repository, Etag)), Is.False);
            }
        }

        private string Store(string commit, string repoPath, string etag, long? size, string content)
        {
            return Task.Run(() => CacheBlobs.StoreAsync(
                    Repository, commit, repoPath, etag, size,
                    path => Write(path, content),
                    null,
                    CancellationToken.None))
                .GetAwaiter()
                .GetResult();
        }

        private Task Write(string path, string content)
        {
            lock (_downloads)
            {
                _downloads.Add(path);
            }

            File.WriteAllText(path, content);
            return Task.CompletedTask;
        }
    }
}
