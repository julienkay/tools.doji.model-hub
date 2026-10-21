using System;
using System.Collections.Generic;
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
        /// Returns cached repository snapshot if required files are already present, otherwise downloads only the missing required files.
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

            using var fileLock = await FileLock.AcquireAsync(lockPath, DefaultLockTimeout, cancellationToken);

            var existingMetadata = ReadSnapshot(
                resolvedSnapshot.Repository,
                resolvedSnapshot.RequestedRevision,
                resolvedSnapshot.ResolvedCommitHash);
            Directory.CreateDirectory(snapshotRoot);

            var filesToDownload = resolvedSnapshot.Files
                .Where(file => !HasCurrentCachedFile(snapshotRoot, existingMetadata, file))
                .ToArray();

            foreach (var file in filesToDownload)
            {
                await DownloadIntoSnapshotAsync(resolvedSnapshot, file, progress, cancellationToken);
            }

            var metadata = BuildMergedMetadata(snapshotRoot, existingMetadata, resolvedSnapshot);

            WriteMetadata(metadataPath, metadata);
            WriteRevisionRef(resolvedSnapshot.Repository, resolvedSnapshot.RequestedRevision, resolvedSnapshot.ResolvedCommitHash);
            return new CachedSnapshot(snapshotRoot, metadataPath, metadata);
        }

        /// <summary>
        /// Stores one file like <c>huggingface_hub</c>: content in <c>blobs/&lt;etag&gt;</c>, and the snapshot path
        /// links to it. A blob that another revision already downloaded is reused without downloading.
        /// Files without an etag are downloaded straight into the snapshot.
        /// </summary>
        private async Task DownloadIntoSnapshotAsync(
            ResolvedSnapshotVersion resolvedSnapshot,
            ResolvedRepoFile file,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            var snapshotPath = CachePaths.GetSnapshotFilePath(
                resolvedSnapshot.Repository,
                resolvedSnapshot.ResolvedCommitHash,
                file.RepoPath);
            var etag = CachePaths.NormalizeEtag(file.EntityTag);
            if (string.IsNullOrWhiteSpace(etag))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath) ?? CachePaths.GetSnapshotRoot(resolvedSnapshot.Repository, resolvedSnapshot.ResolvedCommitHash));
                await _httpClient.DownloadFileAsync(file, snapshotPath, progress, cancellationToken);
                return;
            }

            // Same lock as huggingface_hub, so Unity and Python never write one blob at the same time.
            using var blobLock = await BlobLock.AcquireAsync(
                CachePaths.GetBlobLockPath(resolvedSnapshot.Repository, etag),
                () => UnityEngine.Debug.Log($"HF Hub is waiting for another program that is downloading {file.RepoPath} of {resolvedSnapshot.Repository}."),
                cancellationToken);
            var blobPath = CachePaths.GetBlobPath(resolvedSnapshot.Repository, etag);
            var isNewBlob = !IsCompleteBlob(blobPath, file.SizeInBytes);
            if (isNewBlob)
            {
                Directory.CreateDirectory(CachePaths.GetBlobsRoot(resolvedSnapshot.Repository));
                await _httpClient.DownloadFileAsync(file, blobPath, progress, cancellationToken);
            }

            CacheFiles.LinkSnapshotFile(blobPath, snapshotPath, isNewBlob);
        }

        private static bool IsCompleteBlob(string blobPath, long? expectedSizeInBytes)
        {
            var length = CacheFiles.GetLength(blobPath);
            return length.HasValue && (!expectedSizeInBytes.HasValue || length.Value == expectedSizeInBytes.Value);
        }

        /// <summary>
        /// Reads one snapshot from disk: the files in <c>snapshots/&lt;commit&gt;</c> are the truth, whoever
        /// wrote them (HF Hub or <c>huggingface_hub</c>). HF Hub's index only adds the repository file list and
        /// etags, and only when it describes this repository and commit. A missing, stale, or broken index is
        /// ignored; the next sync rebuilds it.
        /// </summary>
        public RawCacheMetadata ReadSnapshot(string repository, string requestedRevision, string commitHash)
        {
            var snapshotRoot = CachePaths.GetSnapshotRoot(repository, commitHash);
            var hasIndex = TryReadMetadata(CachePaths.GetSnapshotMetadataPath(repository, commitHash), out var index) &&
                string.Equals(index.Repository, repository, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(index.ResolvedCommitHash, commitHash, StringComparison.Ordinal);
            var indexedFiles = hasIndex
                ? (index.Files ?? Array.Empty<RawCacheFileMetadata>())
                    .Where(file => !string.IsNullOrWhiteSpace(file?.RepoPath))
                    .GroupBy(file => file.RepoPath, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal)
                : new Dictionary<string, RawCacheFileMetadata>(StringComparer.Ordinal);

            var files = new List<RawCacheFileMetadata>();
            if (Directory.Exists(snapshotRoot))
            {
                var rootPrefix = Path.GetFullPath(snapshotRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (var filePath in Directory.EnumerateFiles(snapshotRoot, "*", SearchOption.AllDirectories))
                {
                    if (string.Equals(Path.GetFileName(filePath), ".DS_Store", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var repoPath = Path.GetFullPath(filePath).Substring(rootPrefix.Length).Replace(Path.DirectorySeparatorChar, '/');
                    indexedFiles.TryGetValue(repoPath, out var indexed);
                    files.Add(new RawCacheFileMetadata
                    {
                        RepoPath = repoPath,
                        CachedFilePath = filePath,
                        EntityTag = indexed?.EntityTag,
                        SizeInBytes = CacheFiles.GetLength(filePath)
                    });
                }
            }

            return new RawCacheMetadata
            {
                Repository = repository,
                RequestedRevision = requestedRevision,
                ResolvedCommitHash = commitHash,
                CachedSnapshotRoot = snapshotRoot,
                RepositoryFiles = hasIndex ? index.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>() : Array.Empty<RepositoryFileMetadata>(),
                Files = files.OrderBy(file => file.RepoPath, StringComparer.Ordinal).ToArray(),
                DownloadedAtUtc = hasIndex ? index.DownloadedAtUtc : null
            };
        }

        /// <summary>
        /// Reads HF Hub's index entry for one snapshot. Prefer <see cref="ReadSnapshot"/>, which checks the disk.
        /// </summary>
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
            var metadata = ReadSnapshot(dependency.Repository, dependency.Revision, resolvedCommitHash);
            if (!HasSelectedFiles(dependency, metadata))
            {
                throw new InvalidOperationException(
                    $"Raw cache snapshot is not available for '{dependency.Repository}' at '{dependency.Revision}'. Download it first.");
            }

            return new CachedSnapshot(snapshotRoot, metadataPath, metadata);
        }

        /// <summary>
        /// Whether a snapshot file is on disk with the expected size. Snapshot paths are tied to one commit, so an
        /// existing file with the right size is current.
        /// </summary>
        private static bool HasCurrentCachedFile(
            string snapshotRoot,
            RawCacheMetadata metadata,
            ResolvedRepoFile resolvedFile)
        {
            var cachedFile = (metadata?.Files ?? Array.Empty<RawCacheFileMetadata>())
                .FirstOrDefault(file => string.Equals(file.RepoPath, resolvedFile.RepoPath, StringComparison.Ordinal));
            if (cachedFile == null || !File.Exists(cachedFile.CachedFilePath))
            {
                return false;
            }

            return !resolvedFile.SizeInBytes.HasValue ||
                CacheFiles.GetLength(cachedFile.CachedFilePath) == resolvedFile.SizeInBytes.Value;
        }

        /// <summary>
        /// Whether every file the dependency selects is on disk. A whole-repository dependency needs the
        /// repository file list from the index to know what "every file" is.
        /// </summary>
        internal static bool HasSelectedFiles(DiscoveredModelDependency dependency, RawCacheMetadata snapshot)
        {
            var repositoryFiles = (snapshot?.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>()).Select(file => file.RepoPath).ToArray();
            if (dependency.SelectsWholeRepository && repositoryFiles.Length == 0)
            {
                return false;
            }

            var cachedPaths = new HashSet<string>(
                (snapshot?.Files ?? Array.Empty<RawCacheFileMetadata>()).Select(file => file.RepoPath),
                StringComparer.Ordinal);
            var selectedRepoPaths = dependency.GetSelectedRepoPaths(repositoryFiles, cachedPaths);
            return selectedRepoPaths.Count > 0 && selectedRepoPaths.All(cachedPaths.Contains);
        }

        private static RawCacheMetadata BuildMergedMetadata(
            string snapshotRoot,
            RawCacheMetadata existingMetadata,
            ResolvedSnapshotVersion resolvedSnapshot)
        {
            var knownRepositoryPaths = new HashSet<string>(
                (resolvedSnapshot.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>())
                .Select(file => file.RepoPath),
                StringComparer.Ordinal);
            var mergedFiles = new Dictionary<string, RawCacheFileMetadata>(StringComparer.Ordinal);

            foreach (var existingFile in (existingMetadata?.Files ?? Array.Empty<RawCacheFileMetadata>())
                         .Where(file =>
                             file != null &&
                             !string.IsNullOrWhiteSpace(file.RepoPath) &&
                             !string.IsNullOrWhiteSpace(file.CachedFilePath) &&
                             File.Exists(file.CachedFilePath) &&
                             knownRepositoryPaths.Contains(file.RepoPath)))
            {
                mergedFiles[existingFile.RepoPath] = existingFile;
            }

            foreach (var resolvedFile in resolvedSnapshot.Files)
            {
                mergedFiles[resolvedFile.RepoPath] = new RawCacheFileMetadata
                {
                    RepoPath = resolvedFile.RepoPath,
                    CachedFilePath = CachePaths.GetSnapshotFilePath(
                        resolvedSnapshot.Repository,
                        resolvedSnapshot.ResolvedCommitHash,
                        resolvedFile.RepoPath),
                    EntityTag = resolvedFile.EntityTag,
                    SizeInBytes = resolvedFile.SizeInBytes
                };
            }

            return new RawCacheMetadata
            {
                Repository = resolvedSnapshot.Repository,
                RequestedRevision = resolvedSnapshot.RequestedRevision,
                ResolvedCommitHash = resolvedSnapshot.ResolvedCommitHash,
                CachedSnapshotRoot = snapshotRoot,
                RepositoryFiles = (resolvedSnapshot.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>())
                    .OrderBy(file => file.RepoPath, StringComparer.Ordinal)
                    .ToArray(),
                Files = mergedFiles.Values
                    .OrderBy(file => file.RepoPath, StringComparer.Ordinal)
                    .ToArray(),
                DownloadedAtUtc = DateTime.UtcNow.ToString("O")
            };
        }

        internal static void WriteMetadata(string metadataPath, RawCacheMetadata metadata)
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

        internal static void WriteRevisionRef(string repository, string requestedRevision, string resolvedCommitHash)
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
