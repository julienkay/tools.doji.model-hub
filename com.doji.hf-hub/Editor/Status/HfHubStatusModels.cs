using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// High-level local state of one declared model dependency.
    /// </summary>
    internal enum HfHubDependencyState
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
    internal enum HfHubDependencyReasonCode
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
    internal sealed class HfHubDependencyStatus
    {
        /// <summary>
        /// Stores local cache/import state, diagnostic message, and resolved paths for one dependency.
        /// </summary>
        public HfHubDependencyStatus(
            HfHubDiscoveredModelDependency dependency,
            HfHubDependencyState state,
            HfHubDependencyReasonCode reasonCode,
            string maintainerMessage,
            string revisionRefPath,
            string rawFilePath,
            string rawMetadataPath,
            HfHubRawCacheMetadata rawMetadata,
            string sentisAssetPath,
            string bindingMetadataPath,
            HfHubImportedModelMetadata importedMetadata)
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

        public HfHubDiscoveredModelDependency Dependency { get; }
        public HfHubDependencyState State { get; }
        public HfHubDependencyReasonCode ReasonCode { get; }
        public string MaintainerMessage { get; }
        public string RevisionRefPath { get; }
        public string RawFilePath { get; }
        public string RawMetadataPath { get; }
        public HfHubRawCacheMetadata RawMetadata { get; }
        public string SentisAssetPath { get; }
        public string BindingMetadataPath { get; }
        public HfHubImportedModelMetadata ImportedMetadata { get; }
        public bool NeedsAttention => State != HfHubDependencyState.Ready;
    }

    /// <summary>
    /// Summarizes local dependency status across all discovered manifests.
    /// </summary>
    internal sealed class HfHubStatusReport
    {
        private readonly HfHubManifestDiscoveryIssue[] _discoveryIssues;
        private readonly HfHubDependencyStatus[] _dependencyStatuses;

        /// <summary>
        /// Stores discovery issues together with per-dependency local status evaluation.
        /// </summary>
        public HfHubStatusReport(
            HfHubManifestDiscoveryIssue[] discoveryIssues,
            HfHubDependencyStatus[] dependencyStatuses)
        {
            _discoveryIssues = discoveryIssues ?? Array.Empty<HfHubManifestDiscoveryIssue>();
            _dependencyStatuses = dependencyStatuses ?? Array.Empty<HfHubDependencyStatus>();
        }

        public IReadOnlyList<HfHubManifestDiscoveryIssue> DiscoveryIssues => _discoveryIssues;
        public IReadOnlyList<HfHubDependencyStatus> DependencyStatuses => _dependencyStatuses;
        public int ReadyCount => _dependencyStatuses.Count(status => status.State == HfHubDependencyState.Ready);
        public int MissingCount => _dependencyStatuses.Count(status => status.State == HfHubDependencyState.Missing);
        public int StaleCount => _dependencyStatuses.Count(status => status.State == HfHubDependencyState.Stale);
        public int FailedCount => _dependencyStatuses.Count(status => status.State == HfHubDependencyState.Failed);
        public bool NeedsAttention => DiscoveryIssues.Count > 0 || _dependencyStatuses.Any(status => status.NeedsAttention);
    }
}
