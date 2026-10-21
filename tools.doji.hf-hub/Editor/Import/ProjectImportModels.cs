using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Stores import-time warnings or errors that should survive later status refreshes.
    /// </summary>
    [Serializable]
    internal sealed class ImportDiagnostic
    {
        public string Severity { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Records one generated runtime file inside imported repository snapshot.
    /// </summary>
    [Serializable]
    internal sealed class ImportedRuntimeFileMetadata
    {
        public string RepoPath { get; set; }
        public string Kind { get; set; }
        public string AssetPath { get; set; }
        public string RelativePath { get; set; }
        public long? SizeInBytes { get; set; }
        public string RecipeHash { get; set; }
        public string ConversionPipelineVersion { get; set; }
        public string InputSignatureHash { get; set; }
    }

    /// <summary>
    /// Stores shared generated runtime artifact metadata keyed by repository snapshot identity.
    /// </summary>
    [Serializable]
    internal sealed class SharedImportMetadata
    {
        public string Repository { get; set; }
        public string ResolvedCommitHash { get; set; }
        public string RequestedRevisionHint { get; set; }
        public string RawSnapshotRootPath { get; set; }
        public string RawMetadataPath { get; set; }
        public RepositoryFileMetadata[] RepositoryFiles { get; set; } = Array.Empty<RepositoryFileMetadata>();
        public long? RawCacheSizeInBytes { get; set; }
        public string SentisVersion { get; set; }
        public string UnityVersion { get; set; }
        public string RecipeSetHash { get; set; }
        public string RuntimeSnapshotAssetPath { get; set; }
        public ImportDiagnostic[] Diagnostics { get; set; } = Array.Empty<ImportDiagnostic>();
        public ImportedRuntimeFileMetadata[] RuntimeFiles { get; set; } = Array.Empty<ImportedRuntimeFileMetadata>();

        /// <summary>
        /// Selected ONNX files that Sentis could not convert. They count as imported, so a partial
        /// conversion does not look out of date; their reasons are in <see cref="Diagnostics"/>.
        /// </summary>
        public string[] SkippedRepoPaths { get; set; } = Array.Empty<string>();

        public string ImportedAtUtc { get; set; }

        /// <summary>
        /// ONNX files the import handled: converted to <c>.sentis</c>, or skipped as unsupported. Sorted.
        /// </summary>
        public string[] GetImportedOrSkippedOnnxRepoPaths()
        {
            return (RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>())
                .Where(file => string.Equals(file?.Kind, "sentis", StringComparison.Ordinal))
                .Select(file => file.RepoPath)
                .Concat(SkippedRepoPaths ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
    }

    /// <summary>
    /// Stores owner-specific binding metadata that points at one shared generated runtime artifact.
    /// </summary>
    [Serializable]
    internal sealed class ImportBindingMetadata
    {
        public string PackageId { get; set; }
        public string PackageVersion { get; set; }
        public string ModelId { get; set; }
        public string ManifestHash { get; set; }
        public string Repository { get; set; }
        public string RequestedRevision { get; set; }
        public string ResolvedCommitHash { get; set; }
        public string SentisVersion { get; set; }
        public string RecipeSetHash { get; set; }
        public string DependencySelectionHash { get; set; }
        public string SharedImportMetadataPath { get; set; }
        public string RuntimeRegistryAssetPath { get; set; }
        public string RuntimeRegistryRelativePath { get; set; }
        public string BoundAtUtc { get; set; }
    }

    /// <summary>
    /// Resolved import metadata used by status, UI, and registry workflows after binding and shared records are joined.
    /// </summary>
    [Serializable]
    internal sealed class ImportedModelMetadata
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
        public RepositoryFileMetadata[] RepositoryFiles { get; set; } = Array.Empty<RepositoryFileMetadata>();
        public long? RawCacheSizeInBytes { get; set; }
        public string SentisVersion { get; set; }
        public string UnityVersion { get; set; }
        public string RecipeSetHash { get; set; }
        public string DependencySelectionHash { get; set; }
        public string RuntimeRegistryAssetPath { get; set; }
        public string RuntimeRegistryRelativePath { get; set; }
        public string BindingMetadataPath { get; set; }
        public string SharedImportMetadataPath { get; set; }
        public ImportDiagnostic[] Diagnostics { get; set; } = Array.Empty<ImportDiagnostic>();
        public ImportedRuntimeFileMetadata[] RuntimeFiles { get; set; } = Array.Empty<ImportedRuntimeFileMetadata>();
        public string[] SkippedRepoPaths { get; set; } = Array.Empty<string>();
        public string ImportedAtUtc { get; set; }
    }

    /// <summary>
    /// Represents one generated runtime repository snapshot joined with owner binding metadata.
    /// </summary>
    internal sealed class ImportedModelArtifact
    {
        public ImportedModelArtifact(
            string snapshotRootAssetPath,
            string bindingMetadataFilePath,
            ImportedModelMetadata metadata,
            string sharedMetadataFilePath = null)
        {
            SnapshotRootAssetPath = snapshotRootAssetPath;
            BindingMetadataFilePath = bindingMetadataFilePath;
            Metadata = metadata;
            SharedMetadataFilePath = sharedMetadataFilePath;
        }

        public string SnapshotRootAssetPath { get; }
        public string BindingMetadataFilePath { get; }
        public string SharedMetadataFilePath { get; }
        public ImportedModelMetadata Metadata { get; }
        public string RuntimeRegistryAssetPath => Metadata?.RuntimeRegistryAssetPath;
        public string RuntimeRegistryRelativePath => Metadata?.RuntimeRegistryRelativePath;
        public IReadOnlyList<ImportedRuntimeFileMetadata> RuntimeFiles => Metadata?.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>();

        public bool TryGetRuntimeFile(string repoPath, string kind, out ImportedRuntimeFileMetadata file)
        {
            file = RuntimeFiles.FirstOrDefault(candidate =>
                string.Equals(candidate.RepoPath, repoPath, StringComparison.Ordinal) &&
                string.Equals(candidate.Kind, kind, StringComparison.Ordinal));
            return file != null;
        }
    }
}
