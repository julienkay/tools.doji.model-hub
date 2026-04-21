using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// High-level presentation state used by the Model Hub window.
    /// </summary>
    internal enum HfHubModelHubItemState
    {
        Ready,
        ActionRequired,
        Error
    }

    /// <summary>
    /// Structured diagnostic shown in repository-centric Model Hub details.
    /// </summary>
    internal sealed class HfHubModelHubDiagnostic
    {
        public HfHubModelHubDiagnostic(string severity, string message)
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
    internal sealed class HfHubModelHubItem
    {
        private readonly HfHubDependencyStatus[] _dependents;
        private HfHubModelHubDiagnostic[] _diagnostics;
        private readonly HfHubImportedRuntimeFileMetadata[] _runtimeFiles;

        public HfHubModelHubItem(
            string repository,
            string requestedRevision,
            string resolvedCommitHash,
            string rawSnapshotRootPath,
            string runtimeSnapshotAssetPath,
            long? cacheSizeOnDiskBytes,
            long? estimatedCacheSizeBytes,
            long? runtimeSizeOnDiskBytes,
            long? estimatedRuntimeSizeBytes,
            HfHubModelHubItemState state,
            IReadOnlyList<HfHubDependencyStatus> dependents,
            IReadOnlyList<HfHubModelHubDiagnostic> diagnostics,
            IReadOnlyList<HfHubImportedRuntimeFileMetadata> runtimeFiles)
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
            _dependents = dependents?.ToArray() ?? Array.Empty<HfHubDependencyStatus>();
            _diagnostics = diagnostics?.ToArray() ?? Array.Empty<HfHubModelHubDiagnostic>();
            _runtimeFiles = runtimeFiles?.ToArray() ?? Array.Empty<HfHubImportedRuntimeFileMetadata>();
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
        public HfHubModelHubItemState State { get; }
        public IReadOnlyList<HfHubDependencyStatus> Dependents => _dependents;
        public IReadOnlyList<HfHubModelHubDiagnostic> Diagnostics => _diagnostics;
        public IReadOnlyList<HfHubImportedRuntimeFileMetadata> RuntimeFiles => _runtimeFiles;
        public int ImportedFileCount => _runtimeFiles.Count(file => string.Equals(file.Kind, "sentis", StringComparison.Ordinal));
        public int ManifestOwnerCount => _dependents.Select(item => item.Dependency.PackageId).Distinct(StringComparer.Ordinal).Count();
        public bool HasImportedArtifacts => _runtimeFiles.Length > 0;
        public bool NeedsAttention => State != HfHubModelHubItemState.Ready;

        internal void AddDiagnostic(HfHubModelHubDiagnostic diagnostic)
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
    internal sealed class HfHubModelHubReport
    {
        private readonly HfHubModelHubItem[] _items;
        private readonly HfHubManifestDiscoveryIssue[] _discoveryIssues;

        public HfHubModelHubReport(
            IReadOnlyList<HfHubModelHubItem> items,
            IReadOnlyList<HfHubManifestDiscoveryIssue> discoveryIssues)
        {
            _items = items?.ToArray() ?? Array.Empty<HfHubModelHubItem>();
            _discoveryIssues = discoveryIssues?.ToArray() ?? Array.Empty<HfHubManifestDiscoveryIssue>();
        }

        public IReadOnlyList<HfHubModelHubItem> Items => _items;
        public IReadOnlyList<HfHubManifestDiscoveryIssue> DiscoveryIssues => _discoveryIssues;
        public int ReadyCount => _items.Count(item => item.State == HfHubModelHubItemState.Ready);
        public int ActionRequiredCount => _items.Count(item => item.State == HfHubModelHubItemState.ActionRequired);
        public int ErrorCount => _items.Count(item => item.State == HfHubModelHubItemState.Error);
    }
}
