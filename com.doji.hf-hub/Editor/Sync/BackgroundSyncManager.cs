using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Doji.Downloads;
using Doji.Downloads.Editor;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Runs sync in the background using Unity's Progress API and resumes pending work after domain reload.
    /// </summary>
    [InitializeOnLoad]
    internal static class BackgroundSyncManager
    {
        private const string StateDirectoryName = "Doji/HfHub";
        private const string StateFileName = "sync-state.json";
        private static readonly EditorPersistentOperationRunner Runner = new(
            "HF Hub Sync",
            "Syncing model dependencies",
            GetStatePath());

        public static event Action SyncStateChanged;

        static BackgroundSyncManager()
        {
            EditorApplication.delayCall += ResumeIfNeeded;
        }

        public static bool IsRunning => Runner.IsRunning;

        /// <summary>
        /// Starts a new background sync or resumes the persisted one if a previous run was interrupted.
        /// This is the single entry point used by menus, prompts, and Project Settings.
        /// </summary>
        public static void StartOrResume()
        {
            StartOrResume(_ => true);
        }

        /// <summary>
        /// Starts a new background sync for dependencies that match one predicate.
        /// </summary>
        public static void StartOrResume(Func<DiscoveredModelDependency, bool> dependencyFilter)
        {
            if (Runner.IsRunning)
            {
                return;
            }

            var discoveryReport = EditorManifestDiscovery.DiscoverInstalledManifests();
            var pendingKeys = discoveryReport.Dependencies
                .Where(dependencyFilter ?? (_ => true))
                .Select(GetDependencyKey)
                .ToArray();
            if (pendingKeys.Length == 0)
            {
                return;
            }

            NotifySyncStateChanged();
            Runner.StartOrResume(
                () => PersistentItemQueueState.Create(pendingKeys),
                RunAsync,
                exception => UnityEngine.Debug.LogError($"HF Hub background sync failed: {exception.Message}"));
        }

        /// <summary>
        /// Starts background sync for one repository/revision item shown in Model Hub.
        /// </summary>
        public static void StartForRepository(string repository, string revision)
        {
            StartOrResume(dependency => MatchesRepository(dependency, repository, revision));
        }

        /// <summary>
        /// Re-enters pending sync work after domain reload if a persisted sync-state file exists.
        /// </summary>
        private static void ResumeIfNeeded()
        {
            if (Runner.HasPersistedState())
            {
                StartOrResume();
            }
        }

        /// <summary>
        /// Executes sync as a background editor task.
        /// It persists pending dependency keys before each step so a domain reload can resume from local state.
        /// </summary>
        private static async Task<bool> RunAsync(PersistentItemQueueState state, EditorPersistentOperationContext context)
        {
            var discoveryReport = EditorManifestDiscovery.DiscoverInstalledManifests();
            var dependencies = discoveryReport.Dependencies
                .Where(dependency => state.PendingItemKeys.Contains(GetDependencyKey(dependency)))
                .OrderBy(dependency => dependency.PackageId, StringComparer.Ordinal)
                .ThenBy(dependency => dependency.ModelId, StringComparer.Ordinal)
                .ToArray();
            var revisionConflicts = RevisionConflictDetector.Detect(discoveryReport.Dependencies);

            var rawCacheService = new RawCacheService();
            var importService = new ProjectImportService();
            var results = new List<ModelSyncResult>();
            DiscoveredModelDependency activeDependency = null;

            try
            {
                await EditorPersistentItemQueueExecutor.ExecuteAsync(
                    state,
                    dependencies,
                    GetDependencyKey,
                    Runner.SaveState,
                    async (dependency, index, totalCount, operationContext) =>
                    {
                        activeDependency = dependency;

                        if (revisionConflicts.TryGetValue(dependency.Repository, out var revisionConflict))
                        {
                            results.Add(ModelSyncResult.CreateRevisionConflictFailure(dependency, revisionConflict));
                            operationContext.ReportProgress(
                                totalCount == 0 ? 1f : (index + 1f) / totalCount,
                                $"Blocked {dependency.PackageId}/{dependency.ModelId}");
                            return;
                        }

                        var baseProgress = totalCount == 0 ? 1f : index / (float)totalCount;
                        operationContext.ReportProgress(baseProgress, $"Preparing {dependency.PackageId}/{dependency.ModelId}");

                        var downloadProgress = new Progress<DownloadProgress>(progress =>
                        {
                            var downloadRatio = progress.TotalBytes.HasValue && progress.TotalBytes.Value > 0
                                ? progress.DownloadedBytes / (float)progress.TotalBytes.Value
                                : 0f;
                            var overallProgress = totalCount == 0
                                ? downloadRatio
                                : (index + downloadRatio) / totalCount;
                            operationContext.ReportProgress(overallProgress, BuildProgressDescription(dependency, progress));
                        });

                        var cachedFile = await rawCacheService.GetOrDownloadAsync(
                            dependency,
                            downloadProgress,
                            operationContext.CancellationToken);
                        var importedArtifact = await importService.ImportAsync(
                            dependency,
                            cachedFile,
                            operationContext.CancellationToken);
                        results.Add(new ModelSyncResult(dependency, cachedFile, importedArtifact, null));
                    },
                    context);
            }
            catch (Exception exception)
            {
                if (activeDependency != null)
                {
                    results.Add(new ModelSyncResult(activeDependency, null, null, exception.Message));
                    context.QueueOnMainThread(() =>
                    {
                        UnityEngine.Debug.LogError($"HF Hub sync failed for {activeDependency.PackageId}/{activeDependency.ModelId}: {exception.Message}");
                        Fail(state, new SyncReport(discoveryReport.Issues, results));
                    });
                    return false;
                }

                throw;
            }

            context.QueueOnMainThread(() => Complete(new SyncReport(discoveryReport.Issues, results)));
            return true;
        }

        /// <summary>
        /// Finalizes a successful background sync run, removes persisted state, and refreshes visible editor status.
        /// </summary>
        private static void Complete(SyncReport report)
        {
            Runner.RemoveProgressIfExists();
            var consoleReport = EditorOperations.BuildSyncConsoleReport(report);
            if (report.FailureCount > 0 || report.DiscoveryIssues.Count > 0)
            {
                UnityEngine.Debug.LogWarning(consoleReport);
            }
            else
            {
                UnityEngine.Debug.Log(consoleReport);
            }

            ProjectSettingsProvider.Refresh();
            NotifySyncStateChanged();
        }

        /// <summary>
        /// Leaves persisted sync state in place so the remaining dependency queue can be resumed after reload or retry.
        /// </summary>
        private static void Fail(PersistentItemQueueState state, SyncReport report)
        {
            Runner.SaveState(state);
            Runner.RemoveProgressIfExists();
            UnityEngine.Debug.LogWarning(EditorOperations.BuildSyncConsoleReport(report));
            ProjectSettingsProvider.Refresh();
            NotifySyncStateChanged();
        }

        /// <summary>
        /// Builds short user-facing transfer text from byte progress.
        /// </summary>
        private static string BuildProgressDescription(DiscoveredModelDependency dependency, DownloadProgress progress)
        {
            if (progress.TotalBytes.HasValue && progress.TotalBytes.Value > 0)
            {
                return $"Downloading {dependency.ModelId} ({progress.DownloadedBytes / 1024f / 1024f:0.0}/{progress.TotalBytes.Value / 1024f / 1024f:0.0} MB)";
            }

            return $"Downloading {dependency.ModelId} ({progress.DownloadedBytes / 1024f / 1024f:0.0} MB)";
        }

        /// <summary>
        /// Returns the persisted sync-state path stored under `Library/` so Unity does not treat it as package content.
        /// </summary>
        private static string GetStatePath()
        {
            return Path.Combine(Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..")), "Library", StateDirectoryName, StateFileName);
        }

        /// <summary>
        /// Uses a stable package/model key so persisted job state can be matched back onto newly discovered dependencies.
        /// </summary>
        private static string GetDependencyKey(DiscoveredModelDependency dependency)
        {
            return $"{dependency.PackageId}:{dependency.ModelId}";
        }

        internal static bool MatchesRepository(DiscoveredModelDependency dependency, string repository, string revision)
        {
            return dependency != null &&
                string.Equals(dependency.Repository, repository, StringComparison.Ordinal) &&
                string.Equals(dependency.Revision, revision, StringComparison.Ordinal);
        }

        private static void NotifySyncStateChanged()
        {
            SyncStateChanged?.Invoke();
        }
    }
}
