using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers the blob lock shared with huggingface_hub. Waits run on the thread pool, because the Editor main
    /// thread is blocked while a test runs.
    /// </summary>
    internal sealed class BlobLockTests
    {
        private string _lockPath;

        [SetUp]
        public void SetUp()
        {
            _lockPath = Path.Combine(Path.GetTempPath(), "hfhub-bloblock-tests", Guid.NewGuid().ToString("N"), "models--a--b", "etag.lock");
        }

        [TearDown]
        public void TearDown()
        {
            var root = Path.GetDirectoryName(Path.GetDirectoryName(_lockPath));
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>
        /// A held lock blocks a second holder, even through another file handle, until it is released.
        /// </summary>
        [Test]
        public void TryAcquire_BlocksSecondHolderUntilReleased()
        {
            Assert.That(BlobLock.TryAcquire(_lockPath, out var first), Is.True);
            using (first)
            {
                Assert.That(BlobLock.TryAcquire(_lockPath, out _), Is.False);
            }

            Assert.That(BlobLock.TryAcquire(_lockPath, out var second), Is.True);
            second.Dispose();
        }

        /// <summary>
        /// A waiting acquire reports that it waits once, and gets the lock after the holder releases it.
        /// </summary>
        [Test]
        public void AcquireAsync_WaitsForHolderThenAcquires()
        {
            Assert.That(BlobLock.TryAcquire(_lockPath, out var holder), Is.True);
            var waitingCount = 0;
            var acquire = Task.Run(() => BlobLock.AcquireAsync(_lockPath, () => Interlocked.Increment(ref waitingCount), CancellationToken.None));

            Assert.That(acquire.Wait(300), Is.False);
            holder.Dispose();

            Assert.That(acquire.Wait(5000), Is.True);
            acquire.Result.Dispose();
            Assert.That(waitingCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Cancelling stops the wait.
        /// </summary>
        [Test]
        public void AcquireAsync_StopsWhenCancelled()
        {
            Assert.That(BlobLock.TryAcquire(_lockPath, out var holder), Is.True);
            using (holder)
            using (var cancellation = new CancellationTokenSource(200))
            {
                var acquire = Task.Run(() => BlobLock.AcquireAsync(_lockPath, null, cancellation.Token));
                var exception = Assert.Throws<AggregateException>(() => acquire.Wait(5000));
                Assert.That(exception.InnerException, Is.InstanceOf<OperationCanceledException>());
            }
        }
    }
}
