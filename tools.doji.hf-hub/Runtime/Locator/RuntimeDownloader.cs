using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using UnityEngine.Networking;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Downloads repository files into the device's Hugging Face cache at a pinned commit, with the same layout,
    /// blob names, and locks as the Editor and <c>huggingface_hub</c>. On desktop players this is the user's shared
    /// cache, so a model that Python or the Editor already downloaded is not downloaded again.
    /// </summary>
    internal static class RuntimeDownloader
    {
        /// <summary>
        /// Test hook that replaces the network: receives the URL and the target path, and writes the file.
        /// </summary>
        internal static Func<string, string, IProgress<DownloadProgress>, CancellationToken, Task> DownloadOverride { get; set; }

        /// <summary>
        /// Returns the snapshot path of one file, downloading it first when it is not in the cache.
        /// </summary>
        public static async Task<string> EnsureFileAsync(
            string repoId,
            string revision,
            string commit,
            RuntimeSourceFile file,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            if (file == null || string.IsNullOrWhiteSpace(file.repoPath))
            {
                throw new ArgumentException("A source file is required.", nameof(file));
            }

            var snapshotPath = CachePaths.GetSnapshotFilePath(repoId, commit, file.repoPath);
            if (CacheBlobs.IsComplete(snapshotPath, file.size))
            {
                return snapshotPath;
            }

            var url = BuildResolveUrl(repoId, commit, file.repoPath);
            await CacheBlobs.StoreAsync(
                repoId,
                commit,
                file.repoPath,
                file.etag,
                file.size,
                targetPath => DownloadAsync(repoId, file.repoPath, url, targetPath, file.size, progress, cancellationToken),
                null,
                cancellationToken);

            WriteRefIfMissing(repoId, revision, commit);
            PersistentStorage.Flush();
            return snapshotPath;
        }

        internal static string BuildResolveUrl(string repoId, string commit, string repoPath)
        {
            var escapedPath = string.Join("/", repoPath.Split('/').Select(Escape));
            return $"{HubEndpoint.Url}/{repoId}/resolve/{Escape(commit)}/{escapedPath}";
        }

        private static string Escape(string segment)
        {
            return UnityWebRequest.EscapeURL(segment ?? string.Empty).Replace("+", "%20");
        }

        private static async Task DownloadAsync(
            string repoId,
            string repoPath,
            string url,
            string targetPath,
            long size,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? string.Empty);
            if (DownloadOverride != null)
            {
                await DownloadOverride(url, targetPath, progress, cancellationToken);
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            // The Web has no streaming download handler, so the file arrives in memory and is written once.
            using var request = UnityWebRequest.Get(url);
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new DownloadProgress((long)request.downloadedBytes, size > 0 ? size : (long?)null));
                await Task.Yield();
            }

            if (HubAccess.IsDenied(request.responseCode))
            {
                throw HubAccess.FileDenied(repoId, repoPath);
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new IOException($"Download of '{url}' failed: {request.error}");
            }

            File.WriteAllBytes(targetPath, request.downloadHandler.data);
#else
            try
            {
                await new ResumableDownloader().DownloadFileAsync(url, targetPath, size > 0 ? size : (long?)null, progress, cancellationToken);
            }
            catch (WebRequestException exception) when (HubAccess.IsDenied(exception.ResponseCode))
            {
                throw HubAccess.FileDenied(repoId, repoPath, exception);
            }
#endif
        }

        private static void WriteRefIfMissing(string repoId, string revision, string commit)
        {
            if (string.IsNullOrWhiteSpace(revision) || string.Equals(revision, commit, StringComparison.Ordinal))
            {
                return;
            }

            var refPath = CachePaths.GetRevisionRefPath(repoId, revision);
            if (File.Exists(refPath))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(refPath) ?? string.Empty);
            File.WriteAllText(refPath, commit);
        }
    }
}
