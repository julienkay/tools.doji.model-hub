using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Stores import-time warnings or errors that should survive later status refreshes.
    /// </summary>
    [Serializable]
    internal sealed class HfHubImportDiagnostic
    {
        public string Severity { get; set; }
        public string Message { get; set; }
    }

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
        public long? SizeInBytes { get; set; }
    }

    /// <summary>
    /// Stores shared generated runtime artifact metadata keyed by repository snapshot identity.
    /// </summary>
    [Serializable]
    internal sealed class HfHubSharedImportMetadata
    {
        public string Repository { get; set; }
        public string ResolvedCommitHash { get; set; }
        public string RequestedRevisionHint { get; set; }
        public string RawSnapshotRootPath { get; set; }
        public string RawMetadataPath { get; set; }
        public long? RawCacheSizeInBytes { get; set; }
        public string SentisVersion { get; set; }
        public string UnityVersion { get; set; }
        public string RuntimeSnapshotAssetPath { get; set; }
        public HfHubImportDiagnostic[] Diagnostics { get; set; } = Array.Empty<HfHubImportDiagnostic>();
        public HfHubImportedRuntimeFileMetadata[] RuntimeFiles { get; set; } = Array.Empty<HfHubImportedRuntimeFileMetadata>();
        public string ImportedAtUtc { get; set; }
    }

    /// <summary>
    /// Stores owner-specific binding metadata that points at one shared generated runtime artifact.
    /// </summary>
    [Serializable]
    internal sealed class HfHubImportBindingMetadata
    {
        public string PackageId { get; set; }
        public string PackageVersion { get; set; }
        public string ModelId { get; set; }
        public string ManifestHash { get; set; }
        public string Repository { get; set; }
        public string RequestedRevision { get; set; }
        public string ResolvedCommitHash { get; set; }
        public string SentisVersion { get; set; }
        public string SharedImportMetadataPath { get; set; }
        public string RuntimeRegistryAssetPath { get; set; }
        public string RuntimeRegistryRelativePath { get; set; }
        public string BoundAtUtc { get; set; }
    }

    /// <summary>
    /// Resolved import metadata used by status, UI, and registry workflows after binding and shared records are joined.
    /// </summary>
    [Serializable]
    internal sealed class HfHubImportedModelMetadata
    {
        public string PackageId { get; set; }
        public string PackageVersion { get; set; }
        public string ModelId { get; set; }
        public string ManifestHash { get; set; }
        public string Repository { get; set; }
        public string RequestedRevision { get; set; }
        public string ResolvedCommitHash { get; set; }
        public string RawSnapshotRootPath { get; set; }
        public string RawMetadataPath { get; set; }
        public long? RawCacheSizeInBytes { get; set; }
        public string SentisVersion { get; set; }
        public string UnityVersion { get; set; }
        public string RuntimeRegistryAssetPath { get; set; }
        public string RuntimeRegistryRelativePath { get; set; }
        public string BindingMetadataPath { get; set; }
        public string SharedImportMetadataPath { get; set; }
        public HfHubImportDiagnostic[] Diagnostics { get; set; } = Array.Empty<HfHubImportDiagnostic>();
        public HfHubImportedRuntimeFileMetadata[] RuntimeFiles { get; set; } = Array.Empty<HfHubImportedRuntimeFileMetadata>();
        public string ImportedAtUtc { get; set; }
    }

    /// <summary>
    /// Represents one generated runtime repository snapshot joined with owner binding metadata.
    /// </summary>
    internal sealed class HfHubImportedModelArtifact
    {
        public HfHubImportedModelArtifact(
            string snapshotRootAssetPath,
            string metadataFilePath,
            HfHubImportedModelMetadata metadata,
            string sharedMetadataFilePath = null)
        {
            SnapshotRootAssetPath = snapshotRootAssetPath;
            MetadataFilePath = metadataFilePath;
            Metadata = metadata;
            SharedMetadataFilePath = sharedMetadataFilePath;
        }

        public string SnapshotRootAssetPath { get; }
        public string MetadataFilePath { get; }
        public string SharedMetadataFilePath { get; }
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
