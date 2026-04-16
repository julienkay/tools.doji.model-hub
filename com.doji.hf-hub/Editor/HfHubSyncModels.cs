using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
            string errorMessage)
        {
            Dependency = dependency;
            CachedFile = cachedFile;
            ErrorMessage = errorMessage;
        }

        public HfHubDiscoveredModelDependency Dependency { get; }
        public HfHubCachedRawFile CachedFile { get; }
        public string ErrorMessage { get; }
        public bool Success => CachedFile != null && string.IsNullOrWhiteSpace(ErrorMessage);
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
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Syncs discovered model dependencies into shared raw cache.
    /// </summary>
    internal sealed class HfHubSyncService
    {
        private readonly IHfHubRawCacheService _rawCacheService;

        /// <summary>
        /// Creates sync service with optional injected raw-cache service.
        /// </summary>
        public HfHubSyncService(IHfHubRawCacheService rawCacheService = null)
        {
            _rawCacheService = rawCacheService ?? new HfHubRawCacheService();
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
                    var cachedFile = await _rawCacheService.GetOrDownloadAsync(dependency, cancellationToken);
                    modelResults.Add(new HfHubModelSyncResult(dependency, cachedFile, null));
                }
                catch (Exception exception)
                {
                    modelResults.Add(new HfHubModelSyncResult(dependency, null, exception.Message));
                }
            }

            return new HfHubSyncReport(discoveryReport.Issues, modelResults);
        }
    }
}
