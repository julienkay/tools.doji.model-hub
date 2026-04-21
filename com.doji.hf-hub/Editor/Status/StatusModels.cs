using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// High-level local state of one declared model dependency.
    /// </summary>
    internal enum DependencyState
    {
        /// <summary>
        /// Generated runtime files and the corresponding local cache/import metadata are current.
        /// </summary>
        Ready,

        /// <summary>
        /// At least one required local artifact is missing, such as the raw cache ref/snapshot,
        /// import metadata, or generated runtime files.
        /// </summary>
        Missing,

        /// <summary>
        /// Local artifacts exist, but no longer match the manifest, raw snapshot, or current Sentis version.
        /// </summary>
        Stale,

        /// <summary>
        /// Status evaluation could not complete because a required file was unreadable or invalid.
        /// </summary>
        Failed
    }

    /// <summary>
    /// Structured explanation for dependency state used by UI and future workflows.
    /// </summary>
    internal enum DependencyReasonCode
    {
        None,
        RawCacheRemovedAfterImport,
        RawCacheRevisionRefMissing,
        RawCacheRevisionRefUnreadable,
        RawSnapshotRootMissing,
        RawCacheMetadataMissingAfterImport,
        RawCacheMetadataInvalid,
        RawCacheMetadataMismatch,
        ImportBindingMissing,
        ImportBindingInvalid,
        ImportBindingStale,
        SharedImportMetadataMissing,
        SharedImportMetadataInvalid,
        SharedImportMetadataStale,
        RuntimeFilesMissing,
        RuntimeFileMissing
    }

    /// <summary>
    /// Captures the currently known local state of one package-declared model dependency.
    /// The <see cref="State"/> enum describes overall integrity, not whether raw cache is present on disk.
    /// </summary>
    internal sealed class DependencyStatus
    {
        /// <summary>
        /// Stores local cache/import state, diagnostic message, and resolved paths for one dependency.
        /// </summary>
        public DependencyStatus(
            DiscoveredModelDependency dependency,
            DependencyState state,
            DependencyReasonCode reasonCode,
            string maintainerMessage,
            string revisionRefPath,
            string rawFilePath,
            string rawMetadataPath,
            RawCacheMetadata rawMetadata,
            string sentisAssetPath,
            string bindingMetadataPath,
            ImportedModelMetadata importedMetadata)
        {
            Dependency = dependency;
            State = state;
            ReasonCode = reasonCode;
            MaintainerMessage = maintainerMessage;
            RevisionRefPath = revisionRefPath;
            RawFilePath = rawFilePath;
            RawMetadataPath = rawMetadataPath;
            RawMetadata = rawMetadata;
            SentisAssetPath = sentisAssetPath;
            BindingMetadataPath = bindingMetadataPath;
            ImportedMetadata = importedMetadata;
        }

        public DiscoveredModelDependency Dependency { get; }
        public DependencyState State { get; }
        public DependencyReasonCode ReasonCode { get; }
        public string MaintainerMessage { get; }
        public string RevisionRefPath { get; }
        public string RawFilePath { get; }
        public string RawMetadataPath { get; }
        public RawCacheMetadata RawMetadata { get; }
        public string SentisAssetPath { get; }
        public string BindingMetadataPath { get; }
        public ImportedModelMetadata ImportedMetadata { get; }
        public bool NeedsAttention => State != DependencyState.Ready;
    }

    /// <summary>
    /// Summarizes local dependency status across all discovered manifests.
    /// </summary>
    internal sealed class StatusReport
    {
        private readonly ManifestDiscoveryIssue[] _discoveryIssues;
        private readonly DependencyStatus[] _dependencyStatuses;

        /// <summary>
        /// Stores discovery issues together with per-dependency local status evaluation.
        /// </summary>
        public StatusReport(
            ManifestDiscoveryIssue[] discoveryIssues,
            DependencyStatus[] dependencyStatuses)
        {
            _discoveryIssues = discoveryIssues ?? Array.Empty<ManifestDiscoveryIssue>();
            _dependencyStatuses = dependencyStatuses ?? Array.Empty<DependencyStatus>();
        }

        public IReadOnlyList<ManifestDiscoveryIssue> DiscoveryIssues => _discoveryIssues;
        public IReadOnlyList<DependencyStatus> DependencyStatuses => _dependencyStatuses;
        public int ReadyCount => _dependencyStatuses.Count(status => status.State == DependencyState.Ready);
        public int MissingCount => _dependencyStatuses.Count(status => status.State == DependencyState.Missing);
        public int StaleCount => _dependencyStatuses.Count(status => status.State == DependencyState.Stale);
        public int FailedCount => _dependencyStatuses.Count(status => status.State == DependencyState.Failed);
        public bool NeedsAttention => DiscoveryIssues.Count > 0 || _dependencyStatuses.Any(status => status.NeedsAttention);
    }
}
