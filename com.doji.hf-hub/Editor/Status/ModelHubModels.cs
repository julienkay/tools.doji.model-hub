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
        Error
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
    /// Represents one repository-centric item in the Model Hub inventory view.
    /// </summary>
    internal sealed class ModelHubItem
    {
        private readonly DependencyStatus[] _dependents;
        private ModelHubDiagnostic[] _diagnostics;
        private readonly ImportedRuntimeFileMetadata[] _runtimeFiles;

        public ModelHubItem(
            string repository,
            string requestedRevision,
            string resolvedCommitHash,
            string rawSnapshotRootPath,
            string runtimeSnapshotAssetPath,
            long? cacheSizeOnDiskBytes,
            long? estimatedCacheSizeBytes,
            long? runtimeSizeOnDiskBytes,
            long? estimatedRuntimeSizeBytes,
            ModelHubItemState state,
            IReadOnlyList<DependencyStatus> dependents,
            IReadOnlyList<ModelHubDiagnostic> diagnostics,
            IReadOnlyList<ImportedRuntimeFileMetadata> runtimeFiles)
        {
            Repository = repository;
            RequestedRevision = requestedRevision;
            ResolvedCommitHash = resolvedCommitHash;
            RawSnapshotRootPath = rawSnapshotRootPath;
            RuntimeSnapshotAssetPath = runtimeSnapshotAssetPath;
            CacheSizeOnDiskBytes = cacheSizeOnDiskBytes;
            EstimatedCacheSizeBytes = estimatedCacheSizeBytes;
            RuntimeSizeOnDiskBytes = runtimeSizeOnDiskBytes;
            EstimatedRuntimeSizeBytes = estimatedRuntimeSizeBytes;
            State = state;
            _dependents = dependents?.ToArray() ?? Array.Empty<DependencyStatus>();
            _diagnostics = diagnostics?.ToArray() ?? Array.Empty<ModelHubDiagnostic>();
            _runtimeFiles = runtimeFiles?.ToArray() ?? Array.Empty<ImportedRuntimeFileMetadata>();
        }

        public string Repository { get; }
        public string RequestedRevision { get; }
        public string ResolvedCommitHash { get; }
        public string RawSnapshotRootPath { get; }
        public string RuntimeSnapshotAssetPath { get; }
        public long? CacheSizeOnDiskBytes { get; }
        public long? EstimatedCacheSizeBytes { get; }
        public long? RuntimeSizeOnDiskBytes { get; }
        public long? EstimatedRuntimeSizeBytes { get; }
        public ModelHubItemState State { get; }
        public IReadOnlyList<DependencyStatus> Dependents => _dependents;
        public IReadOnlyList<ModelHubDiagnostic> Diagnostics => _diagnostics;
        public IReadOnlyList<ImportedRuntimeFileMetadata> RuntimeFiles => _runtimeFiles;
        public int ImportedFileCount => _runtimeFiles.Count(file => string.Equals(file.Kind, "sentis", StringComparison.Ordinal));
        public int ManifestOwnerCount => _dependents.Select(item => item.Dependency.PackageId).Distinct(StringComparer.Ordinal).Count();
        public bool HasRevisionConflict => _dependents.Any(item => item.ReasonCode == DependencyReasonCode.RevisionConflict);
        public bool HasImportedArtifacts => _runtimeFiles.Length > 0;
        public bool NeedsAttention => State != ModelHubItemState.Ready;

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
    }
}
