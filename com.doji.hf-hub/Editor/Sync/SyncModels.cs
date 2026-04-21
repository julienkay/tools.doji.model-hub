using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Result for one dependency sync attempt.
    /// </summary>
    internal sealed class ModelSyncResult
    {
        /// <summary>
        /// Stores outcome for one model dependency sync.
        /// </summary>
        public ModelSyncResult(
            DiscoveredModelDependency dependency,
            CachedSnapshot cachedFile,
            ImportedModelArtifact importedArtifact,
            string errorMessage)
        {
            Dependency = dependency;
            CachedFile = cachedFile;
            ImportedArtifact = importedArtifact;
            ErrorMessage = errorMessage;
        }

        public DiscoveredModelDependency Dependency { get; }
        public CachedSnapshot CachedFile { get; }
        public ImportedModelArtifact ImportedArtifact { get; }
        public string ErrorMessage { get; }
        public bool Success => ImportedArtifact != null && string.IsNullOrWhiteSpace(ErrorMessage);
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
            var modelResults = new List<ModelSyncResult>();
            foreach (var dependency in discoveryReport.Dependencies)
            {
                try
                {
                    var cachedFile = await _rawCacheService.GetOrDownloadAsync(dependency, null, cancellationToken);
                    var importedArtifact = await _projectImportService.ImportAsync(dependency, cachedFile, cancellationToken);
                    modelResults.Add(new ModelSyncResult(dependency, cachedFile, importedArtifact, null));
                }
                catch (Exception exception)
                {
                    modelResults.Add(new ModelSyncResult(dependency, null, null, exception.Message));
                }
            }

            _runtimeLocatorRegistryService.WriteRegistries(modelResults);

            return new SyncReport(discoveryReport.Issues, modelResults);
        }
    }
}
