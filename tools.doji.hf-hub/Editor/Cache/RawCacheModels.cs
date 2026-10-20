using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Describes one file contained in a resolved HuggingFace repository snapshot and targeted for download.
    /// </summary>
    internal sealed class ResolvedRepoFile
    {
        public ResolvedRepoFile(string repoPath, string downloadUrl, string entityTag, long? sizeInBytes)
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
    /// Describes one file known to exist in a resolved HuggingFace repository snapshot.
    /// </summary>
    [Serializable]
    internal sealed class RepositoryFileMetadata
    {
        public string RepoPath { get; set; }
        public long? SizeInBytes { get; set; }
    }

    /// <summary>
    /// Describes the full file inventory for one resolved repository revision.
    /// </summary>
    internal sealed class ResolvedRepositoryInventory
    {
        private readonly RepositoryFileMetadata[] _files;

        public ResolvedRepositoryInventory(
            string repository,
            string requestedRevision,
            string resolvedCommitHash,
            IReadOnlyList<RepositoryFileMetadata> files)
        {
            Repository = repository;
            RequestedRevision = requestedRevision;
            ResolvedCommitHash = resolvedCommitHash;
            _files = files?.ToArray() ?? Array.Empty<RepositoryFileMetadata>();
        }

        public string Repository { get; }
        public string RequestedRevision { get; }
        public string ResolvedCommitHash { get; }
        public IReadOnlyList<RepositoryFileMetadata> Files => _files;
    }

    /// <summary>
    /// Describes one resolved repository snapshot returned by Hub lookup.
    /// </summary>
    internal sealed class ResolvedSnapshotVersion
    {
        private readonly RepositoryFileMetadata[] _repositoryFiles;
        private readonly ResolvedRepoFile[] _requestedFiles;

        public ResolvedSnapshotVersion(
            string repository,
            string requestedRevision,
            string resolvedCommitHash,
            IReadOnlyList<RepositoryFileMetadata> repositoryFiles,
            IReadOnlyList<ResolvedRepoFile> requestedFiles)
        {
            Repository = repository;
            RequestedRevision = requestedRevision;
            ResolvedCommitHash = resolvedCommitHash;
            _repositoryFiles = repositoryFiles?.ToArray() ?? Array.Empty<RepositoryFileMetadata>();
            _requestedFiles = requestedFiles?.ToArray() ?? Array.Empty<ResolvedRepoFile>();
        }

        public string Repository { get; }
        public string RequestedRevision { get; }
        public string ResolvedCommitHash { get; }
        public IReadOnlyList<RepositoryFileMetadata> RepositoryFiles => _repositoryFiles;
        public IReadOnlyList<ResolvedRepoFile> Files => _requestedFiles;
    }

    /// <summary>
    /// Stores one cached repository file inside shared raw snapshot cache.
    /// </summary>
    [Serializable]
    internal sealed class RawCacheFileMetadata
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
    internal sealed class RawCacheMetadata
    {
        public string Repository { get; set; }
        public string RequestedRevision { get; set; }
        public string ResolvedCommitHash { get; set; }
        public string CachedSnapshotRoot { get; set; }
        public RepositoryFileMetadata[] RepositoryFiles { get; set; } = Array.Empty<RepositoryFileMetadata>();
        public RawCacheFileMetadata[] Files { get; set; } = Array.Empty<RawCacheFileMetadata>();
        public string DownloadedAtUtc { get; set; }
    }

    /// <summary>
    /// Represents one cached repository snapshot ready in shared cache.
    /// </summary>
    internal sealed class CachedSnapshot
    {
        public CachedSnapshot(string snapshotRootPath, string metadataPath, RawCacheMetadata metadata)
        {
            SnapshotRootPath = snapshotRootPath;
            MetadataPath = metadataPath;
            Metadata = metadata;
        }

        public string SnapshotRootPath { get; }
        public string MetadataPath { get; }
        public RawCacheMetadata Metadata { get; }

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
