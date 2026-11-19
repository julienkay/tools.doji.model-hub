using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// An import would convert ONNX files, but the caller allowed only imports that reuse converted models.
    /// </summary>
    internal sealed class ConversionRequiredException : InvalidOperationException
    {
        public ConversionRequiredException(string repository, IReadOnlyList<string> repoPaths)
            : base($"'{repository}' needs conversion of {string.Join(", ", repoPaths ?? Array.Empty<string>())}.")
        {
            RepoPaths = repoPaths?.ToArray() ?? Array.Empty<string>();
        }

        public IReadOnlyList<string> RepoPaths { get; }
    }

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
    /// Records the runtime model of one selected model file: the downloaded <c>.sentis</c> file in the Hugging Face
    /// cache, or the converted <c>.sentis</c> file of an <c>.onnx</c> file in the shared converted folder.
    /// </summary>
    [Serializable]
    internal sealed class ImportedRuntimeFileMetadata
    {
        public string RepoPath { get; set; }

        /// <summary>
        /// Always <c>sentis</c>: the runtime model is a Sentis file.
        /// </summary>
        public string Kind { get; set; }

        /// <summary>
        /// Format of the source file: <c>onnx</c> or <c>sentis</c>.
        /// </summary>
        public string Format { get; set; }

        /// <summary>
        /// Absolute path of the runtime model on this machine.
        /// </summary>
        public string FilePath { get; set; }

        public string SourceEtag { get; set; }

        public long? SourceSizeInBytes { get; set; }

        /// <summary>
        /// ONNX companion files (external weights) a player downloads with the model.
        /// </summary>
        public ImportedSourceFileMetadata[] Companions { get; set; } = Array.Empty<ImportedSourceFileMetadata>();

        /// <summary>
        /// Key in <see cref="ConvertedModelStore"/>, for converted files only.
        /// </summary>
        public string ConversionKey { get; set; }

        public long? SizeInBytes { get; set; }
        public string RecipeHash { get; set; }
        public string ConversionPipelineVersion { get; set; }

        /// <summary>
        /// Constant bytes after import, before quantization, for converted files. Null when not measured.
        /// </summary>
        public long? ImportedWeightBytes { get; set; }

        /// <summary>
        /// Constant bytes of the converted model. Null when not measured.
        /// </summary>
        public long? ConvertedWeightBytes { get; set; }
    }

    /// <summary>
    /// One source file in the Hugging Face cache, by repository path, etag, and size.
    /// </summary>
    [Serializable]
    internal sealed class ImportedSourceFileMetadata
    {
        public string RepoPath { get; set; }
        public string Etag { get; set; }
        public long? SizeInBytes { get; set; }
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
        public ImportDiagnostic[] Diagnostics { get; set; } = Array.Empty<ImportDiagnostic>();
        public ImportedRuntimeFileMetadata[] RuntimeFiles { get; set; } = Array.Empty<ImportedRuntimeFileMetadata>();

        /// <summary>
        /// Selected ONNX files that Sentis could not convert. They count as imported, so a partial
        /// conversion does not look out of date; their reasons are in <see cref="Diagnostics"/>.
        /// </summary>
        public string[] SkippedRepoPaths { get; set; } = Array.Empty<string>();

        public string ImportedAtUtc { get; set; }

        /// <summary>
        /// Model files the import handled: with a runtime model, or skipped because conversion failed. Sorted.
        /// </summary>
        public string[] GetImportedOrSkippedModelRepoPaths()
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
        public string Repository { get; set; }
        public string RequestedRevision { get; set; }
        public string ResolvedCommitHash { get; set; }
        public string SentisVersion { get; set; }
        public string RecipeSetHash { get; set; }
        public string DependencySelectionHash { get; set; }
        public string SharedImportMetadataPath { get; set; }
        public string BoundAtUtc { get; set; }
    }

    /// <summary>
    /// One import of a dependency: where its binding and shared records are, and the shared record.
    /// </summary>
    internal sealed class ImportedModelArtifact
    {
        public ImportedModelArtifact(
            string bindingMetadataFilePath,
            string sharedMetadataFilePath,
            SharedImportMetadata shared)
        {
            BindingMetadataFilePath = bindingMetadataFilePath;
            SharedMetadataFilePath = sharedMetadataFilePath;
            Shared = shared;
        }

        public string BindingMetadataFilePath { get; }
        public string SharedMetadataFilePath { get; }
        public SharedImportMetadata Shared { get; }
        public IReadOnlyList<ImportedRuntimeFileMetadata> RuntimeFiles => Shared?.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>();
    }
}
