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
    internal sealed class RawCacheService : IRawCacheService
    {
        private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(30);
        private readonly IHttpClient _httpClient;

        public RawCacheService(IHttpClient httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        /// <summary>
        /// Returns cached repository snapshot if valid, otherwise resolves and downloads all repository files.
        /// </summary>
        public async Task<CachedSnapshot> GetOrDownloadAsync(
            DiscoveredModelDependency dependency,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            var resolvedSnapshot = await _httpClient.ResolveSnapshotVersionAsync(dependency, cancellationToken);
            var snapshotRoot = CachePaths.GetSnapshotRoot(
                resolvedSnapshot.Repository,
                resolvedSnapshot.ResolvedCommitHash);
            var metadataPath = CachePaths.GetSnapshotMetadataPath(
                resolvedSnapshot.Repository,
                resolvedSnapshot.ResolvedCommitHash);
            var lockPath = CachePaths.GetSnapshotLockPath(
                resolvedSnapshot.Repository,
                resolvedSnapshot.ResolvedCommitHash);

            using var fileLock = FileLock.Acquire(lockPath, DefaultLockTimeout);

            if (TryReadMetadata(metadataPath, out var existingMetadata) &&
                IsCacheHit(snapshotRoot, existingMetadata, resolvedSnapshot))
            {
                WriteRevisionRef(resolvedSnapshot.Repository, resolvedSnapshot.RequestedRevision, resolvedSnapshot.ResolvedCommitHash);
                return new CachedSnapshot(snapshotRoot, metadataPath, existingMetadata);
            }

            Directory.CreateDirectory(snapshotRoot);
            foreach (var file in resolvedSnapshot.Files)
            {
                var targetPath = CachePaths.GetSnapshotFilePath(
                    resolvedSnapshot.Repository,
                    resolvedSnapshot.ResolvedCommitHash,
                    file.RepoPath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? snapshotRoot);
                await _httpClient.DownloadFileAsync(file, targetPath, progress, cancellationToken);
            }

            var metadata = new RawCacheMetadata
            {
                Repository = resolvedSnapshot.Repository,
                RequestedRevision = resolvedSnapshot.RequestedRevision,
                ResolvedCommitHash = resolvedSnapshot.ResolvedCommitHash,
                CachedSnapshotRoot = snapshotRoot,
                Files = resolvedSnapshot.Files
                    .Select(file => new RawCacheFileMetadata
                    {
                        RepoPath = file.RepoPath,
                        CachedFilePath = CachePaths.GetSnapshotFilePath(
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
            return new CachedSnapshot(snapshotRoot, metadataPath, metadata);
        }

        public bool TryReadMetadata(string metadataPath, out RawCacheMetadata metadata)
        {
            metadata = null;
            if (!File.Exists(metadataPath))
            {
                return false;
            }

            try
            {
                metadata = JsonConvert.DeserializeObject<RawCacheMetadata>(File.ReadAllText(metadataPath));
                return metadata != null;
            }
            catch
            {
                return false;
            }
        }

        public CachedSnapshot GetCachedOrThrow(DiscoveredModelDependency dependency)
        {
            var revisionRefPath = CachePaths.GetRevisionRefPath(dependency.Repository, dependency.Revision);
            if (!File.Exists(revisionRefPath))
            {
                throw new InvalidOperationException(
                    $"Raw cache revision ref is missing for '{dependency.Repository}' at '{dependency.Revision}'. Download it first.");
            }

            var resolvedCommitHash = File.ReadAllText(revisionRefPath).Trim();
            if (string.IsNullOrWhiteSpace(resolvedCommitHash))
            {
                throw new InvalidOperationException(
                    $"Raw cache revision ref is invalid for '{dependency.Repository}' at '{dependency.Revision}'. Download it again.");
            }

            var snapshotRoot = CachePaths.GetSnapshotRoot(dependency.Repository, resolvedCommitHash);
            var metadataPath = CachePaths.GetSnapshotMetadataPath(dependency.Repository, resolvedCommitHash);
            if (!TryReadMetadata(metadataPath, out var metadata) ||
                !IsLocalCacheSnapshotCurrent(snapshotRoot, dependency, resolvedCommitHash, metadata))
            {
                throw new InvalidOperationException(
                    $"Raw cache snapshot is not available for '{dependency.Repository}' at '{dependency.Revision}'. Download it first.");
            }

            return new CachedSnapshot(snapshotRoot, metadataPath, metadata);
        }

        private static bool IsCacheHit(
            string snapshotRoot,
            RawCacheMetadata metadata,
            ResolvedSnapshotVersion resolvedSnapshot)
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

            var files = metadata.Files ?? Array.Empty<RawCacheFileMetadata>();
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

        private static bool IsLocalCacheSnapshotCurrent(
            string snapshotRoot,
            DiscoveredModelDependency dependency,
            string resolvedCommitHash,
            RawCacheMetadata metadata)
        {
            if (metadata == null || !Directory.Exists(snapshotRoot))
            {
                return false;
            }

            if (!string.Equals(metadata.Repository, dependency.Repository, StringComparison.Ordinal) ||
                !string.Equals(metadata.RequestedRevision, dependency.Revision, StringComparison.Ordinal) ||
                !string.Equals(metadata.ResolvedCommitHash, resolvedCommitHash, StringComparison.Ordinal) ||
                !string.Equals(metadata.CachedSnapshotRoot, snapshotRoot, StringComparison.Ordinal))
            {
                return false;
            }

            return (metadata.Files ?? Array.Empty<RawCacheFileMetadata>())
                .All(file =>
                    !string.IsNullOrWhiteSpace(file.CachedFilePath) &&
                    File.Exists(file.CachedFilePath));
        }

        private static void WriteMetadata(string metadataPath, RawCacheMetadata metadata)
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
            var refPath = CachePaths.GetRevisionRefPath(repository, requestedRevision);
            var refDirectory = Path.GetDirectoryName(refPath);
            if (!string.IsNullOrWhiteSpace(refDirectory))
            {
                Directory.CreateDirectory(refDirectory);
            }

            File.WriteAllText(refPath, resolvedCommitHash);
        }
    }
}
