using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;

namespace Doji.AI.ModelHub.Editor
{
    internal enum SyncOperationMode
    {
        Full,
        DownloadOnly,
        MaterializeOnly
    }

    /// <summary>
    /// Result for one dependency sync attempt.
    /// </summary>
    internal sealed class ModelSyncResult
    {
        /// <summary>
        /// Stores outcome for one model dependency sync.
        /// </summary>
        public ModelSyncResult(
            SyncOperationMode mode,
            DiscoveredModelDependency dependency,
            CachedSnapshot cachedFile,
            ImportedModelArtifact importedArtifact,
            string errorMessage)
        {
            Mode = mode;
            Dependency = dependency;
            CachedFile = cachedFile;
            ImportedArtifact = importedArtifact;
            ErrorMessage = errorMessage;
        }

        public SyncOperationMode Mode { get; }
        public DiscoveredModelDependency Dependency { get; }
        public CachedSnapshot CachedFile { get; }
        public ImportedModelArtifact ImportedArtifact { get; }
        public string ErrorMessage { get; }
        public bool Success => string.IsNullOrWhiteSpace(ErrorMessage) && (ImportedArtifact != null || CachedFile != null);

        /// <summary>
        /// Creates failed sync result for repository revision conflict that must block import work.
        /// </summary>
        internal static ModelSyncResult CreateRevisionConflictFailure(
            DiscoveredModelDependency dependency,
            RepositoryRevisionConflict revisionConflict)
        {
            return new ModelSyncResult(
                SyncOperationMode.Full,
                dependency,
                null,
                null,
                revisionConflict?.BuildMaintainerMessage() ?? "Repository revision conflict blocked sync.");
        }
    }

    /// <summary>
    /// Aggregated sync result for one sync run.
    /// </summary>
    internal sealed class SyncReport
    {
        private readonly ManifestDiscoveryIssue[] _discoveryIssues;
        private readonly ModelSyncResult[] _modelResults;

        /// <summary>
        /// Stores discovery issues and per-model sync results.
        /// </summary>
        public SyncReport(
            IReadOnlyList<ManifestDiscoveryIssue> discoveryIssues,
            IReadOnlyList<ModelSyncResult> modelResults)
        {
            _discoveryIssues = discoveryIssues?.ToArray() ?? Array.Empty<ManifestDiscoveryIssue>();
            _modelResults = modelResults?.ToArray() ?? Array.Empty<ModelSyncResult>();
        }

        public IReadOnlyList<ManifestDiscoveryIssue> DiscoveryIssues => _discoveryIssues;
        public IReadOnlyList<ModelSyncResult> ModelResults => _modelResults;
        public int SuccessCount => _modelResults.Count(result => result.Success);
        public int FailureCount => _modelResults.Count(result => !result.Success);
    }

    /// <summary>
    /// Contract for syncing raw Hub dependencies into shared cache.
    /// </summary>
    internal interface IRawCacheService
    {
        Task<CachedSnapshot> GetOrDownloadAsync(
            DiscoveredModelDependency dependency,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken);

        CachedSnapshot GetCachedOrThrow(DiscoveredModelDependency dependency);
    }

    /// <summary>
    /// Contract for importing cached raw files into project-owned `.sentis` artifacts.
    /// </summary>
    internal interface IProjectImportService
    {
        Task<ImportedModelArtifact> ImportAsync(
            DiscoveredModelDependency dependency,
            CachedSnapshot cachedSnapshot,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Contract for rebuilding package runtime locator registries from successful imports.
    /// </summary>
    internal interface IRuntimeLocatorRegistryService
    {
        void WriteRegistries(IReadOnlyList<ModelSyncResult> modelResults);
    }

    /// <summary>
    /// Syncs discovered model dependencies into shared cache and project-owned `.sentis` artifacts.
    /// </summary>
    internal sealed class SyncService
    {
        private readonly IRawCacheService _rawCacheService;
        private readonly IProjectImportService _projectImportService;
        private readonly IRuntimeLocatorRegistryService _runtimeLocatorRegistryService;

        /// <summary>
        /// Creates sync service with optional injected raw-cache and import services.
        /// </summary>
        public SyncService(
            IRawCacheService rawCacheService = null,
            IProjectImportService projectImportService = null,
            IRuntimeLocatorRegistryService runtimeLocatorRegistryService = null)
        {
            _rawCacheService = rawCacheService ?? new RawCacheService();
            _projectImportService = projectImportService ?? new ProjectImportService();
            _runtimeLocatorRegistryService = runtimeLocatorRegistryService ?? new RuntimeLocatorRegistryService();
        }

        /// <summary>
        /// Syncs all discovered dependencies into shared cache.
        /// </summary>
        public async Task<SyncReport> SyncAsync(
            DiscoveryReport discoveryReport,
            CancellationToken cancellationToken)
        {
            return await SyncAsync(discoveryReport, SyncOperationMode.Full, cancellationToken);
        }

        public async Task<SyncReport> SyncAsync(
            DiscoveryReport discoveryReport,
            SyncOperationMode mode,
            CancellationToken cancellationToken)
        {
            var modelResults = new List<ModelSyncResult>();
            var revisionConflicts = RevisionConflictDetector.Detect(discoveryReport?.Dependencies);
            var statusByDependencyKey = new StatusService().BuildStatusReport(discoveryReport).DependencyStatuses
                .ToDictionary(
                    status => $"{status.Dependency.PackageId}:{status.Dependency.ModelId}",
                    status => status,
                    StringComparer.Ordinal);
            foreach (var dependency in discoveryReport.Dependencies)
            {
                if (statusByDependencyKey.TryGetValue($"{dependency.PackageId}:{dependency.ModelId}", out var dependencyStatus) &&
                    dependencyStatus.ReasonCode == DependencyReasonCode.SelectionRequired)
                {
                    continue;
                }

                if (revisionConflicts.TryGetValue(dependency.Repository, out var revisionConflict))
                {
                    modelResults.Add(ModelSyncResult.CreateRevisionConflictFailure(dependency, revisionConflict));
                    continue;
                }

                try
                {
                    CachedSnapshot cachedFile = null;
                    ImportedModelArtifact importedArtifact = null;

                    switch (mode)
                    {
                        case SyncOperationMode.DownloadOnly:
                            cachedFile = await _rawCacheService.GetOrDownloadAsync(dependency, null, cancellationToken);
                            break;
                        case SyncOperationMode.MaterializeOnly:
                            cachedFile = _rawCacheService.GetCachedOrThrow(dependency);
                            importedArtifact = await _projectImportService.ImportAsync(dependency, cachedFile, cancellationToken);
                            break;
                        default:
                            cachedFile = await _rawCacheService.GetOrDownloadAsync(dependency, null, cancellationToken);
                            importedArtifact = await _projectImportService.ImportAsync(dependency, cachedFile, cancellationToken);
                            break;
                    }

                    modelResults.Add(new ModelSyncResult(mode, dependency, cachedFile, importedArtifact, null));
                }
                catch (Exception exception)
                {
                    modelResults.Add(new ModelSyncResult(mode, dependency, null, null, exception.Message));
                }
            }

            _runtimeLocatorRegistryService.WriteRegistries(modelResults);

            return new SyncReport(discoveryReport.Issues, modelResults);
        }
    }
}
