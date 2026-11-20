using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// High-level presentation state used by the Model Hub window.
    /// </summary>
    internal enum ModelHubItemState
    {
        Ready,
        ActionRequired,
        Error,

        /// <summary>
        /// An optional model that this project did not enable. It needs no action.
        /// </summary>
        NotEnabled
    }

    /// <summary>
    /// Structured diagnostic shown in repository-centric Model Hub details.
    /// </summary>
    internal sealed class ModelHubDiagnostic
    {
        public ModelHubDiagnostic(string severity, string message)
        {
            Severity = severity;
            Message = message;
        }

        public string Severity { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Represents one repository-relative ONNX file row in Model Hub details.
    /// </summary>
    internal sealed class ModelHubFileItem
    {
        public ModelHubFileItem(
            string repoPath,
            bool isDependencySelected,
            int selectedOwnerCount,
            string rawCachedFilePath,
            long? rawSizeInBytes,
            long? rawSizeOnDiskBytes,
            ImportedRuntimeFileMetadata runtimeFile,
            long? runtimeSizeOnDiskBytes,
            OnnxConversionRecipe recipe,
            string recipeSourceLabel,
            bool hasProjectOverride)
        {
            RepoPath = repoPath;
            IsDependencySelected = isDependencySelected;
            SelectedOwnerCount = Math.Max(0, selectedOwnerCount);
            RawCachedFilePath = rawCachedFilePath;
            RawSizeInBytes = rawSizeInBytes;
            RawSizeOnDiskBytes = rawSizeOnDiskBytes;
            RuntimeFile = runtimeFile;
            RuntimeSizeOnDiskBytes = runtimeSizeOnDiskBytes;
            Recipe = recipe;
            RecipeSourceLabel = recipeSourceLabel;
            HasProjectOverride = hasProjectOverride;
        }

        public string RepoPath { get; }
        public bool IsDependencySelected { get; }
        public int SelectedOwnerCount { get; }
        public string RawCachedFilePath { get; }
        public long? RawSizeInBytes { get; }
        public long? RawSizeOnDiskBytes { get; }
        public ImportedRuntimeFileMetadata RuntimeFile { get; }
        public long? RuntimeSizeOnDiskBytes { get; }
        public OnnxConversionRecipe Recipe { get; }
        public string RecipeSourceLabel { get; }
        public bool HasProjectOverride { get; }
        public bool IsOnnxFile => ModelDependencyDefinition.IsOnnxFile(RepoPath);
        public bool SupportsImportRecipeEditing => IsDependencySelected && IsOnnxFile;
        public bool HasRawFile => !string.IsNullOrWhiteSpace(RawCachedFilePath);
        public bool HasRuntimeFile => RuntimeFile != null;
    }

    /// <summary>
    /// Represents one repository-centric item in the Model Hub inventory view.
    /// </summary>
    internal sealed class ModelHubItem
    {
        private readonly DependencyStatus[] _dependents;
        private ModelHubDiagnostic[] _diagnostics;
        private readonly ImportedRuntimeFileMetadata[] _runtimeFiles;
        private readonly ModelHubFileItem[] _files;

        public ModelHubItem(
            string repository,
            string requestedRevision,
            string resolvedCommitHash,
            string rawSnapshotRootPath,
            string runtimeModelFolder,
            long? cacheSizeOnDiskBytes,
            long? estimatedCacheSizeBytes,
            long? repositorySizeBytes,
            long? runtimeSizeOnDiskBytes,
            ModelHubItemState state,
            IReadOnlyList<DependencyStatus> dependents,
            IReadOnlyList<ModelHubDiagnostic> diagnostics,
            IReadOnlyList<ImportedRuntimeFileMetadata> runtimeFiles,
            IReadOnlyList<ModelHubFileItem> files,
            ModelCardMetadata modelCard = null)
        {
            ModelCard = modelCard;
            Repository = repository;
            RequestedRevision = requestedRevision;
            ResolvedCommitHash = resolvedCommitHash;
            RawSnapshotRootPath = rawSnapshotRootPath;
            RuntimeModelFolder = runtimeModelFolder;
            CacheSizeOnDiskBytes = cacheSizeOnDiskBytes;
            EstimatedCacheSizeBytes = estimatedCacheSizeBytes;
            RepositorySizeBytes = repositorySizeBytes;
            RuntimeSizeOnDiskBytes = runtimeSizeOnDiskBytes;
            State = state;
            _dependents = dependents?.ToArray() ?? Array.Empty<DependencyStatus>();
            _diagnostics = diagnostics?.ToArray() ?? Array.Empty<ModelHubDiagnostic>();
            _runtimeFiles = runtimeFiles?.ToArray() ?? Array.Empty<ImportedRuntimeFileMetadata>();
            _files = files?.ToArray() ?? Array.Empty<ModelHubFileItem>();
        }

        public string Repository { get; }
        public string RequestedRevision { get; }
        public string ResolvedCommitHash { get; }

        /// <summary>
        /// The model card's license fields, or null before the Hub was asked.
        /// </summary>
        public ModelCardMetadata ModelCard { get; }
        public string RawSnapshotRootPath { get; }
        public string RuntimeModelFolder { get; }
        public long? CacheSizeOnDiskBytes { get; }
        public long? EstimatedCacheSizeBytes { get; }
        public long? RepositorySizeBytes { get; }
        public long? SourceSizeBytes => EstimatedCacheSizeBytes;
        public long? RuntimeSizeOnDiskBytes { get; }
        public ModelHubItemState State { get; }
        public IReadOnlyList<DependencyStatus> Dependents => _dependents;
        public IReadOnlyList<ModelHubDiagnostic> Diagnostics => _diagnostics;
        public IReadOnlyList<ImportedRuntimeFileMetadata> RuntimeFiles => _runtimeFiles;
        public IReadOnlyList<ModelHubFileItem> Files => _files;
        public int SelectedFileCount => _files.Count(file => file.IsDependencySelected);
        public int RepositoryFileCount => _files.Length;
        public int ImportedFileCount => _runtimeFiles.Count(file => string.Equals(file.Kind, "sentis", StringComparison.Ordinal));
        public int ManifestOwnerCount => _dependents
            .Select(item => string.IsNullOrWhiteSpace(item.Dependency.ManifestPath)
                ? item.Dependency.PackageId
                : item.Dependency.ManifestPath)
            .Distinct(StringComparer.Ordinal)
            .Count();
        /// <summary>
        /// An optional model that no owner in this project enabled.
        /// </summary>
        public bool IsNotEnabled => State == ModelHubItemState.NotEnabled;

        /// <summary>
        /// Owners that declare this model as optional and did not enable it.
        /// </summary>
        public IReadOnlyList<DependencyStatus> NotEnabledDependents => _dependents.Where(item => item.IsNotEnabled).ToArray();

        /// <summary>
        /// Owners that declare this model as optional and enabled it.
        /// </summary>
        public IReadOnlyList<DependencyStatus> EnabledOptionalDependents => _dependents
            .Where(item => item.Dependency.IsOptionalModel && item.Dependency.IsEnabled)
            .ToArray();

        /// <summary>
        /// Whether any owner declares this model with the model-level <c>optional</c> flag.
        /// </summary>
        public bool IsOptionalModel => _dependents.Any(item => item.Dependency.IsOptionalModel);

        public bool HasRevisionConflict => _dependents.Any(item => item.ReasonCode == DependencyReasonCode.RevisionConflict);
        public bool HasSelectionRequired => _dependents.Any(item => item.ReasonCode == DependencyReasonCode.SelectionRequired);
        public bool HasMissingCacheFiles => _dependents.Any(item => item.ReasonCode == DependencyReasonCode.RawCacheFilesMissing);

        /// <summary>
        /// A whole-repository dependency whose file list is not in the index, for example after another tool downloaded
        /// it. The files may all be there; AI Model Hub needs the list to tell.
        /// </summary>
        public bool HasUnknownFileList => _dependents.Any(item =>
            item.ReasonCode == DependencyReasonCode.RawCacheFilesMissing &&
            item.Dependency.SelectsWholeRepository &&
            (item.RawMetadata?.RepositoryFiles == null || item.RawMetadata.RepositoryFiles.Length == 0));
        public bool HasDamagedCache => _dependents.Any(item => item.HasDamagedCache);

        /// <summary>
        /// Every selected file is cached and only this project's setup is missing or out of date.
        /// </summary>
        public bool IsImportPending => _dependents.Length > 0 &&
            _dependents.Where(item => item.NeedsAttention).All(item => item.IsImportPending) &&
            _dependents.Any(item => item.IsImportPending);

        /// <summary>
        /// The cached files are complete, but ONNX files need a conversion that waits for the user.
        /// </summary>
        public bool NeedsConversion => IsImportPending && _dependents.Any(item => item.NeedsConversion);

        /// <summary>
        /// Selected ONNX files that need a new conversion for this project.
        /// </summary>
        public IReadOnlyList<string> PendingConversions => _dependents
            .SelectMany(item => item.PendingConversions)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        /// <summary>
        /// Why the conversion is needed. A format change wins over a settings change, which wins over a first conversion.
        /// </summary>
        public ConversionCause ConversionCause => _dependents
            .Where(item => item.NeedsConversion)
            .Select(item => item.ConversionCause)
            .DefaultIfEmpty(ConversionCause.None)
            .Max();

        /// <summary>
        /// Selected ONNX files that Sentis could not convert in the last import.
        /// </summary>
        public IReadOnlyList<string> SkippedOnnxRepoPaths => _dependents
            .SelectMany(item => item.Shared?.SkippedRepoPaths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        public bool HasImportedArtifacts => _runtimeFiles.Length > 0;
        public bool NeedsAttention => State != ModelHubItemState.Ready && State != ModelHubItemState.NotEnabled;

        internal void AddDiagnostic(ModelHubDiagnostic diagnostic)
        {
            if (diagnostic == null)
            {
                return;
            }

            if (_diagnostics.Any(existing =>
                    string.Equals(existing.Severity, diagnostic.Severity, StringComparison.Ordinal) &&
                    string.Equals(existing.Message, diagnostic.Message, StringComparison.Ordinal)))
            {
                return;
            }

            _diagnostics = _diagnostics.Concat(new[] { diagnostic }).ToArray();
        }
    }

    /// <summary>
    /// Aggregated editor-facing report used by the Model Hub window.
    /// </summary>
    internal sealed class ModelHubReport
    {
        private readonly ModelHubItem[] _items;
        private readonly ManifestDiscoveryIssue[] _discoveryIssues;

        public ModelHubReport(
            IReadOnlyList<ModelHubItem> items,
            IReadOnlyList<ManifestDiscoveryIssue> discoveryIssues)
        {
            _items = items?.ToArray() ?? Array.Empty<ModelHubItem>();
            _discoveryIssues = discoveryIssues?.ToArray() ?? Array.Empty<ManifestDiscoveryIssue>();
        }

        public IReadOnlyList<ModelHubItem> Items => _items;
        public IReadOnlyList<ManifestDiscoveryIssue> DiscoveryIssues => _discoveryIssues;
        public int ReadyCount => _items.Count(item => item.State == ModelHubItemState.Ready);
        public int ActionRequiredCount => _items.Count(item => item.State == ModelHubItemState.ActionRequired);
        public int ErrorCount => _items.Count(item => item.State == ModelHubItemState.Error);
        public int NotEnabledCount => _items.Count(item => item.State == ModelHubItemState.NotEnabled);
    }
}
