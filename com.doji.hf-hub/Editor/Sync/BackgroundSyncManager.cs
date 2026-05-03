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

        internal static readonly HashSet<DependencyReasonCode> CorruptionHealCodes = new()
        {
            DependencyReasonCode.RawCacheMetadataInvalid,
            DependencyReasonCode.RawCacheMetadataMismatch,
            DependencyReasonCode.RawCacheRevisionRefUnreadable,
        };

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
            StartOrResumeInternal(_ => true, SyncOperationMode.Full, allowResumePersistedState: true);
        }

        /// <summary>
        /// Starts a new background sync for dependencies that match one predicate.
        /// </summary>
        public static void StartOrResume(Func<DiscoveredModelDependency, bool> dependencyFilter, SyncOperationMode mode = SyncOperationMode.Full)
        {
            StartOrResumeInternal(dependencyFilter, mode, allowResumePersistedState: true);
        }

        private static void StartOrResumeInternal(
            Func<DiscoveredModelDependency, bool> dependencyFilter,
            SyncOperationMode mode,
            bool allowResumePersistedState)
        {
            if (Runner.IsRunning)
            {
                return;
            }

            var discoveryReport = EditorManifestDiscovery.DiscoverInstalledManifests();
            var pendingKeys = BuildPendingDependencyKeys(discoveryReport, dependencyFilter);
            if (pendingKeys.Length == 0)
            {
                return;
            }

            NotifySyncStateChanged();
            if (!allowResumePersistedState)
            {
                ClearPersistedStateForNewScopedSync();
            }

            Runner.StartOrResume(
                () => PersistentItemQueueState.Create(pendingKeys.Select(key => BuildStateKey(mode, key))),
                RunAsync,
                exception => UnityEngine.Debug.LogError($"HF Hub background sync failed: {exception.Message}"));
        }

        internal static string[] BuildPendingDependencyKeys(
            DiscoveryReport discoveryReport,
            Func<DiscoveredModelDependency, bool> dependencyFilter)
        {
            var statusReport = new StatusService().BuildStatusReport(discoveryReport);
            return statusReport.DependencyStatuses
                .Where(status => status.ReasonCode != DependencyReasonCode.InvalidRepository)
                .Select(status => status.Dependency)
                .Where(dependencyFilter ?? (_ => true))
                .Select(GetDependencyKey)
                .ToArray();
        }

        /// <summary>
        /// Starts background sync for one repository/revision item shown in Model Hub.
        /// </summary>
        public static void StartForRepository(string repository, string revision, SyncOperationMode mode)
        {
            StartOrResumeInternal(
                dependency => MatchesRepository(dependency, repository, revision),
                mode,
                allowResumePersistedState: false);
        }

        /// <summary>
        /// Starts a DownloadOnly sync for any dependency currently in a cache-corruption state.
        /// Safe to call redundantly; no-ops if sync is already running or no corruption is found.
        /// </summary>
        public static void StartHealCorruptionIfNeeded()
        {
            var discoveryReport = EditorManifestDiscovery.DiscoverInstalledManifests();
            var statusReport = new StatusService().BuildStatusReport(discoveryReport);
            var corruptKeys = statusReport.DependencyStatuses
                .Where(s => CorruptionHealCodes.Contains(s.ReasonCode))
                .Select(s => GetDependencyKey(s.Dependency))
                .ToHashSet(StringComparer.Ordinal);
            if (corruptKeys.Count == 0)
            {
                return;
            }

            StartOrResumeInternal(
                dependency => corruptKeys.Contains(GetDependencyKey(dependency)),
                SyncOperationMode.DownloadOnly,
                allowResumePersistedState: false);
        }

        /// <summary>
        /// Re-enters pending sync work after domain reload if a persisted sync-state file exists.
        /// Also auto-heals any cache-corruption states found at startup.
        /// </summary>
        private static void ResumeIfNeeded()
        {
            if (Runner.HasPersistedState())
            {
                StartOrResume();
                return;
            }

            HealCorruptionAtStartup();
        }

        private static void HealCorruptionAtStartup()
        {
            var discoveryReport = EditorManifestDiscovery.DiscoverInstalledManifests();
            var statusReport = new StatusService().BuildStatusReport(discoveryReport);
            var corruptKeys = statusReport.DependencyStatuses
                .Where(s => CorruptionHealCodes.Contains(s.ReasonCode))
                .Select(s => GetDependencyKey(s.Dependency))
                .ToHashSet(StringComparer.Ordinal);
            if (corruptKeys.Count == 0)
            {
                return;
            }

            StartOrResumeInternal(
                dependency => corruptKeys.Contains(GetDependencyKey(dependency)),
                SyncOperationMode.DownloadOnly,
                allowResumePersistedState: false);
        }

        /// <summary>
        /// Executes sync as a background editor task.
        /// It persists pending dependency keys before each step so a domain reload can resume from local state.
        /// </summary>
        private static async Task<bool> RunAsync(PersistentItemQueueState state, EditorPersistentOperationContext context)
        {
            var discoveryReport = EditorManifestDiscovery.DiscoverInstalledManifests();
            var pendingStates = state.PendingItemKeys
                .Select(ParseStateKey)
                .Where(item => item.HasValue)
                .Select(item => item.Value)
                .ToArray();
            var dependencies = discoveryReport.Dependencies
                .Where(dependency => pendingStates.Any(item => string.Equals(item.dependencyKey, GetDependencyKey(dependency), StringComparison.Ordinal)))
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
                    dependency => pendingStates
                        .First(item => string.Equals(item.dependencyKey, GetDependencyKey(dependency), StringComparison.Ordinal))
                        .stateKey,
                    Runner.SaveState,
                    async (dependency, index, totalCount, operationContext) =>
                    {
                        activeDependency = dependency;

                        if (revisionConflicts.TryGetValue(dependency.Repository, out var revisionConflict))
                        {
                            results.Add(ModelSyncResult.CreateRevisionConflictFailure(dependency, revisionConflict));
                            operationContext.ReportProgress(
                                totalCount == 0 ? 1f : (index + 1f) / totalCount,
                                $"Blocked {GetDependencyDisplayLabel(dependency)}");
                            return;
                        }

                        var baseProgress = totalCount == 0 ? 1f : index / (float)totalCount;
                        operationContext.ReportProgress(baseProgress, $"Preparing {GetDependencyDisplayLabel(dependency)}");

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

                        var operationMode = pendingStates
                            .First(item => string.Equals(item.dependencyKey, GetDependencyKey(dependency), StringComparison.Ordinal))
                            .mode;

                        CachedSnapshot cachedFile = null;
                        ImportedModelArtifact importedArtifact = null;
                        switch (operationMode)
                        {
                            case SyncOperationMode.DownloadOnly:
                                cachedFile = await rawCacheService.GetOrDownloadAsync(
                                    dependency,
                                    downloadProgress,
                                    operationContext.CancellationToken);
                                break;
                            case SyncOperationMode.MaterializeOnly:
                                cachedFile = rawCacheService.GetCachedOrThrow(dependency);
                                importedArtifact = await importService.ImportAsync(
                                    dependency,
                                    cachedFile,
                                    operationContext.CancellationToken);
                                break;
                            default:
                                cachedFile = await rawCacheService.GetOrDownloadAsync(
                                    dependency,
                                    downloadProgress,
                                    operationContext.CancellationToken);
                                importedArtifact = await importService.ImportAsync(
                                    dependency,
                                    cachedFile,
                                    operationContext.CancellationToken);
                                break;
                        }

                        results.Add(new ModelSyncResult(operationMode, dependency, cachedFile, importedArtifact, null));
                    },
                    context);
            }
            catch (Exception exception)
            {
                if (activeDependency != null)
                {
                    var operationMode = pendingStates
                        .FirstOrDefault(item => string.Equals(item.dependencyKey, GetDependencyKey(activeDependency), StringComparison.Ordinal))
                        .mode;
                    results.Add(new ModelSyncResult(operationMode, activeDependency, null, null, exception.Message));
                    context.QueueOnMainThread(() =>
                    {
                        UnityEngine.Debug.LogError($"HF Hub sync failed for {GetDependencyDisplayLabel(activeDependency)}: {exception.Message}");
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

        internal static string GetDependencyDisplayLabel(DiscoveredModelDependency dependency)
        {
            if (dependency == null)
            {
                return string.Empty;
            }

            var ownerLabel = ProjectManifestDiscovery.GetOwnerDisplayName(
                dependency.PackageId,
                dependency.PackageDisplayName);
            return $"{ownerLabel}/{dependency.ModelId}";
        }

        /// <summary>
        /// Returns the persisted sync-state path stored under `Library/` so Unity does not treat it as package content.
        /// </summary>
        internal static string GetPersistedStatePath()
        {
            return Path.Combine(Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..")), "Library", StateDirectoryName, StateFileName);
        }

        internal static void ClearPersistedStateForNewScopedSync()
        {
            var statePath = GetPersistedStatePath();
            if (!File.Exists(statePath))
            {
                return;
            }

            File.Delete(statePath);
        }

        private static string GetStatePath()
        {
            return GetPersistedStatePath();
        }

        /// <summary>
        /// Uses a stable package/model key so persisted job state can be matched back onto newly discovered dependencies.
        /// </summary>
        private static string GetDependencyKey(DiscoveredModelDependency dependency)
        {
            return $"{dependency.PackageId}:{dependency.ModelId}";
        }

        private static string BuildStateKey(SyncOperationMode mode, string dependencyKey)
        {
            return $"{mode}|{dependencyKey}";
        }

        private static (SyncOperationMode mode, string dependencyKey, string stateKey)? ParseStateKey(string stateKey)
        {
            if (string.IsNullOrWhiteSpace(stateKey))
            {
                return null;
            }

            var separatorIndex = stateKey.IndexOf('|');
            if (separatorIndex <= 0)
            {
                return (SyncOperationMode.Full, stateKey, stateKey);
            }

            var modeText = stateKey.Substring(0, separatorIndex);
            var dependencyKey = stateKey.Substring(separatorIndex + 1);
            return Enum.TryParse(modeText, out SyncOperationMode mode)
                ? (mode, dependencyKey, stateKey)
                : (SyncOperationMode.Full, dependencyKey, stateKey);
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
