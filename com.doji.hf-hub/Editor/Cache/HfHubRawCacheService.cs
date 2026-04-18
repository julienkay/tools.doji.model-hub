using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using Newtonsoft.Json;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Manages shared raw repository snapshot cache, sidecar metadata, revision refs, and file-level locking.
    /// </summary>
    internal sealed class HfHubRawCacheService : IHfHubRawCacheService
    {
        private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(30);
        private readonly IHfHubHttpClient _httpClient;

        public HfHubRawCacheService(IHfHubHttpClient httpClient = null)
        {
            _httpClient = httpClient ?? new HfHubHttpClient();
        }

        /// <summary>
        /// Returns cached repository snapshot if valid, otherwise resolves and downloads all repository files.
        /// </summary>
        public async Task<HfHubCachedSnapshot> GetOrDownloadAsync(
            HfHubDiscoveredModelDependency dependency,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            var resolvedSnapshot = await _httpClient.ResolveSnapshotVersionAsync(dependency, cancellationToken);
            var snapshotRoot = HfHubCachePaths.GetSnapshotRoot(
                resolvedSnapshot.Repository,
                resolvedSnapshot.ResolvedCommitHash);
            var metadataPath = HfHubCachePaths.GetSnapshotMetadataPath(
                resolvedSnapshot.Repository,
                resolvedSnapshot.ResolvedCommitHash);
            var lockPath = HfHubCachePaths.GetSnapshotLockPath(
                resolvedSnapshot.Repository,
                resolvedSnapshot.ResolvedCommitHash);

            using var fileLock = FileLock.Acquire(lockPath, DefaultLockTimeout);

            if (TryReadMetadata(metadataPath, out var existingMetadata) &&
                IsCacheHit(snapshotRoot, existingMetadata, resolvedSnapshot))
            {
                WriteRevisionRef(resolvedSnapshot.Repository, resolvedSnapshot.RequestedRevision, resolvedSnapshot.ResolvedCommitHash);
                return new HfHubCachedSnapshot(snapshotRoot, metadataPath, existingMetadata);
            }

            Directory.CreateDirectory(snapshotRoot);
            foreach (var file in resolvedSnapshot.Files)
            {
                var targetPath = HfHubCachePaths.GetSnapshotFilePath(
                    resolvedSnapshot.Repository,
                    resolvedSnapshot.ResolvedCommitHash,
                    file.RepoPath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? snapshotRoot);
                await _httpClient.DownloadFileAsync(file, targetPath, progress, cancellationToken);
            }

            var metadata = new HfHubRawCacheMetadata
            {
                Repository = resolvedSnapshot.Repository,
                RequestedRevision = resolvedSnapshot.RequestedRevision,
                ResolvedCommitHash = resolvedSnapshot.ResolvedCommitHash,
                CachedSnapshotRoot = snapshotRoot,
                Files = resolvedSnapshot.Files
                    .Select(file => new HfHubRawCacheFileMetadata
                    {
                        RepoPath = file.RepoPath,
                        CachedFilePath = HfHubCachePaths.GetSnapshotFilePath(
                            resolvedSnapshot.Repository,
                            resolvedSnapshot.ResolvedCommitHash,
                            file.RepoPath),
                        EntityTag = file.EntityTag,
                        SizeInBytes = file.SizeInBytes
                    })
                    .ToArray(),
                DownloadedAtUtc = DateTime.UtcNow.ToString("O")
            };

            WriteMetadata(metadataPath, metadata);
            WriteRevisionRef(resolvedSnapshot.Repository, resolvedSnapshot.RequestedRevision, resolvedSnapshot.ResolvedCommitHash);
            return new HfHubCachedSnapshot(snapshotRoot, metadataPath, metadata);
        }

        public bool TryReadMetadata(string metadataPath, out HfHubRawCacheMetadata metadata)
        {
            metadata = null;
            if (!File.Exists(metadataPath))
            {
                return false;
            }

            try
            {
                metadata = JsonConvert.DeserializeObject<HfHubRawCacheMetadata>(File.ReadAllText(metadataPath));
                return metadata != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsCacheHit(
            string snapshotRoot,
            HfHubRawCacheMetadata metadata,
            HfHubResolvedSnapshotVersion resolvedSnapshot)
        {
            if (metadata == null || !Directory.Exists(snapshotRoot))
            {
                return false;
            }

            if (!string.Equals(metadata.Repository, resolvedSnapshot.Repository, StringComparison.Ordinal) ||
                !string.Equals(metadata.ResolvedCommitHash, resolvedSnapshot.ResolvedCommitHash, StringComparison.Ordinal) ||
                !string.Equals(metadata.CachedSnapshotRoot, snapshotRoot, StringComparison.Ordinal))
            {
                return false;
            }

            var files = metadata.Files ?? Array.Empty<HfHubRawCacheFileMetadata>();
            if (files.Length != resolvedSnapshot.Files.Count)
            {
                return false;
            }

            foreach (var resolvedFile in resolvedSnapshot.Files)
            {
                var cachedFile = files.FirstOrDefault(file =>
                    string.Equals(file.RepoPath, resolvedFile.RepoPath, StringComparison.Ordinal));
                if (cachedFile == null || !File.Exists(cachedFile.CachedFilePath))
                {
                    return false;
                }

                if (resolvedFile.SizeInBytes.HasValue)
                {
                    var fileInfo = new FileInfo(cachedFile.CachedFilePath);
                    if (fileInfo.Length != resolvedFile.SizeInBytes.Value)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static void WriteMetadata(string metadataPath, HfHubRawCacheMetadata metadata)
        {
            var metadataDirectory = Path.GetDirectoryName(metadataPath);
            if (!string.IsNullOrWhiteSpace(metadataDirectory))
            {
                Directory.CreateDirectory(metadataDirectory);
            }

            var temporaryPath = metadataPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(metadata, Formatting.Indented));

            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
            }

            File.Move(temporaryPath, metadataPath);
        }

        private static void WriteRevisionRef(string repository, string requestedRevision, string resolvedCommitHash)
        {
            var refPath = HfHubCachePaths.GetRevisionRefPath(repository, requestedRevision);
            var refDirectory = Path.GetDirectoryName(refPath);
            if (!string.IsNullOrWhiteSpace(refDirectory))
            {
                Directory.CreateDirectory(refDirectory);
            }

            File.WriteAllText(refPath, resolvedCommitHash);
        }
    }
}
