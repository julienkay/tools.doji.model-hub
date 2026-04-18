using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Records one generated runtime file inside imported repository snapshot.
    /// </summary>
    [Serializable]
    internal sealed class HfHubImportedRuntimeFileMetadata
    {
        public string RepoPath { get; set; }
        public string Kind { get; set; }
        public string AssetPath { get; set; }
        public string RelativePath { get; set; }
    }

    /// <summary>
    /// Editor-only import metadata persisted under `Library` for one synchronized repository snapshot.
    /// </summary>
    [Serializable]
    internal sealed class HfHubImportedModelMetadata
    {
        public string PackageId { get; set; }
        public string PackageVersion { get; set; }
        public string ModelId { get; set; }
        public string ModelDisplayName { get; set; }
        public string ManifestHash { get; set; }
        public string Repository { get; set; }
        public string RequestedRevision { get; set; }
        public string ResolvedCommitHash { get; set; }
        public string RawSnapshotRootPath { get; set; }
        public string RawMetadataPath { get; set; }
        public string SentisVersion { get; set; }
        public string UnityVersion { get; set; }
        public string RuntimeRegistryAssetPath { get; set; }
        public string RuntimeRegistryRelativePath { get; set; }
        public HfHubImportedRuntimeFileMetadata[] RuntimeFiles { get; set; } = Array.Empty<HfHubImportedRuntimeFileMetadata>();
        public string ImportedAtUtc { get; set; }
    }

    /// <summary>
    /// Represents one generated runtime repository snapshot and its editor-only import metadata.
    /// </summary>
    internal sealed class HfHubImportedModelArtifact
    {
        public HfHubImportedModelArtifact(
            string snapshotRootAssetPath,
            string metadataFilePath,
            HfHubImportedModelMetadata metadata)
        {
            SnapshotRootAssetPath = snapshotRootAssetPath;
            MetadataFilePath = metadataFilePath;
            Metadata = metadata;
        }

        public string SnapshotRootAssetPath { get; }
        public string MetadataFilePath { get; }
        public HfHubImportedModelMetadata Metadata { get; }
        public string RuntimeRegistryAssetPath => Metadata?.RuntimeRegistryAssetPath;
        public string RuntimeRegistryRelativePath => Metadata?.RuntimeRegistryRelativePath;
        public IReadOnlyList<HfHubImportedRuntimeFileMetadata> RuntimeFiles => Metadata?.RuntimeFiles ?? Array.Empty<HfHubImportedRuntimeFileMetadata>();

        public bool TryGetRuntimeFile(string repoPath, string kind, out HfHubImportedRuntimeFileMetadata file)
        {
            file = RuntimeFiles.FirstOrDefault(candidate =>
                string.Equals(candidate.RepoPath, repoPath, StringComparison.Ordinal) &&
                string.Equals(candidate.Kind, kind, StringComparison.Ordinal));
            return file != null;
        }
    }
}
