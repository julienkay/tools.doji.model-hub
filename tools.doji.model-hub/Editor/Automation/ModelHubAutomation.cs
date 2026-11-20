using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// What agents, scripts, and CI can do with AI Model Hub without the window: read status, start and watch a sync,
    /// change a file's selection or import settings, cancel, and remove a model from the cache. It calls the same
    /// services as the window. It never opens a dialog: every call returns at once, and a step that would ask the user
    /// becomes an error that names the argument to pass.
    /// </summary>
    internal static class ModelHubAutomation
    {
        private static readonly string[] QuantizationModes = { "none", "float16", "uint8" };

        /// <summary>
        /// Replaces the project's status report, so tests control which dependencies exist. Null uses the installed
        /// manifests.
        /// </summary>
        internal static Func<StatusReport> StatusReportOverride { get; set; }

        private static StatusReport CreateStatusReport()
        {
            return StatusReportOverride?.Invoke() ?? EditorOperations.CreateStatusReport();
        }

        /// <summary>
        /// Every dependency of the project, or of one repository, with its files. Reads local state only.
        /// </summary>
        public static AutomationStatusResult GetStatus(string repository = null)
        {
            var statusReport = CreateStatusReport();
            RequireKnownRepository(statusReport, repository);
            var hubReport = new ModelHubService().BuildReport(statusReport);
            var lastRun = BackgroundSyncManager.LastRun;
            return new AutomationStatusResult
            {
                Dependencies = BuildDependencies(statusReport, hubReport, repository),
                DiscoveryIssues = statusReport.DiscoveryIssues
                    .Select(issue => new AutomationIssueInfo
                    {
                        Owner = issue.PackageId,
                        ManifestPath = issue.ManifestPath,
                        Message = issue.Message
                    })
                    .ToArray(),
                Sync = new AutomationSyncSummary
                {
                    Running = BackgroundSyncManager.IsRunning,
                    Resumable = BackgroundSyncManager.HasResumableState,
                    LastRunState = lastRun?.State
                }
            };
        }

        /// <summary>
        /// The Hugging Face cache folder and the data folder in use, what decides each, the free space on their
        /// drives, and environment variables that changed after Unity started.
        /// </summary>
        public static AutomationCacheInfoResult GetCacheInfo(IEnvironmentVariables environment = null, IDiskSpace diskSpace = null)
        {
            var report = CacheFolders.Describe(environment, diskSpace);
            return new AutomationCacheInfoResult
            {
                HubCache = ToFolderInfo(report.HubCache),
                DataRoot = ToFolderInfo(report.DataRoot),
                StaleVariables = report.StaleVariables
                    .Select(variable => new AutomationStaleVariableInfo
                    {
                        Name = variable.Name,
                        EditorValue = variable.EditorValue,
                        CurrentValue = variable.CurrentValue
                    })
                    .ToArray(),
                Warning = CacheFolders.BuildStaleMessage(report.StaleVariables)
            };
        }

        private static AutomationFolderInfo ToFolderInfo(CacheFolderInfo folder)
        {
            return new AutomationFolderInfo
            {
                Path = folder.Path,
                Source = folder.Source,
                Drive = folder.Drive?.RootPath,
                FreeBytes = folder.Drive?.AvailableBytes,
                TotalBytes = folder.Drive?.TotalBytes
            };
        }

        /// <summary>
        /// Starts a background sync of every model, or of one repository, and returns at once. When nothing starts,
        /// the result says why for each dependency.
        /// </summary>
        /// <param name="repository">A repository id, or null for every model.</param>
        /// <param name="mode"><c>full</c> (download and import, the default) or <c>download</c>.</param>
        public static AutomationSyncStartResult StartSync(string repository = null, string mode = null)
        {
            if (!SyncModes.TryParse(mode, out var operationMode))
            {
                throw new ModelHubAutomationException($"Unknown mode '{mode}'. Use 'full' (download and import) or 'download'.");
            }

            repository = NormalizeRepository(repository);
            RequireKnownRepository(CreateStatusReport(), repository);
            var start = BackgroundSyncManager.StartForTool(repository, operationMode);
            return new AutomationSyncStartResult
            {
                Started = start.Started,
                AlreadyRunning = start.AlreadyRunning,
                Resumed = start.Resumed,
                Mode = SyncModes.ToName(operationMode),
                Repository = repository,
                Message = start.Message,
                Dependencies = start.Entries.Select(ToPlanItem).ToArray(),
                Next = start.Started || start.AlreadyRunning
                    ? "Poll model_hub_sync_status until finished is true."
                    : DescribeNextStepWhenNothingStarted(start)
            };
        }

        /// <summary>
        /// Starts a sync like <see cref="StartSync"/> and completes when it ends, with the run record. For one-shot
        /// batch runs, where the process must not exit before the sync is done.
        /// </summary>
        public static async Task<AutomationSyncStartResult> StartSyncAndWaitAsync(string repository = null, string mode = null)
        {
            var result = StartSync(repository, mode);
            if (result.Started || result.AlreadyRunning)
            {
                result.Run = await BackgroundSyncManager.WaitForRunToEndAsync();
                result.Next = result.Run?.State == SyncRunStates.Succeeded
                    ? "Done. Run model_hub_status to see the result."
                    : "The sync did not succeed. Read run.results for each model's message.";
            }

            return result;
        }

        /// <summary>
        /// The running or the last sync, with progress while it runs.
        /// </summary>
        public static AutomationSyncStatusResult GetSyncStatus()
        {
            var running = BackgroundSyncManager.IsRunning;
            var run = BackgroundSyncManager.LastRun;
            var resumable = BackgroundSyncManager.HasResumableState;
            var progress = BackgroundSyncManager.CurrentProgress;
            var state = running
                ? SyncRunStates.Running
                : resumable
                    ? "resumable"
                    : run == null
                        ? "idle"
                        : run.IsFinished ? run.State : SyncRunStates.Running;
            return new AutomationSyncStatusResult
            {
                State = state,
                Running = running || state == SyncRunStates.Running,
                Finished = run != null && run.IsFinished && !running && !resumable,
                Progress = progress == null
                    ? null
                    : new AutomationSyncProgress
                    {
                        Phase = progress.Phase,
                        Repository = progress.Repository,
                        Dependency = progress.DependencyLabel,
                        DependencyIndex = progress.DependencyIndex,
                        DependencyCount = progress.DependencyCount,
                        CurrentFile = progress.CurrentFile,
                        FileIndex = progress.FileIndex,
                        FileCount = progress.FileCount,
                        DownloadedBytes = progress.DownloadedBytes,
                        TotalBytes = progress.TotalBytes,
                        Progress = progress.Progress
                    },
                Run = run,
                Next = state switch
                {
                    SyncRunStates.Running => "Poll again in a few seconds.",
                    "resumable" => "An interrupted sync saved its queue. Run model_hub_sync to resume it.",
                    "idle" => "No sync ran in this project yet. Run model_hub_sync.",
                    SyncRunStates.Succeeded => "Done. Run model_hub_status to see the result.",
                    _ => "Read run.results and run.message, fix the cause, then run model_hub_sync again."
                }
            };
        }

        /// <summary>
        /// Turns an optional file on or off, or changes an ONNX file's quantization, like the Files tab. Does not
        /// start a sync: run <see cref="StartSync"/> afterwards to download and import.
        /// </summary>
        /// <param name="enabled">Turn the optional file on or off, or null to keep it.</param>
        /// <param name="quantization"><c>none</c>, <c>float16</c>, or <c>uint8</c>, or null to keep it.</param>
        public static AutomationSetFileResult SetFile(string repository, string path, bool? enabled, string quantization)
        {
            repository = NormalizeRepository(repository);
            path = ModelDependencyDefinition.NormalizeRepoPath(path);
            if (string.IsNullOrWhiteSpace(repository))
            {
                throw new ModelHubAutomationException("Pass --repo with the model's repository id.");
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ModelHubAutomationException("Pass --path with the file's path in the repository.");
            }

            if (!enabled.HasValue && string.IsNullOrWhiteSpace(quantization))
            {
                throw new ModelHubAutomationException("Pass --enabled true|false, --quantization none|float16|uint8, or both.");
            }

            string quantizationMode = null;
            if (!string.IsNullOrWhiteSpace(quantization))
            {
                quantizationMode = quantization.Trim().ToLowerInvariant();
                if (!QuantizationModes.Contains(quantizationMode, StringComparer.Ordinal))
                {
                    throw new ModelHubAutomationException($"Unknown quantization '{quantization}'. Use none, float16, or uint8.");
                }
            }

            if (BackgroundSyncManager.IsRunning)
            {
                throw new ModelHubAutomationException("A sync is running. Wait until model_hub_sync_status reports finished, or run model_hub_cancel, then try again.");
            }

            var statusReport = CreateStatusReport();
            RequireKnownRepository(statusReport, repository);
            var dependencies = statusReport.DependencyStatuses
                .Select(status => status.Dependency)
                .Where(dependency => string.Equals(dependency.Repository, repository, StringComparison.Ordinal))
                .ToArray();
            var changes = new List<string>();

            if (enabled == true && dependencies.All(dependency => dependency.IsNotEnabled))
            {
                throw new ModelHubAutomationException(
                    $"'{repository}' is an optional model that this project did not enable. Run model_hub_set_model --repo {repository} --enabled true first.");
            }

            if (enabled.HasValue)
            {
                changes.AddRange(SetEnabled(dependencies, path, enabled.Value));
                if (changes.Count > 0)
                {
                    statusReport = CreateStatusReport();
                }
            }

            if (quantizationMode != null)
            {
                changes.AddRange(SetQuantization(statusReport, repository, path, quantizationMode));
            }

            if (changes.Count > 0)
            {
                DiscoveredDependencyCache.Invalidate();
                EditorRuntimeCatalog.Invalidate();
                ModelHubWindow.RefreshOpenWindows();
                ProjectSettingsProvider.Refresh();
                BackgroundSyncManager.NotifyStateChanged();
                statusReport = CreateStatusReport();
            }

            var hubReport = new ModelHubService().BuildReport(statusReport);
            var dependencyInfos = BuildDependencies(statusReport, hubReport, repository);
            return new AutomationSetFileResult
            {
                Repository = repository,
                Path = path,
                Changed = changes.Count > 0,
                Changes = changes.Count > 0 ? changes.ToArray() : new[] { "Nothing changed: the file already had these settings." },
                Dependencies = dependencyInfos,
                Next = dependencyInfos.Any(info => !string.Equals(info.State, nameof(DependencyState.Ready), StringComparison.Ordinal))
                    ? $"Run model_hub_sync --repo {repository} to download and import."
                    : null
            };
        }

        /// <summary>
        /// Enables or disables an optional model for this project, like the Enable and Disable buttons of the Model Hub
        /// window. The choice is stored in this project's settings. Does not start a sync: run <see cref="StartSync"/>
        /// afterwards to download and import.
        /// </summary>
        /// <param name="owner">The owner (package id) to change, or null for every owner that declares the model optional.</param>
        public static AutomationSetModelResult SetModel(string repository, bool enabled, string owner = null)
        {
            repository = NormalizeRepository(repository);
            owner = string.IsNullOrWhiteSpace(owner) ? null : owner.Trim();
            if (string.IsNullOrWhiteSpace(repository))
            {
                throw new ModelHubAutomationException("Pass --repo with the model's repository id.");
            }

            if (BackgroundSyncManager.IsRunning)
            {
                throw new ModelHubAutomationException("A sync is running. Wait until model_hub_sync_status reports finished, or run model_hub_cancel, then try again.");
            }

            var statusReport = CreateStatusReport();
            RequireKnownRepository(statusReport, repository);
            var dependencies = statusReport.DependencyStatuses
                .Select(status => status.Dependency)
                .Where(dependency => string.Equals(dependency.Repository, repository, StringComparison.Ordinal))
                .ToArray();
            if (owner != null)
            {
                var owned = dependencies.Where(dependency => string.Equals(dependency.PackageId, owner, StringComparison.Ordinal)).ToArray();
                if (owned.Length == 0)
                {
                    throw new ModelHubAutomationException(
                        $"'{owner}' does not declare '{repository}'. Owners: {string.Join(", ", dependencies.Select(dependency => dependency.PackageId).Distinct())}.");
                }

                dependencies = owned;
            }

            var optional = dependencies.Where(dependency => dependency.IsOptionalModel).ToArray();
            if (optional.Length == 0 && !enabled)
            {
                throw new ModelHubAutomationException(
                    $"'{repository}' is not an optional model of {string.Join(", ", dependencies.Select(dependency => dependency.PackageId).Distinct())}. " +
                    "Only a model that its manifest marks \"optional\": true can be disabled.");
            }

            var changes = SetModelEnabledOverride != null
                ? SetModelEnabledOverride(optional, enabled)
                : EditorOperations.SetOptionalModelEnabled(optional, enabled);
            if (changes.Length > 0)
            {
                statusReport = CreateStatusReport();
            }

            var hubReport = new ModelHubService().BuildReport(statusReport);
            var dependencyInfos = BuildDependencies(statusReport, hubReport, repository);
            return new AutomationSetModelResult
            {
                Repository = repository,
                Enabled = enabled,
                Changed = changes.Length > 0,
                Changes = changes.Length > 0
                    ? changes
                    : new[]
                    {
                        optional.Length == 0
                            ? $"Nothing changed: '{repository}' is not optional, so the project always uses it."
                            : $"Nothing changed: the model was already {(enabled ? "enabled" : "disabled")}."
                    },
                Dependencies = dependencyInfos,
                Next = enabled && dependencyInfos.Any(info => info.Enabled && !string.Equals(info.State, nameof(DependencyState.Ready), StringComparison.Ordinal))
                    ? $"Run model_hub_sync --repo {repository} to download and import."
                    : null
            };
        }

        /// <summary>
        /// Replaces how <see cref="SetModel"/> stores the change, so tests do not write this project's settings. Null
        /// uses <see cref="EditorOperations.SetOptionalModelEnabled"/>.
        /// </summary>
        internal static Func<IReadOnlyList<DiscoveredModelDependency>, bool, string[]> SetModelEnabledOverride { get; set; }

        /// <summary>
        /// Cancels the running sync. Finished files stay in the cache.
        /// </summary>
        public static AutomationCancelResult Cancel()
        {
            var canceled = BackgroundSyncManager.Cancel();
            return new AutomationCancelResult
            {
                Canceled = canceled,
                Message = canceled
                    ? "Cancel requested. Poll model_hub_sync_status until finished is true."
                    : "No sync is running."
            };
        }

        /// <summary>
        /// Removes one model's downloaded files from the shared cache on this machine. The project's import records
        /// stay. Other projects on this machine that use the model must download it again, so the call needs
        /// <paramref name="confirm"/>.
        /// </summary>
        public static AutomationRemoveCacheResult RemoveCache(string repository, bool confirm)
        {
            repository = NormalizeRepository(repository);
            if (string.IsNullOrWhiteSpace(repository))
            {
                throw new ModelHubAutomationException("Pass --repo with the model's repository id.");
            }

            var statusReport = CreateStatusReport();
            RequireKnownRepository(statusReport, repository);
            var items = new ModelHubService().BuildReport(statusReport).Items
                .Where(item => string.Equals(item.Repository, repository, StringComparison.Ordinal))
                .ToArray();
            if (items.Length == 0)
            {
                throw new ModelHubAutomationException($"Model '{repository}' has no cache entry to remove.");
            }

            if (items.Any(item => item.HasRevisionConflict))
            {
                throw new ModelHubAutomationException(
                    $"Model '{repository}' is requested at more than one revision. Resolve the manifests to one revision first.");
            }

            if (BackgroundSyncManager.IsRunning)
            {
                throw new ModelHubAutomationException("A sync is running. Wait until it finishes, or run model_hub_cancel, then try again.");
            }

            if (!confirm)
            {
                throw new ModelHubAutomationException(
                    $"Removing '{repository}' deletes its downloaded files from the shared cache, also for other projects on this machine. " +
                    "Ask the user, then pass --confirm true.");
            }

            foreach (var item in items)
            {
                EditorOperations.RemoveRawCache(item);
            }

            EditorRuntimeCatalog.Invalidate();
            ModelHubWindow.RefreshOpenWindows();
            ProjectSettingsProvider.Refresh();
            BackgroundSyncManager.NotifyStateChanged();
            return new AutomationRemoveCacheResult
            {
                Repository = repository,
                RemovedRevisions = items.Select(item => item.RequestedRevision).ToArray(),
                Message = "Removed the model's files from the shared cache. The project's import records stay."
            };
        }

        private static IEnumerable<string> SetEnabled(IReadOnlyList<DiscoveredModelDependency> dependencies, string path, bool isEnabled)
        {
            var declaring = dependencies
                .Select(dependency => (Dependency: dependency, Entry: dependency.FileEntries.FirstOrDefault(entry =>
                    string.Equals(entry.Path, path, StringComparison.Ordinal))))
                .Where(pair => pair.Entry != null)
                .ToArray();
            if (declaring.Length == 0)
            {
                throw new ModelHubAutomationException(
                    $"No manifest lists '{path}' for '{dependencies[0].Repository}'. Only files a manifest marks optional can be turned on or off. " +
                    $"Listed files: {DescribeListedFiles(dependencies)}.");
            }

            var optionalOwners = declaring.Where(pair => pair.Entry.Optional).ToArray();
            if (optionalOwners.Length == 0)
            {
                if (isEnabled)
                {
                    return Array.Empty<string>();
                }

                throw new ModelHubAutomationException(
                    $"'{path}' is a required file of {string.Join(", ", declaring.Select(pair => pair.Dependency.PackageId))}. Only optional files can be turned off.");
            }

            var source = new ProjectOptionalFileSelectionSource();
            return optionalOwners
                .Where(pair => source.SetEnabled(pair.Dependency, path, isEnabled))
                .Select(pair => $"{(isEnabled ? "Turned on" : "Turned off")} '{path}' for {pair.Dependency.PackageId}.")
                .ToArray();
        }

        private static IEnumerable<string> SetQuantization(StatusReport statusReport, string repository, string path, string quantizationMode)
        {
            var hubReport = new ModelHubService().BuildReport(statusReport);
            var items = hubReport.Items
                .Where(item => string.Equals(item.Repository, repository, StringComparison.Ordinal))
                .ToArray();
            if (items.Length > 1)
            {
                throw new ModelHubAutomationException(
                    $"Model '{repository}' is requested at more than one revision. Resolve the manifests to one revision first.");
            }

            if (!ModelDependencyDefinition.IsOnnxFile(path))
            {
                throw new ModelHubAutomationException(
                    $"Quantization applies to ONNX files only. '{path}' is loaded as downloaded.");
            }

            var item = items.FirstOrDefault();
            var file = item?.Files.FirstOrDefault(candidate => string.Equals(candidate.RepoPath, path, StringComparison.Ordinal));
            if (file == null)
            {
                var listed = statusReport.DependencyStatuses.Any(status =>
                    string.Equals(status.Dependency.Repository, repository, StringComparison.Ordinal) &&
                    status.Dependency.FileEntries.Any(entry => string.Equals(entry.Path, path, StringComparison.Ordinal)));
                throw new ModelHubAutomationException(listed
                    ? $"AI Model Hub has no file list for '{repository}' yet. Run model_hub_sync --repo {repository} --mode download first, then set the quantization."
                    : $"'{path}' is not a known file of '{repository}'. Run model_hub_status --repo {repository} for the file list.");
            }

            if (!file.SupportsImportRecipeEditing)
            {
                throw new ModelHubAutomationException(
                    $"'{path}' is not used by this project. Turn it on first with --enabled true.");
            }

            var state = FileRecipeEditState.Create(file);
            state.QuantizationMode = quantizationMode;
            if (!state.IsDirty)
            {
                return Array.Empty<string>();
            }

            EditorOperations.ApplyProjectOverrides(item, new Dictionary<string, OnnxConversionRecipe>(StringComparer.Ordinal)
            {
                [path] = state.BuildRecipe()
            });
            return new[] { $"Set quantization of '{path}' to {quantizationMode} in this project's import settings." };
        }

        private static AutomationDependencyInfo[] BuildDependencies(StatusReport statusReport, ModelHubReport hubReport, string repository)
        {
            return statusReport.DependencyStatuses
                .Where(status => repository == null || string.Equals(status.Dependency.Repository, repository, StringComparison.Ordinal))
                .Select(status => BuildDependency(status, hubReport.Items.FirstOrDefault(item =>
                    string.Equals(item.Repository, status.Dependency.Repository, StringComparison.Ordinal) &&
                    string.Equals(item.RequestedRevision, status.Dependency.Revision, StringComparison.Ordinal))))
                .ToArray();
        }

        private static AutomationDependencyInfo BuildDependency(DependencyStatus status, ModelHubItem item)
        {
            var dependency = status.Dependency;
            var license = item?.ModelCard ?? status.RawMetadata?.ModelCard;
            return new AutomationDependencyInfo
            {
                Owner = dependency.PackageId,
                OwnerName = ProjectManifestDiscovery.GetOwnerDisplayName(dependency.PackageId, dependency.PackageDisplayName),
                OwnerVersion = dependency.PackageVersion,
                ManifestPath = dependency.ManifestPath,
                Repository = dependency.Repository,
                RequestedRevision = dependency.Revision,
                Optional = dependency.IsOptionalModel,
                Enabled = dependency.IsEnabled,
                DownloadSizeBytes = item?.SourceSizeBytes,
                ResolvedCommit = status.Shared?.ResolvedCommitHash ?? status.Binding?.ResolvedCommitHash ?? status.RawMetadata?.ResolvedCommitHash ?? item?.ResolvedCommitHash,
                State = status.State.ToString(),
                ReasonCode = status.ReasonCode.ToString(),
                Message = status.MaintainerMessage,
                ImportPending = status.IsImportPending,
                PendingConversions = status.PendingConversions.ToArray(),
                License = license == null || (string.IsNullOrWhiteSpace(license.License) && string.IsNullOrWhiteSpace(license.LicenseName) && string.IsNullOrWhiteSpace(license.LicenseLink))
                    ? null
                    : new AutomationLicenseInfo { License = license.License, Name = license.LicenseName, Link = license.LicenseLink },
                Files = BuildFiles(dependency, item)
            };
        }

        /// <summary>
        /// The files a dependency lists, turned on or not, plus the files it selects beyond the list: the companion
        /// files of listed ONNX files, or every file of a whole-repository dependency.
        /// </summary>
        private static AutomationFileInfo[] BuildFiles(DiscoveredModelDependency dependency, ModelHubItem item)
        {
            var itemFiles = (item?.Files ?? Array.Empty<ModelHubFileItem>())
                .GroupBy(file => file.RepoPath, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var entries = dependency.FileEntries
                .GroupBy(entry => entry.Path, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var enabledOptional = new HashSet<string>(dependency.GetEnabledOptionalRepoPaths(), StringComparer.Ordinal);
            var selected = new HashSet<string>(
                dependency.GetSelectedRepoPaths(itemFiles.Count > 0 ? itemFiles.Keys : null),
                StringComparer.Ordinal);

            return entries.Keys
                .Concat(selected)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path =>
                {
                    entries.TryGetValue(path, out var entry);
                    itemFiles.TryGetValue(path, out var file);
                    var optional = entry?.Optional ?? false;
                    return new AutomationFileInfo
                    {
                        Path = path,
                        Optional = optional,
                        Enabled = dependency.IsEnabled && (!optional || enabledOptional.Contains(path)),
                        Cached = file?.HasRawFile ?? false,
                        Imported = file?.HasRuntimeFile ?? false,
                        SizeBytes = file?.RawSizeInBytes,
                        Quantization = file != null && file.IsOnnxFile
                            ? OnnxConversionSettings.NormalizeQuantizationMode(file.Recipe?.QuantizationMode)
                            : null
                    };
                })
                .ToArray();
        }

        private static AutomationSyncPlanItem ToPlanItem(SyncPlanEntry entry)
        {
            return new AutomationSyncPlanItem
            {
                Owner = entry.Dependency.PackageId,
                Repository = entry.Dependency.Repository,
                Revision = entry.Dependency.Revision,
                Action = entry.IsPending ? "sync" : "skip",
                Mode = entry.IsPending ? SyncModes.ToName(entry.Mode) : null,
                Reason = entry.IsPending ? null : ToReasonName(entry.SkipReason),
                Message = entry.Message
            };
        }

        internal static string ToReasonName(SyncSkipReason reason)
        {
            return reason switch
            {
                SyncSkipReason.Ready => "ready",
                SyncSkipReason.InvalidRepository => "invalid_repository",
                SyncSkipReason.SelectionRequired => "selection_required",
                SyncSkipReason.NotEnabled => "not_enabled",
                SyncSkipReason.RevisionConflict => "revision_conflict",
                SyncSkipReason.AlreadyRunning => "already_running",
                SyncSkipReason.FolderMoving => "folder_moving",
                _ => null
            };
        }

        private static string DescribeNextStepWhenNothingStarted(SyncStartResult start)
        {
            if (start.Entries.Count == 0)
            {
                return "No manifest declares a model. Nothing to do.";
            }

            if (start.Entries.All(entry => entry.SkipReason is SyncSkipReason.Ready or SyncSkipReason.NotEnabled))
            {
                return start.Entries.Any(entry => entry.SkipReason == SyncSkipReason.NotEnabled)
                    ? "Nothing to do: every enabled model is ready. Optional models that are not enabled are skipped; enable one with model_hub_set_model --enabled true, with the user's consent."
                    : "Nothing to do: every model is ready.";
            }

            return "Nothing started. Read each dependency's reason. For selection_required, ask the user which optional " +
                "file to turn on, then run model_hub_set_file --enabled true. For not_enabled, the model is optional: " +
                "run model_hub_set_model --enabled true only with the user's consent.";
        }

        private static string NormalizeRepository(string repository)
        {
            return string.IsNullOrWhiteSpace(repository) ? null : repository.Trim();
        }

        /// <summary>
        /// Refuses a repository id no manifest declares, and lists the ones that are declared.
        /// </summary>
        private static void RequireKnownRepository(StatusReport statusReport, string repository)
        {
            repository = NormalizeRepository(repository);
            if (repository == null)
            {
                return;
            }

            var known = statusReport.DependencyStatuses
                .Select(status => status.Dependency.Repository)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            if (known.Contains(repository, StringComparer.Ordinal))
            {
                return;
            }

            var caseMatch = known.FirstOrDefault(id => string.Equals(id, repository, StringComparison.OrdinalIgnoreCase));
            throw new ModelHubAutomationException(
                caseMatch != null
                    ? $"Unknown model '{repository}'. Did you mean '{caseMatch}'? Repository ids are case-sensitive."
                    : known.Length == 0
                        ? $"Unknown model '{repository}'. No manifest in this project declares a model."
                        : $"Unknown model '{repository}'. Declared models: {string.Join(", ", known)}.");
        }

        private static string DescribeListedFiles(IReadOnlyList<DiscoveredModelDependency> dependencies)
        {
            var listed = dependencies
                .SelectMany(dependency => dependency.FileEntries)
                .Select(entry => entry.Optional ? $"{entry.Path} (optional)" : entry.Path)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return listed.Length == 0 ? "none, the manifest selects the whole repository" : string.Join(", ", listed);
        }
    }
}
