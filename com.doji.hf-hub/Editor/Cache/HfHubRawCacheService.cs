using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using Newtonsoft.Json;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Manages shared raw ONNX cache, sidecar metadata, ref files, and file-level locking.
    /// </summary>
    internal sealed class HfHubRawCacheService : IHfHubRawCacheService
    {
        private static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(30);
        private readonly IHfHubHttpClient _httpClient;

        /// <summary>
        /// Creates cache service with optional injected HTTP client for tests.
        /// </summary>
        public HfHubRawCacheService(IHfHubHttpClient httpClient = null)
        {
            _httpClient = httpClient ?? new HfHubHttpClient();
        }

        /// <summary>
        /// Returns cached raw file if valid, otherwise resolves and downloads it.
        /// </summary>
        public async Task<HfHubCachedRawFile> GetOrDownloadAsync(
            HfHubDiscoveredModelDependency dependency,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            var resolvedFile = await _httpClient.ResolveFileVersionAsync(dependency, cancellationToken);
            var snapshotFilePath = HfHubCachePaths.GetSnapshotFilePath(
                resolvedFile.Repository,
                resolvedFile.ResolvedCommitHash,
                resolvedFile.SourceFile);
            var metadataPath = HfHubCachePaths.GetSnapshotMetadataPath(
                resolvedFile.Repository,
                resolvedFile.ResolvedCommitHash,
                resolvedFile.SourceFile);
            var lockPath = HfHubCachePaths.GetSnapshotLockPath(
                resolvedFile.Repository,
                resolvedFile.ResolvedCommitHash,
                resolvedFile.SourceFile);

            using var fileLock = FileLock.Acquire(lockPath, DefaultLockTimeout);

            if (TryReadMetadata(metadataPath, out var existingMetadata) &&
                IsCacheHit(snapshotFilePath, existingMetadata, resolvedFile))
            {
                WriteRevisionRef(resolvedFile.Repository, resolvedFile.RequestedRevision, resolvedFile.ResolvedCommitHash);
                return new HfHubCachedRawFile(snapshotFilePath, metadataPath, existingMetadata);
            }

            await _httpClient.DownloadFileAsync(resolvedFile, snapshotFilePath, progress, cancellationToken);

            var metadata = new HfHubRawCacheMetadata
            {
                Repository = resolvedFile.Repository,
                RequestedRevision = resolvedFile.RequestedRevision,
                ResolvedCommitHash = resolvedFile.ResolvedCommitHash,
                SourceFile = resolvedFile.SourceFile,
                EntityTag = resolvedFile.EntityTag,
                SizeInBytes = resolvedFile.SizeInBytes,
                CachedFilePath = snapshotFilePath,
                DownloadedAtUtc = DateTime.UtcNow.ToString("O")
            };

            WriteMetadata(metadataPath, metadata);
            WriteRevisionRef(resolvedFile.Repository, resolvedFile.RequestedRevision, resolvedFile.ResolvedCommitHash);
            return new HfHubCachedRawFile(snapshotFilePath, metadataPath, metadata);
        }

        /// <summary>
        /// Reads cached metadata file if present and valid.
        /// </summary>
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
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsCacheHit(
            string snapshotFilePath,
            HfHubRawCacheMetadata metadata,
            HfHubResolvedFileVersion resolvedFile)
        {
            if (!File.Exists(snapshotFilePath))
            {
                return false;
            }

            if (metadata == null)
            {
                return false;
            }

            if (!string.Equals(metadata.Repository, resolvedFile.Repository, StringComparison.Ordinal) ||
                !string.Equals(metadata.ResolvedCommitHash, resolvedFile.ResolvedCommitHash, StringComparison.Ordinal) ||
                !string.Equals(metadata.SourceFile, resolvedFile.SourceFile, StringComparison.Ordinal))
            {
                return false;
            }

            if (resolvedFile.SizeInBytes.HasValue)
            {
                var fileInfo = new FileInfo(snapshotFilePath);
                if (fileInfo.Length != resolvedFile.SizeInBytes.Value)
                {
                    return false;
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
