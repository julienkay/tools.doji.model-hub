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
        InvalidRepository,
        RawCacheRemovedAfterImport,
        RawCacheRevisionRefMissing,
        RawCacheRevisionRefUnreadable,
        RawSnapshotRootMissing,
        RawCacheFilesMissing,
        RevisionConflict,
        SelectionRequired,
        InvalidSelection,
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
    /// Why ONNX files of a downloaded model must be converted. Higher values explain more, so a model with several
    /// owners shows the highest.
    /// </summary>
    internal enum ConversionCause
    {
        None,

        /// <summary>
        /// No converted model exists yet for these files and settings.
        /// </summary>
        FirstConversion,

        /// <summary>
        /// The project's conversion settings changed since the last import.
        /// </summary>
        SettingsChanged,

        /// <summary>
        /// The installed Inference Engine uses a new model format generation.
        /// </summary>
        FormatChanged
    }

    /// <summary>
    /// Captures the currently known local state of one package-declared model dependency.
    /// The <see cref="State"/> enum describes overall integrity, not whether raw cache is present on disk.
    /// </summary>
    internal sealed class DependencyStatus
    {
        /// <summary>
        /// Reason codes that mean the cache on disk is damaged. The fix is to download again.
        /// AI Model Hub's own index is never damage: it is a hint that the next sync rebuilds.
        /// </summary>
        internal static readonly HashSet<DependencyReasonCode> DamagedCacheCodes = new()
        {
            DependencyReasonCode.RawCacheRevisionRefUnreadable,
        };

        /// <summary>
        /// Reason codes that mean every selected file is in the cache and only this project's import is missing or out
        /// of date. The import is cheap unless <see cref="PendingConversions"/> lists ONNX files.
        /// </summary>
        internal static readonly HashSet<DependencyReasonCode> ImportPendingCodes = new()
        {
            DependencyReasonCode.ImportBindingMissing,
            DependencyReasonCode.ImportBindingInvalid,
            DependencyReasonCode.ImportBindingStale,
            DependencyReasonCode.SharedImportMetadataMissing,
            DependencyReasonCode.SharedImportMetadataInvalid,
            DependencyReasonCode.SharedImportMetadataStale,
            DependencyReasonCode.RuntimeFilesMissing,
            DependencyReasonCode.RuntimeFileMissing,
        };

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
            ImportedModelMetadata importedMetadata,
            IReadOnlyList<string> pendingConversions = null,
            ConversionCause conversionCause = ConversionCause.None)
        {
            PendingConversions = pendingConversions?.ToArray() ?? Array.Empty<string>();
            ConversionCause = PendingConversions.Count > 0 ? conversionCause : ConversionCause.None;
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

        /// <summary>
        /// Selected ONNX files the import would convert, because the converted store has no model for them with this
        /// project's settings. Empty unless <see cref="IsImportPending"/>.
        /// </summary>
        public IReadOnlyList<string> PendingConversions { get; }
        public ConversionCause ConversionCause { get; }

        /// <summary>
        /// Every selected file is cached; only this project's import is missing or out of date.
        /// </summary>
        public bool IsImportPending => ImportPendingCodes.Contains(ReasonCode);

        /// <summary>
        /// The import must convert ONNX files, which takes CPU, memory, and time, so it waits for the user.
        /// </summary>
        public bool NeedsConversion => IsImportPending && PendingConversions.Count > 0;

        /// <summary>
        /// The import only writes records and reuses runtime models, so AI Model Hub runs it without asking.
        /// </summary>
        public bool CanImportAutomatically => IsImportPending && PendingConversions.Count == 0;

        /// <summary>
        /// The setup prompt asks only for work that costs something: downloads and new conversions.
        /// </summary>
        public bool NeedsStartupPrompt => NeedsAttention &&
            ReasonCode != DependencyReasonCode.SelectionRequired &&
            !CanImportAutomatically;

        public bool HasDamagedCache => DamagedCacheCodes.Contains(ReasonCode);
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
        public bool NeedsStartupPrompt => DiscoveryIssues.Count > 0 || _dependencyStatuses.Any(status => status.NeedsStartupPrompt);
    }

    /// <summary>
    /// Describes one unsupported same-repository multi-revision declaration set within one Unity project.
    /// </summary>
    internal sealed class RepositoryRevisionConflict
    {
        private readonly string[] _requestedRevisions;
        private readonly DiscoveredModelDependency[] _dependencies;

        public RepositoryRevisionConflict(
            string repository,
            IReadOnlyList<string> requestedRevisions,
            IReadOnlyList<DiscoveredModelDependency> dependencies)
        {
            Repository = repository;
            _requestedRevisions = requestedRevisions?.ToArray() ?? Array.Empty<string>();
            _dependencies = dependencies?.ToArray() ?? Array.Empty<DiscoveredModelDependency>();
        }

        public string Repository { get; }
        public IReadOnlyList<string> RequestedRevisions => _requestedRevisions;
        public IReadOnlyList<DiscoveredModelDependency> Dependencies => _dependencies;

        public string BuildMaintainerMessage()
        {
            return
                $"Repository '{Repository}' is declared at multiple requested revisions ({string.Join(", ", RequestedRevisions)}). AI Model Hub supports only one revision per repository per project. Resolve manifests to one revision before importing or removing project artifacts.";
        }
    }

    /// <summary>
    /// Finds repositories that are declared at more than one requested revision in one discovery pass.
    /// </summary>
    internal static class RevisionConflictDetector
    {
        public static IReadOnlyDictionary<string, RepositoryRevisionConflict> Detect(
            IReadOnlyList<DiscoveredModelDependency> dependencies)
        {
            var conflicts = new Dictionary<string, RepositoryRevisionConflict>(StringComparer.Ordinal);
            foreach (var repositoryGroup in (dependencies ?? Array.Empty<DiscoveredModelDependency>())
                         .Where(dependency => dependency != null)
                         .GroupBy(dependency => dependency.Repository, StringComparer.Ordinal))
            {
                var requestedRevisions = repositoryGroup
                    .Select(dependency => dependency.Revision)
                    .Where(revision => !string.IsNullOrWhiteSpace(revision))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(revision => revision, StringComparer.Ordinal)
                    .ToArray();
                if (requestedRevisions.Length <= 1)
                {
                    continue;
                }

                conflicts[repositoryGroup.Key] = new RepositoryRevisionConflict(
                    repositoryGroup.Key,
                    requestedRevisions,
                    repositoryGroup
                        .OrderBy(dependency => dependency.PackageId, StringComparer.Ordinal)
                        .ThenBy(dependency => dependency.ModelId, StringComparer.Ordinal)
                        .ToArray());
            }

            return conflicts;
        }
    }
}
