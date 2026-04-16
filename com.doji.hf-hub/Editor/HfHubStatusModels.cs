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
        Ready,
        Missing,
        Stale,
        Failed
    }

    /// <summary>
    /// Captures the currently known local state of one package-declared model dependency.
    /// </summary>
    internal sealed class HfHubDependencyStatus
    {
        /// <summary>
        /// Stores local cache/import state, diagnostic message, and resolved paths for one dependency.
        /// </summary>
        public HfHubDependencyStatus(
            HfHubDiscoveredModelDependency dependency,
            HfHubDependencyState state,
            string message,
            string revisionRefPath,
            string rawFilePath,
            string rawMetadataPath,
            HfHubRawCacheMetadata rawMetadata,
            string sentisAssetPath,
            string importMetadataPath,
            HfHubImportedModelMetadata importedMetadata)
        {
            Dependency = dependency;
            State = state;
            Message = message;
            RevisionRefPath = revisionRefPath;
            RawFilePath = rawFilePath;
            RawMetadataPath = rawMetadataPath;
            RawMetadata = rawMetadata;
            SentisAssetPath = sentisAssetPath;
            ImportMetadataPath = importMetadataPath;
            ImportedMetadata = importedMetadata;
        }

        public HfHubDiscoveredModelDependency Dependency { get; }
        public HfHubDependencyState State { get; }
        public string Message { get; }
        public string RevisionRefPath { get; }
        public string RawFilePath { get; }
        public string RawMetadataPath { get; }
        public HfHubRawCacheMetadata RawMetadata { get; }
        public string SentisAssetPath { get; }
        public string ImportMetadataPath { get; }
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
