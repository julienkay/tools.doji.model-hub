using System;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Describes resolved remote file version metadata returned by Hub lookup.
    /// </summary>
    internal sealed class HfHubResolvedFileVersion
    {
        /// <summary>
        /// Stores resolved remote file metadata used by cache and download code.
        /// </summary>
        public HfHubResolvedFileVersion(
            string repository,
            string requestedRevision,
            string resolvedCommitHash,
            string sourceFile,
            string downloadUrl,
            string entityTag,
            long? sizeInBytes)
        {
            Repository = repository;
            RequestedRevision = requestedRevision;
            ResolvedCommitHash = resolvedCommitHash;
            SourceFile = sourceFile;
            DownloadUrl = downloadUrl;
            EntityTag = entityTag;
            SizeInBytes = sizeInBytes;
        }

        public string Repository { get; }
        public string RequestedRevision { get; }
        public string ResolvedCommitHash { get; }
        public string SourceFile { get; }
        public string DownloadUrl { get; }
        public string EntityTag { get; }
        public long? SizeInBytes { get; }
    }

    /// <summary>
    /// Sidecar metadata written next to cached raw model file.
    /// </summary>
    [Serializable]
    internal sealed class HfHubRawCacheMetadata
    {
        public string Repository { get; set; }
        public string RequestedRevision { get; set; }
        public string ResolvedCommitHash { get; set; }
        public string SourceFile { get; set; }
        public string EntityTag { get; set; }
        public long? SizeInBytes { get; set; }
        public string CachedFilePath { get; set; }
        public string DownloadedAtUtc { get; set; }
    }

    /// <summary>
    /// Represents one raw model file ready in shared cache.
    /// </summary>
    internal sealed class HfHubCachedRawFile
    {
        /// <summary>
        /// Stores final file path and metadata for one cached raw ONNX file.
        /// </summary>
        public HfHubCachedRawFile(string filePath, string metadataPath, HfHubRawCacheMetadata metadata)
        {
            FilePath = filePath;
            MetadataPath = metadataPath;
            Metadata = metadata;
        }

        public string FilePath { get; }
        public string MetadataPath { get; }
        public HfHubRawCacheMetadata Metadata { get; }
    }
}
