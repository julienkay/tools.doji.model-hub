using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Persists one-time cache-size lookups for repository revisions so Model Hub can show a size before download.
    /// </summary>
    internal interface IRemoteSnapshotSizeEstimateService
    {
        bool TryGetEstimatedCacheSize(string repository, string requestedRevision, out long? estimatedCacheSizeBytes);
        bool TryGetRepositoryInventory(string repository, string requestedRevision, out RepositoryFileMetadata[] repositoryFiles);
        bool TryGetModelCard(string repository, string requestedRevision, out ModelCardMetadata modelCard);
        void EnsureEstimateLookup(DiscoveredModelDependency dependency, Action onCompleted = null);
    }

    /// <summary>
    /// Stores one project-local cache-size lookup result for a repository revision.
    /// </summary>
    [Serializable]
    internal sealed class RemoteSnapshotSizeEstimateMetadata
    {
        public string Repository { get; set; }
        public string RequestedRevision { get; set; }
        public long? EstimatedCacheSizeBytes { get; set; }
        public string ResolvedCommitHash { get; set; }
        public RepositoryFileMetadata[] RepositoryFiles { get; set; } = Array.Empty<RepositoryFileMetadata>();
        public ModelCardMetadata ModelCard { get; set; }
        public string AttemptedAtUtc { get; set; }
    }

    /// <summary>
    /// Resolves repository file sizes through existing Hub HEAD/API flow, then stores the summed estimate under project `Library/`.
    /// </summary>
    internal sealed class RemoteSnapshotSizeEstimateService : IRemoteSnapshotSizeEstimateService
    {
        private static readonly HashSet<string> InFlightEstimateKeys = new(StringComparer.Ordinal);
        private static readonly object InFlightLock = new();

        private readonly IHttpClient _httpClient;

        public RemoteSnapshotSizeEstimateService(IHttpClient httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public bool TryGetEstimatedCacheSize(string repository, string requestedRevision, out long? estimatedCacheSizeBytes)
        {
            estimatedCacheSizeBytes = null;
            if (!TryReadMetadata(repository, requestedRevision, out var metadata))
            {
                return false;
            }

            estimatedCacheSizeBytes = metadata.EstimatedCacheSizeBytes;
            return true;
        }

        public bool TryGetRepositoryInventory(string repository, string requestedRevision, out RepositoryFileMetadata[] repositoryFiles)
        {
            repositoryFiles = Array.Empty<RepositoryFileMetadata>();
            if (!TryReadMetadata(repository, requestedRevision, out var metadata))
            {
                return false;
            }

            repositoryFiles = metadata.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>();
            return repositoryFiles.Length > 0;
        }

        public bool TryGetModelCard(string repository, string requestedRevision, out ModelCardMetadata modelCard)
        {
            modelCard = null;
            if (!TryReadMetadata(repository, requestedRevision, out var metadata))
            {
                return false;
            }

            modelCard = metadata.ModelCard;
            return modelCard != null;
        }

        public void EnsureEstimateLookup(DiscoveredModelDependency dependency, Action onCompleted = null)
        {
            if (dependency == null || string.IsNullOrWhiteSpace(dependency.Repository))
            {
                return;
            }

            var revision = ManifestRevision.NormalizeForResolution(dependency.Revision);
            var metadataPath = ProjectPaths.ToProjectAbsolutePath(
                ProjectPaths.GetCacheSizeEstimateMetadataPath(dependency.Repository, revision));
            if (File.Exists(metadataPath) &&
                TryReadMetadata(dependency.Repository, revision, out var existing) &&
                !NeedsRefresh(existing))
            {
                return;
            }

            var key = BuildEstimateKey(dependency.Repository, revision);
            lock (InFlightLock)
            {
                if (!InFlightEstimateKeys.Add(key))
                {
                    return;
                }
            }

            _ = ResolveAndPersistEstimateAsync(dependency.Repository, revision, metadataPath, key, onCompleted);
        }

        private async Task ResolveAndPersistEstimateAsync(
            string repository,
            string revision,
            string metadataPath,
            string key,
            Action onCompleted)
        {
            try
            {
                var repositoryInventory = await _httpClient.GetRepositoryInventoryAsync(
                    repository,
                    revision,
                    CancellationToken.None);
                var metadata = new RemoteSnapshotSizeEstimateMetadata
                {
                    Repository = repository,
                    RequestedRevision = revision,
                    ResolvedCommitHash = repositoryInventory.ResolvedCommitHash,
                    RepositoryFiles = repositoryInventory.Files.ToArray(),
                    ModelCard = repositoryInventory.ModelCard ?? new ModelCardMetadata(),
                    EstimatedCacheSizeBytes = SumFileSizes(repositoryInventory.Files),
                    AttemptedAtUtc = DateTime.UtcNow.ToString("O")
                };
                WriteMetadata(metadataPath, metadata);
            }
            catch
            {
                // Persist failed attempts too so opening the same model does not keep retrying forever.
                WriteMetadata(
                    metadataPath,
                    new RemoteSnapshotSizeEstimateMetadata
                    {
                        Repository = repository,
                        RequestedRevision = revision,
                        AttemptedAtUtc = DateTime.UtcNow.ToString("O")
                    });
            }
            finally
            {
                lock (InFlightLock)
                {
                    InFlightEstimateKeys.Remove(key);
                }

                if (onCompleted != null)
                {
                    EditorApplication.delayCall += () => onCompleted();
                }
            }
        }

        private static long? SumFileSizes(IReadOnlyList<RepositoryFileMetadata> repositoryFiles)
        {
            var knownSizes = repositoryFiles?
                .Select(file => file.SizeInBytes)
                .Where(size => size.HasValue)
                .Select(size => size.Value)
                .ToArray();
            if (knownSizes == null || knownSizes.Length == 0)
            {
                return null;
            }

            return knownSizes.Sum();
        }

        private static string BuildEstimateKey(string repository, string requestedRevision)
        {
            return $"{repository}\n{requestedRevision}";
        }

        /// <summary>
        /// Refetches estimates without a file list or model card (older format) and failed attempts older than one day.
        /// </summary>
        private static bool NeedsRefresh(RemoteSnapshotSizeEstimateMetadata metadata)
        {
            if (metadata.RepositoryFiles?.Length > 0)
            {
                return metadata.ModelCard == null;
            }

            if (!string.IsNullOrWhiteSpace(metadata.ResolvedCommitHash))
            {
                return true;
            }

            return !DateTime.TryParse(metadata.AttemptedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var attemptedAt) ||
                DateTime.UtcNow - attemptedAt.ToUniversalTime() > TimeSpan.FromDays(1);
        }

        private static bool TryReadMetadata(string repository, string requestedRevision, out RemoteSnapshotSizeEstimateMetadata metadata)
        {
            var metadataPath = ProjectPaths.ToProjectAbsolutePath(
                ProjectPaths.GetCacheSizeEstimateMetadataPath(repository, ManifestRevision.NormalizeForResolution(requestedRevision)));
            metadata = null;
            if (!File.Exists(metadataPath))
            {
                return false;
            }

            try
            {
                metadata = JsonConvert.DeserializeObject<RemoteSnapshotSizeEstimateMetadata>(File.ReadAllText(metadataPath));
                return metadata != null;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteMetadata(string metadataPath, RemoteSnapshotSizeEstimateMetadata metadata)
        {
            var metadataDirectory = Path.GetDirectoryName(metadataPath);
            if (!string.IsNullOrWhiteSpace(metadataDirectory))
            {
                Directory.CreateDirectory(metadataDirectory);
            }

            var temporaryPath = metadataPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(metadata, Formatting.Indented));

            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
            }

            File.Move(temporaryPath, metadataPath);
        }
    }
}
