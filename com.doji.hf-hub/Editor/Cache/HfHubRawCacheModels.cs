using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Describes one file contained in a resolved HuggingFace repository snapshot.
    /// </summary>
    internal sealed class HfHubResolvedRepoFile
    {
        public HfHubResolvedRepoFile(string repoPath, string downloadUrl, string entityTag, long? sizeInBytes)
        {
            RepoPath = repoPath;
            DownloadUrl = downloadUrl;
            EntityTag = entityTag;
            SizeInBytes = sizeInBytes;
        }

        public string RepoPath { get; }
        public string DownloadUrl { get; }
        public string EntityTag { get; }
        public long? SizeInBytes { get; }
    }

    /// <summary>
    /// Describes one resolved repository snapshot returned by Hub lookup.
    /// </summary>
    internal sealed class HfHubResolvedSnapshotVersion
    {
        private readonly HfHubResolvedRepoFile[] _files;

        public HfHubResolvedSnapshotVersion(
            string repository,
            string requestedRevision,
            string resolvedCommitHash,
            IReadOnlyList<HfHubResolvedRepoFile> files)
        {
            Repository = repository;
            RequestedRevision = requestedRevision;
            ResolvedCommitHash = resolvedCommitHash;
            _files = files?.ToArray() ?? Array.Empty<HfHubResolvedRepoFile>();
        }

        public string Repository { get; }
        public string RequestedRevision { get; }
        public string ResolvedCommitHash { get; }
        public IReadOnlyList<HfHubResolvedRepoFile> Files => _files;
    }

    /// <summary>
    /// Stores one cached repository file inside shared raw snapshot cache.
    /// </summary>
    [Serializable]
    internal sealed class HfHubRawCacheFileMetadata
    {
        public string RepoPath { get; set; }
        public string CachedFilePath { get; set; }
        public string EntityTag { get; set; }
        public long? SizeInBytes { get; set; }
    }

    /// <summary>
    /// Sidecar metadata written once per cached repository snapshot.
    /// </summary>
    [Serializable]
    internal sealed class HfHubRawCacheMetadata
    {
        public string Repository { get; set; }
        public string RequestedRevision { get; set; }
        public string ResolvedCommitHash { get; set; }
        public string CachedSnapshotRoot { get; set; }
        public HfHubRawCacheFileMetadata[] Files { get; set; } = Array.Empty<HfHubRawCacheFileMetadata>();
        public string DownloadedAtUtc { get; set; }
    }

    /// <summary>
    /// Represents one cached repository snapshot ready in shared cache.
    /// </summary>
    internal sealed class HfHubCachedSnapshot
    {
        public HfHubCachedSnapshot(string snapshotRootPath, string metadataPath, HfHubRawCacheMetadata metadata)
        {
            SnapshotRootPath = snapshotRootPath;
            MetadataPath = metadataPath;
            Metadata = metadata;
        }

        public string SnapshotRootPath { get; }
        public string MetadataPath { get; }
        public HfHubRawCacheMetadata Metadata { get; }

        public bool TryGetFilePath(string repoPath, out string filePath)
        {
            var match = Metadata?.Files?.FirstOrDefault(file =>
                string.Equals(file.RepoPath, repoPath, StringComparison.Ordinal));
            if (match == null)
            {
                filePath = null;
                return false;
            }

            filePath = match.CachedFilePath;
            return true;
        }
    }
}
