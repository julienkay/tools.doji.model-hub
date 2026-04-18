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
    internal sealed class HfHubModelSyncResult
    {
        /// <summary>
        /// Stores outcome for one model dependency sync.
        /// </summary>
        public HfHubModelSyncResult(
            HfHubDiscoveredModelDependency dependency,
            HfHubCachedRawFile cachedFile,
            HfHubImportedModelArtifact importedArtifact,
            string errorMessage)
        {
            Dependency = dependency;
            CachedFile = cachedFile;
            ImportedArtifact = importedArtifact;
            ErrorMessage = errorMessage;
        }

        public HfHubDiscoveredModelDependency Dependency { get; }
        public HfHubCachedRawFile CachedFile { get; }
        public HfHubImportedModelArtifact ImportedArtifact { get; }
        public string ErrorMessage { get; }
        public bool Success => ImportedArtifact != null && string.IsNullOrWhiteSpace(ErrorMessage);
    }

    /// <summary>
    /// Aggregated sync result for one sync run.
    /// </summary>
    internal sealed class HfHubSyncReport
    {
        private readonly HfHubManifestDiscoveryIssue[] _discoveryIssues;
        private readonly HfHubModelSyncResult[] _modelResults;

        /// <summary>
        /// Stores discovery issues and per-model sync results.
        /// </summary>
        public HfHubSyncReport(
            IReadOnlyList<HfHubManifestDiscoveryIssue> discoveryIssues,
            IReadOnlyList<HfHubModelSyncResult> modelResults)
        {
            _discoveryIssues = discoveryIssues?.ToArray() ?? Array.Empty<HfHubManifestDiscoveryIssue>();
            _modelResults = modelResults?.ToArray() ?? Array.Empty<HfHubModelSyncResult>();
        }

        public IReadOnlyList<HfHubManifestDiscoveryIssue> DiscoveryIssues => _discoveryIssues;
        public IReadOnlyList<HfHubModelSyncResult> ModelResults => _modelResults;
        public int SuccessCount => _modelResults.Count(result => result.Success);
        public int FailureCount => _modelResults.Count(result => !result.Success);
    }

    /// <summary>
    /// Contract for syncing raw Hub dependencies into shared cache.
    /// </summary>
    internal interface IHfHubRawCacheService
    {
        Task<HfHubCachedRawFile> GetOrDownloadAsync(
            HfHubDiscoveredModelDependency dependency,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Contract for importing cached raw files into project-owned `.sentis` artifacts.
    /// </summary>
    internal interface IHfHubProjectImportService
    {
        Task<HfHubImportedModelArtifact> ImportAsync(
            HfHubDiscoveredModelDependency dependency,
            HfHubCachedRawFile cachedRawFile,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Contract for rebuilding package runtime locator registries from successful imports.
    /// </summary>
    internal interface IHfHubRuntimeLocatorRegistryService
    {
        void WriteRegistries(IReadOnlyList<HfHubModelSyncResult> modelResults);
    }

    /// <summary>
    /// Syncs discovered model dependencies into shared cache and project-owned `.sentis` artifacts.
    /// </summary>
    internal sealed class HfHubSyncService
    {
        private readonly IHfHubRawCacheService _rawCacheService;
        private readonly IHfHubProjectImportService _projectImportService;
        private readonly IHfHubRuntimeLocatorRegistryService _runtimeLocatorRegistryService;

        /// <summary>
        /// Creates sync service with optional injected raw-cache and import services.
        /// </summary>
        public HfHubSyncService(
            IHfHubRawCacheService rawCacheService = null,
            IHfHubProjectImportService projectImportService = null,
            IHfHubRuntimeLocatorRegistryService runtimeLocatorRegistryService = null)
        {
            _rawCacheService = rawCacheService ?? new HfHubRawCacheService();
            _projectImportService = projectImportService ?? new HfHubProjectImportService();
            _runtimeLocatorRegistryService = runtimeLocatorRegistryService ?? new HfHubRuntimeLocatorRegistryService();
        }

        /// <summary>
        /// Syncs all discovered dependencies into shared cache.
        /// </summary>
        public async Task<HfHubSyncReport> SyncAsync(
            HfHubDiscoveryReport discoveryReport,
            CancellationToken cancellationToken)
        {
            var modelResults = new List<HfHubModelSyncResult>();
            foreach (var dependency in discoveryReport.Dependencies)
            {
                try
                {
                    var cachedFile = await _rawCacheService.GetOrDownloadAsync(dependency, null, cancellationToken);
                    var importedArtifact = await _projectImportService.ImportAsync(dependency, cachedFile, cancellationToken);
                    modelResults.Add(new HfHubModelSyncResult(dependency, cachedFile, importedArtifact, null));
                }
                catch (Exception exception)
                {
                    modelResults.Add(new HfHubModelSyncResult(dependency, null, null, exception.Message));
                }
            }

            _runtimeLocatorRegistryService.WriteRegistries(modelResults);

            return new HfHubSyncReport(discoveryReport.Issues, modelResults);
        }
    }
}
