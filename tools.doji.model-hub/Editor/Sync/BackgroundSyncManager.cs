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
        private const string StateDirectoryName = "Doji/ModelHub";
        private const string StateFileName = "sync-state.json";
        private const string OperationName = "AI Model Hub Sync";
        private static readonly EditorPersistentOperationRunner Runner = new(
            OperationName,
            "Syncing model dependencies",
            GetStatePath());

        private static readonly List<TaskCompletionSource<SyncRunRecord>> RunEndWaiters = new();
        private static SyncRunRecord _lastRun;
        private static bool _lastRunLoaded;
        private static bool _runActiveInThisDomain;
        private static volatile SyncProgressSnapshot _progress;

        public static event Action SyncStateChanged;

        static BackgroundSyncManager()
        {
            // Not delayCall: it does not run while the Editor is in the background.
            EditorMainThread.Post(ResumeIfNeeded);
        }

        public static bool IsRunning => Runner.IsRunning;

        /// <summary>
        /// True when an interrupted sync saved its queue and has not resumed yet. It resumes after the next domain
        /// reload, or with the next <see cref="StartOrResume()"/>.
        /// </summary>
        internal static bool HasResumableState => !Runner.IsRunning && Runner.HasPersistedState();

        /// <summary>
        /// What the running sync does right now, or null when no sync runs.
        /// </summary>
        internal static SyncProgressSnapshot CurrentProgress => Runner.IsRunning ? _progress : null;

        /// <summary>
        /// The running or the last finished sync, or null when this project never ran one. A run that a closed
        /// Editor left behind, with no queue to resume, reads as <see cref="SyncRunStates.Interrupted"/>.
        /// </summary>
        internal static SyncRunRecord LastRun
        {
            get
            {
                if (!_lastRunLoaded)
                {
                    _lastRun = SyncRunStore.Load();
                    _lastRunLoaded = true;
                }

                if (_lastRun != null &&
                    !_lastRun.IsFinished &&
                    !_runActiveInThisDomain &&
                    !Runner.IsRunning &&
                    !Runner.HasPersistedState())
                {
                    _lastRun.State = SyncRunStates.Interrupted;
                    _lastRun.FinishedAtUtc ??= DateTime.UtcNow.ToString("o");
                    _lastRun.Message ??= "The Editor stopped during the sync, and no saved queue is left to resume it.";
                    SyncRunStore.Save(_lastRun);
                }

                return _lastRun;
            }
        }

        /// <summary>
        /// Cancels the running sync. Finished files stay in the cache. Returns false when no sync runs.
        /// </summary>
        public static bool Cancel()
        {
            if (!Runner.IsRunning)
            {
                return false;
            }

            Runner.Cancel();
            return true;
        }

        /// <summary>
        /// Completes when the current run ends, with its record. Completes at once when no run is active in this
        /// domain. A domain reload drops the wait; the run itself resumes.
        /// </summary>
        internal static Task<SyncRunRecord> WaitForRunToEndAsync()
        {
            if (!_runActiveInThisDomain)
            {
                return Task.FromResult(LastRun);
            }

            var waiter = new TaskCompletionSource<SyncRunRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
            RunEndWaiters.Add(waiter);
            return waiter.Task;
        }

        /// <summary>
        /// Reads the running sync's progress from Unity's Progress API, for in-window progress UI.
        /// </summary>
        internal static bool TryGetProgress(out float progress, out string description)
        {
            progress = 0f;
            description = null;
            if (!Runner.IsRunning)
            {
                return false;
            }

            foreach (var item in Progress.EnumerateItems())
            {
                if (item.running && string.Equals(item.name, OperationName, StringComparison.Ordinal))
                {
                    progress = item.progress;
                    description = item.description;
                    return true;
                }
            }

            return true;
        }

        /// <summary>
        /// Starts a new background sync or resumes the persisted one if a previous run was interrupted.
        /// This is the single entry point used by menus, prompts, and Project Settings.
        /// </summary>
        public static SyncStartResult StartOrResume()
        {
            return Start(_ => true, SyncOperationMode.Full, allowResumePersistedState: true, scopeRepository: null, logWhenNothingStarts: true);
        }

        /// <summary>
        /// Starts a new background sync for dependencies that match one predicate.
        /// </summary>
        public static SyncStartResult StartOrResume(Func<DiscoveredModelDependency, bool> dependencyFilter, SyncOperationMode mode = SyncOperationMode.Full)
        {
            return Start(dependencyFilter, mode, allowResumePersistedState: true, scopeRepository: null, logWhenNothingStarts: true);
        }

        /// <summary>
        /// Starts background sync for one repository/revision item shown in Model Hub.
        /// </summary>
        public static SyncStartResult StartForRepository(string repository, string revision, SyncOperationMode mode)
        {
            return Start(
                dependency => MatchesRepository(dependency, repository, revision),
                mode,
                allowResumePersistedState: false,
                scopeRepository: repository,
                logWhenNothingStarts: true);
        }

        /// <summary>
        /// Starts a sync for tools: every model, or one repository at whatever revision the project requests. A
        /// sync of every model resumes a saved queue; a sync of one repository starts a new one. Logs nothing when
        /// nothing starts, because the result says why.
        /// </summary>
        internal static SyncStartResult StartForTool(string repository, SyncOperationMode mode)
        {
            return string.IsNullOrWhiteSpace(repository)
                ? Start(_ => true, mode, allowResumePersistedState: true, scopeRepository: null, logWhenNothingStarts: false)
                : Start(
                    dependency => string.Equals(dependency?.Repository, repository, StringComparison.Ordinal),
                    mode,
                    allowResumePersistedState: false,
                    scopeRepository: repository,
                    logWhenNothingStarts: false);
        }

        /// <summary>
        /// The one place that decides and starts a sync. Never returns silently: the result says whether a sync
        /// started and, for each dependency in scope, what the sync does with it or why it leaves it out.
        /// </summary>
        private static SyncStartResult Start(
            Func<DiscoveredModelDependency, bool> dependencyFilter,
            SyncOperationMode mode,
            bool allowResumePersistedState,
            string scopeRepository,
            bool logWhenNothingStarts)
        {
            var discoveryReport = EditorManifestDiscovery.DiscoverInstalledManifests();
            var plan = BuildSyncPlan(discoveryReport, dependencyFilter, mode);
            if (Runner.IsRunning)
            {
                var running = plan
                    .Select(entry => entry.IsPending
                        ? new SyncPlanEntry(entry.Status, entry.Mode, SyncSkipReason.AlreadyRunning, "A sync is already running.")
                        : entry)
                    .ToArray();
                return new SyncStartResult(false, true, false, running, "A sync is already running. Wait for it to finish, or cancel it.");
            }

            var pendingItems = plan.Where(entry => entry.IsPending).ToArray();
            if (pendingItems.Length == 0)
            {
                var message = BuildNothingToSyncMessage(plan);
                if (logWhenNothingStarts)
                {
                    UnityEngine.Debug.Log(message);
                }

                return new SyncStartResult(false, false, false, plan, message);
            }

            NotifySyncStateChanged();
            if (!allowResumePersistedState)
            {
                ClearPersistedStateForNewScopedSync();
            }

            var resumed = Runner.HasPersistedState();
            BeginRun(mode, scopeRepository, pendingItems, resumed);
            // RunAsync finishes the run itself through EditorMainThread: the runner's callbacks go through
            // EditorApplication.delayCall, which does not run while the Editor is in the background.
            Runner.StartOrResume(
                () => PersistentItemQueueState.Create(pendingItems.Select(item => BuildStateKey(item.Mode, GetDependencyKey(item.Dependency)))),
                RunAsync,
                exception => UnityEngine.Debug.LogError($"AI Model Hub background sync failed: {exception.Message}"));
            return new SyncStartResult(
                true,
                false,
                resumed,
                plan,
                resumed ? "Resumed the sync queue an earlier session saved." : "Sync started.");
        }

        internal static string[] BuildPendingDependencyKeys(
            DiscoveryReport discoveryReport,
            Func<DiscoveredModelDependency, bool> dependencyFilter)
        {
            return BuildPendingItems(discoveryReport, dependencyFilter, SyncOperationMode.Full)
                .Select(item => item.Key)
                .ToArray();
        }

        /// <summary>
        /// Dependencies a sync works on, with the mode for each. See <see cref="BuildSyncPlan"/>.
        /// </summary>
        internal static (string Key, SyncOperationMode Mode)[] BuildPendingItems(
            DiscoveryReport discoveryReport,
            Func<DiscoveredModelDependency, bool> dependencyFilter,
            SyncOperationMode mode)
        {
            return BuildSyncPlan(discoveryReport, dependencyFilter, mode)
                .Where(entry => entry.IsPending)
                .Select(entry => (GetDependencyKey(entry.Dependency), entry.Mode))
                .ToArray();
        }

        /// <summary>
        /// Decides what a sync does with each dependency that matches the filter. Models that are ready, unknown on
        /// the Hub, waiting for a file selection, or requested at conflicting revisions are left out, with the
        /// reason. A full sync of a model whose files are all cached only imports it, so converting needs no
        /// network.
        /// </summary>
        internal static SyncPlanEntry[] BuildSyncPlan(
            DiscoveryReport discoveryReport,
            Func<DiscoveredModelDependency, bool> dependencyFilter,
            SyncOperationMode mode)
        {
            var statusReport = new StatusService().BuildStatusReport(discoveryReport);
            var filter = dependencyFilter ?? (_ => true);
            return statusReport.DependencyStatuses
                .Where(status => filter(status.Dependency))
                .Select(status => BuildPlanEntry(status, mode))
                .ToArray();
        }

        private static SyncPlanEntry BuildPlanEntry(DependencyStatus status, SyncOperationMode mode)
        {
            if (status.State == DependencyState.Ready)
            {
                return new SyncPlanEntry(status, mode, SyncSkipReason.Ready, "Every selected file is downloaded and set up.");
            }

            switch (status.ReasonCode)
            {
                case DependencyReasonCode.InvalidRepository:
                    return new SyncPlanEntry(status, mode, SyncSkipReason.InvalidRepository, status.MaintainerMessage);
                case DependencyReasonCode.SelectionRequired:
                    return new SyncPlanEntry(status, mode, SyncSkipReason.SelectionRequired, status.MaintainerMessage);
                case DependencyReasonCode.RevisionConflict:
                    return new SyncPlanEntry(status, mode, SyncSkipReason.RevisionConflict, status.MaintainerMessage);
            }

            var effectiveMode = mode == SyncOperationMode.Full && status.IsImportPending ? SyncOperationMode.MaterializeOnly : mode;
            return new SyncPlanEntry(status, effectiveMode, SyncSkipReason.None, status.MaintainerMessage);
        }

        /// <summary>
        /// Explains why a sync request started nothing, one line per dependency.
        /// </summary>
        internal static string BuildNothingToSyncMessage(IReadOnlyList<SyncPlanEntry> plan)
        {
            if (plan == null || plan.Count == 0)
            {
                return "AI Model Hub: nothing to sync. No manifest declares a model in this scope.";
            }

            var lines = plan.Select(entry =>
                $"- {GetDependencyDisplayLabel(entry.Dependency)}: {DescribeSkipReason(entry.SkipReason)}. {entry.Message}".TrimEnd());
            return "AI Model Hub: nothing to sync.\n" + string.Join("\n", lines);
        }

        /// <summary>
        /// A short phrase for a skip reason, for console and tool messages.
        /// </summary>
        internal static string DescribeSkipReason(SyncSkipReason reason)
        {
            return reason switch
            {
                SyncSkipReason.Ready => "ready",
                SyncSkipReason.InvalidRepository => "not found on Hugging Face",
                SyncSkipReason.SelectionRequired => "no optional model file is turned on",
                SyncSkipReason.RevisionConflict => "requested at more than one revision",
                SyncSkipReason.AlreadyRunning => "a sync is already running",
                _ => "pending"
            };
        }

        /// <summary>
        /// Re-enters pending sync work after domain reload if a persisted sync-state file exists.
        /// Never starts new work: downloads only start from an explicit user action.
        /// </summary>
        private static void ResumeIfNeeded()
        {
            if (Runner.HasPersistedState())
            {
                Start(_ => true, SyncOperationMode.Full, allowResumePersistedState: true, scopeRepository: null, logWhenNothingStarts: false);
            }
        }

        /// <summary>
        /// Records the start of a run, or keeps the record of the run a domain reload interrupted.
        /// </summary>
        private static void BeginRun(SyncOperationMode mode, string scopeRepository, IReadOnlyList<SyncPlanEntry> pendingItems, bool resumed)
        {
            var previous = LastRun;
            _runActiveInThisDomain = true;
            _progress = null;
            if (resumed && previous != null && !previous.IsFinished)
            {
                previous.ResumeCount++;
                SyncRunStore.Save(previous);
                return;
            }

            _lastRun = new SyncRunRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                State = SyncRunStates.Running,
                Mode = SyncModes.ToName(mode),
                Repository = scopeRepository,
                StartedAtUtc = DateTime.UtcNow.ToString("o"),
                Planned = pendingItems
                    .Select(item => new SyncRunPlannedItem
                    {
                        Owner = item.Dependency.PackageId,
                        Repository = item.Dependency.Repository,
                        Revision = item.Dependency.Revision,
                        Mode = SyncModes.ToName(item.Mode)
                    })
                    .ToArray()
            };
            _lastRunLoaded = true;
            SyncRunStore.Save(_lastRun);
        }

        /// <summary>
        /// Records the end of a run and releases everyone who waits for it.
        /// </summary>
        private static void EndRun(string state, SyncReport report, string message)
        {
            var record = LastRun ?? new SyncRunRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                StartedAtUtc = DateTime.UtcNow.ToString("o")
            };
            record.State = state;
            record.FinishedAtUtc = DateTime.UtcNow.ToString("o");
            record.Message = message;
            if (report != null)
            {
                record.Results = SyncRunStore.ToResultItems(report);
                record.DiscoveryIssues = report.DiscoveryIssues
                    .Select(issue => $"{issue.PackageId}: {issue.Message}")
                    .ToArray();
            }

            _lastRun = record;
            _lastRunLoaded = true;
            _runActiveInThisDomain = false;
            _progress = null;
            SyncRunStore.Save(record);

            var waiters = RunEndWaiters.ToArray();
            RunEndWaiters.Clear();
            foreach (var waiter in waiters)
            {
                waiter.TrySetResult(record);
            }
        }

        /// <summary>
        /// Executes sync as a background editor task.
        /// It persists pending dependency keys before each step so a domain reload can resume from local state.
        /// </summary>
        private static async Task<bool> RunAsync(PersistentItemQueueState state, EditorPersistentOperationContext context)
        {
            try
            {
                return await RunQueueAsync(state, context);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                // The runner deletes the saved queue when this propagates.
                EditorMainThread.Post(OnCanceled);
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The saved queue stays, as for a failed model, so the next sync or domain reload retries.
                EditorMainThread.Post(() =>
                {
                    UnityEngine.Debug.LogError($"AI Model Hub background sync failed: {exception.Message}");
                    EndRun(SyncRunStates.Failed, null, exception.Message);
                    ProjectSettingsProvider.Refresh();
                    NotifySyncStateChanged();
                });
                return false;
            }
        }

        private static async Task<bool> RunQueueAsync(PersistentItemQueueState state, EditorPersistentOperationContext context)
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
                        var baseProgress = totalCount == 0 ? 1f : index / (float)totalCount;
                        _progress = new SyncProgressSnapshot(
                            "preparing",
                            dependency.Repository,
                            GetDependencyDisplayLabel(dependency),
                            index,
                            totalCount,
                            null,
                            0,
                            0,
                            0,
                            null,
                            baseProgress);

                        if (revisionConflicts.TryGetValue(dependency.Repository, out var revisionConflict))
                        {
                            _progress = _progress?.With(phase: "blocked");
                            results.Add(ModelSyncResult.CreateRevisionConflictFailure(dependency, revisionConflict));
                            operationContext.ReportProgress(
                                totalCount == 0 ? 1f : (index + 1f) / totalCount,
                                $"Blocked {GetDependencyDisplayLabel(dependency)}");
                            return;
                        }

                        operationContext.ReportProgress(baseProgress, $"Preparing {GetDependencyDisplayLabel(dependency)}");

                        float GetOverallProgress(DownloadProgress progress)
                        {
                            var downloadRatio = progress.TotalBytes.HasValue && progress.TotalBytes.Value > 0
                                ? progress.DownloadedBytes / (float)progress.TotalBytes.Value
                                : 0f;
                            return totalCount == 0
                                ? downloadRatio
                                : (index + downloadRatio) / totalCount;
                        }

                        var downloadProgress = new FileDownloadProgress(
                            new Progress<DownloadProgress>(progress =>
                                operationContext.ReportProgress(GetOverallProgress(progress), BuildProgressDescription(dependency, progress))),
                            (repoPath, sizeInBytes, fileIndex, fileCount) => _progress = _progress?.With(
                                phase: "downloading",
                                currentFile: repoPath,
                                fileIndex: fileIndex,
                                fileCount: fileCount,
                                downloadedBytes: 0,
                                totalBytes: sizeInBytes),
                            progress => _progress = _progress?.With(
                                downloadedBytes: progress.DownloadedBytes,
                                totalBytes: progress.TotalBytes,
                                progress: GetOverallProgress(progress)));

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
                                _progress = _progress?.With(phase: "importing", clearFile: true);
                                operationContext.ReportProgress(baseProgress, $"Converting {GetDependencyDisplayLabel(dependency)}");
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
                                _progress = _progress?.With(phase: "importing", clearFile: true);
                                operationContext.ReportProgress(
                                    totalCount == 0 ? 1f : (index + 1f) / totalCount,
                                    $"Setting up {GetDependencyDisplayLabel(dependency)}");
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
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (activeDependency != null)
                {
                    var operationMode = pendingStates
                        .FirstOrDefault(item => string.Equals(item.dependencyKey, GetDependencyKey(activeDependency), StringComparison.Ordinal))
                        .mode;
                    results.Add(new ModelSyncResult(operationMode, activeDependency, null, null, exception.Message));
                    EditorMainThread.Post(() =>
                    {
                        UnityEngine.Debug.LogError($"AI Model Hub sync failed for {GetDependencyDisplayLabel(activeDependency)}: {exception.Message}");
                        Fail(state, new SyncReport(discoveryReport.Issues, results));
                    });
                    return false;
                }

                throw;
            }

            EditorMainThread.Post(() => Complete(new SyncReport(discoveryReport.Issues, results)));
            return true;
        }

        /// <summary>
        /// Finalizes a successful background sync run and refreshes visible editor status. The runner deletes the state and the progress entry.
        /// </summary>
        private static void Complete(SyncReport report)
        {
            EditorRuntimeCatalog.Invalidate();
            var consoleReport = EditorOperations.BuildSyncConsoleReport(report);
            if (report.FailureCount > 0 || report.DiscoveryIssues.Count > 0)
            {
                UnityEngine.Debug.LogWarning(consoleReport);
            }
            else
            {
                UnityEngine.Debug.Log(consoleReport);
            }

            EndRun(
                report.FailureCount > 0 ? SyncRunStates.Failed : SyncRunStates.Succeeded,
                report,
                report.FailureCount > 0 ? $"{report.FailureCount} of {report.ModelResults.Count} models failed." : null);
            ProjectSettingsProvider.Refresh();
            NotifySyncStateChanged();
        }

        /// <summary>
        /// Refreshes editor status after the user cancelled the sync. The runner already deleted the persisted state.
        /// </summary>
        private static void OnCanceled()
        {
            UnityEngine.Debug.Log("AI Model Hub sync canceled.");
            EndRun(SyncRunStates.Canceled, null, "The sync was canceled. Finished files stay in the cache.");
            ProjectSettingsProvider.Refresh();
            NotifySyncStateChanged();
        }

        /// <summary>
        /// Leaves persisted sync state in place so the remaining dependency queue can be resumed after reload or retry.
        /// </summary>
        private static void Fail(PersistentItemQueueState state, SyncReport report)
        {
            Runner.SaveState(state);
            UnityEngine.Debug.LogWarning(EditorOperations.BuildSyncConsoleReport(report));
            EndRun(
                SyncRunStates.Failed,
                report,
                "The sync stopped at a failed model. Its queue is saved: the next sync, or the next domain reload, retries it.");
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
        internal static string GetDependencyKey(DiscoveredModelDependency dependency)
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

        /// <summary>
        /// Tells listeners that model status changed outside a sync, for example after an automatic import.
        /// </summary>
        internal static void NotifyStateChanged()
        {
            NotifySyncStateChanged();
        }

        private static void NotifySyncStateChanged()
        {
            SyncStateChanged?.Invoke();
        }
    }
}
