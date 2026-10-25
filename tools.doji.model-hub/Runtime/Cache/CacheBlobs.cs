using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Stores one repository file in the cache like <c>huggingface_hub</c>: content in <c>blobs/&lt;etag&gt;</c>, and the
    /// snapshot path links to it. The Editor and players both use it, so they name, lock, and link blobs the same way.
    /// </summary>
    internal static class CacheBlobs
    {
        /// <summary>
        /// Downloads one file into the cache and returns its snapshot path. A blob that is already complete, for
        /// example from another revision or from Python, is linked without downloading. A file without an etag is
        /// downloaded straight into the snapshot.
        /// </summary>
        /// <param name="download">Writes the file to the given path.</param>
        /// <param name="onWaiting">Called once when another program holds the blob lock.</param>
        public static async Task<string> StoreAsync(
            string repoId,
            string commit,
            string repoPath,
            string etag,
            long? sizeInBytes,
            Func<string, Task> download,
            Action onWaiting,
            CancellationToken cancellationToken)
        {
            if (download == null)
            {
                throw new ArgumentNullException(nameof(download));
            }

            var snapshotPath = CachePaths.GetSnapshotFilePath(repoId, commit, repoPath);
            var normalizedEtag = CachePaths.NormalizeEtag(etag);
            if (string.IsNullOrWhiteSpace(normalizedEtag))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath) ?? CachePaths.GetSnapshotRoot(repoId, commit));
                await download(snapshotPath);
                return snapshotPath;
            }

            // Same lock as huggingface_hub, so Unity and Python never write one blob at the same time.
            using var blobLock = await BlobLock.AcquireAsync(CachePaths.GetBlobLockPath(repoId, normalizedEtag), onWaiting, cancellationToken);
            var blobPath = CachePaths.GetBlobPath(repoId, normalizedEtag);
            var isNewBlob = !IsComplete(blobPath, sizeInBytes);
            if (isNewBlob)
            {
                Directory.CreateDirectory(CachePaths.GetBlobsRoot(repoId));
                await download(blobPath);
            }

            CacheFiles.LinkSnapshotFile(blobPath, snapshotPath, isNewBlob);
            return snapshotPath;
        }

        /// <summary>
        /// Whether a file exists with the expected size. An unknown size accepts any existing file.
        /// </summary>
        public static bool IsComplete(string path, long? sizeInBytes)
        {
            var length = CacheFiles.GetLength(path);
            return length.HasValue && (!sizeInBytes.HasValue || sizeInBytes.Value <= 0 || length.Value == sizeInBytes.Value);
        }
    }
}
